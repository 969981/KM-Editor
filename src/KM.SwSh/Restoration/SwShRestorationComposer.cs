// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using KM.Formats.Audio;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

public enum SwShRestorationWriter { File, Personal, Learnset, Icons, ModelCatalog, Camp, Voices }
public sealed record SwShRestorationContribution(string Path, SwShRestorationWriter Writer, string Key, byte[]? Before, byte[] After);
public sealed record SwShRestorationComposition(IReadOnlyDictionary<string, byte[]?> Files,
    IReadOnlyList<SwShRestorationContribution> RetainedCustomizations);

/// <summary>Composes semantic contributions once per shared file against the actual effective state.</summary>
public static class SwShRestorationComposer
{
    public const string PersonalPath = "romfs/bin/pml/personal/personal_total.bin";
    public const string LearnsetPath = "romfs/bin/pml/waza_oboe/wazaoboe_total.bin";
    public const string IconsPath = "romfs/bin/appli/icon_pokemon/pokecaplist.bin";
    public const string CatalogPath = "romfs/bin/pokemon/table/poke_resource_table.gfbpmcatalog";
    public const string CampPath = "romfs/bin/archive/pokemon_data/pokecamp.gfpak";
    public const string VoicesPath = "romfs/bin/sound/NX64/wwise/bank/Common.bnk";

    /// <summary>Checks presence against immutable source buffers owned by one catalog load.</summary>
    internal sealed class PresenceValidator
    {
        private const int MaximumCampCacheBytes = 16 * 1024 * 1024;
        private const int MaximumCampCacheEntries = 16384;
        private byte[]? campSource;
        private SwShGfPackFile? camp;
        private byte[]? voiceSource;
        private HashSet<uint> media = [];
        private Dictionary<(byte Type, uint Id), int> objects = [];

        internal bool HasAllContributions(IReadOnlyList<SwShRestorationContribution> contributions, Func<string, byte[]?> readEffective)
            => ComposeCore(contributions, readEffective, false, false, null, true, this).RetainedCustomizations.Count == contributions.Count;

        internal SwShGfPackFile ReadCamp(byte[] source)
        {
            if (ReferenceEquals(source, campSource)) return camp!;
            campSource = null;
            camp = null;
            if (source.Length <= MaximumCampCacheBytes)
            {
                try
                {
                    camp = SwShGfPackFile.ParseBoundedReadOnly(source, MaximumCampCacheEntries, MaximumCampCacheBytes);
                    campSource = source;
                    return camp;
                }
                catch (InvalidDataException)
                {
                    // The retention budget must not exclude otherwise valid archives.
                }
            }
            return SwShGfPackFile.Parse(source);
        }

        internal void ReadVoices(byte[] source)
        {
            if (ReferenceEquals(source, voiceSource)) return;
            voiceSource = null;
            media.Clear(); objects.Clear();
            var bank = new SoundBankDocument(source);
            foreach (var item in bank.Media) media.Add(item.Id);
            foreach (var item in bank.Objects)
            {
                var key = (item.Type, item.Id);
                objects[key] = objects.GetValueOrDefault(key) + 1;
            }
            // Retain only identities, never the parsed document or its copied audio payloads.
            voiceSource = source;
        }

        internal bool HasVoice(byte? type, uint id)
        {
            if (type is null) return media.Contains(id);
            var count = objects.GetValueOrDefault((type.Value, id));
            if (count > 1) throw new InvalidDataException("Owned voice object identity is ambiguous.");
            return count == 1;
        }
    }

    public static SwShRestorationComposition Compose(IReadOnlyList<SwShRestorationContribution> contributions,
        Func<string, byte[]?> readEffective, bool enable, bool allowOwnedCustomizations = false,
        IReadOnlySet<string>? ownedPaths = null, bool captureOnly = false)
        => ComposeCore(contributions, readEffective, enable, allowOwnedCustomizations, ownedPaths, captureOnly, null);

    private static SwShRestorationComposition ComposeCore(IReadOnlyList<SwShRestorationContribution> contributions,
        Func<string, byte[]?> readEffective, bool enable, bool allowOwnedCustomizations,
        IReadOnlySet<string>? ownedPaths, bool captureOnly, PresenceValidator? presenceValidator)
    {
        if (contributions.Count is < 1 or > 10000) throw new InvalidDataException("Restoration contribution count is invalid.");
        var outputs = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        var retained = new List<SwShRestorationContribution>();
        foreach (var group in contributions.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToArray(); var kind = items[0].Writer;
            if (items.Any(i => i.Writer != kind) || items.Select(i => i.Key).Distinct(StringComparer.Ordinal).Count() != items.Length)
                throw new InvalidDataException("Restoration contributions overlap or use conflicting writers.");
            ValidatePath(group.Key, kind);
            var current = readEffective(group.Key);
            if (kind == SwShRestorationWriter.File)
            {
                if (items.Length != 1 || items[0].Key.Length != 0) throw new InvalidDataException("Whole file contribution is ambiguous.");
                outputs.Add(group.Key, Select(items[0], current)); continue;
            }
            if (current is null) throw new InvalidDataException($"Shared restoration source is missing: {group.Key}");
            switch (kind)
            {
                case SwShRestorationWriter.Personal:
                case SwShRestorationWriter.Learnset:
                {
                    var size = kind == SwShRestorationWriter.Personal ? SwShPersonalTable.RecordSize : 260;
                    if (current.Length % size != 0) throw new InvalidDataException("Shared Pokemon table has an invalid length.");
                    var result = current.ToArray();
                    foreach (var item in items)
                    {
                        var row = Number(item.Key);
                        if (!SwShRestorationForms.IsRegisteredRow(row) || row >= result.Length / size || item.After.Length != size || item.Before?.Length != size)
                            throw new InvalidDataException("Restoration record identity or length is invalid.");
                        Select(item, current.AsSpan(row * size, size).ToArray())!.CopyTo(result, row * size);
                    }
                    outputs.Add(group.Key, result); break;
                }
                case SwShRestorationWriter.Icons:
                {
                    if (current.Length % 8 != 0) throw new InvalidDataException("Icon registry length is invalid.");
                    var rows = Enumerable.Range(0, current.Length / 8).Select(i => current.AsSpan(i * 8, 8).ToArray()).ToList();
                    foreach (var item in items)
                    {
                        var identity = IconIdentity(item.Key); var matches = rows.Where(r => r.AsSpan().SequenceEqual(identity)).ToArray();
                        if (matches.Length > 1 || item.After.Length != 8 || !item.After.AsSpan().SequenceEqual(identity))
                            throw new InvalidDataException("Restored icon identity is ambiguous.");
                        var replacement = Select(item, matches.SingleOrDefault());
                        if (matches.Length == 1) { var at = rows.IndexOf(matches[0]); if (replacement is null) rows.RemoveAt(at); else rows[at] = replacement; }
                        else if (replacement is not null) rows.Add(replacement);
                    }
                    outputs.Add(group.Key, rows.SelectMany(r => r).ToArray()); break;
                }
                case SwShRestorationWriter.Camp:
                {
                    var pack = presenceValidator?.ReadCamp(current) ?? SwShGfPackFile.Parse(current);
                    var writes = new Dictionary<string, byte[]>(); var removals = new List<string>();
                    foreach (var item in items)
                    {
                        if (!item.Key.StartsWith("bin/pokemon_data/pokecamp/", StringComparison.Ordinal) || item.Key.Contains("..", StringComparison.Ordinal))
                            throw new InvalidDataException("Camp member identity is invalid.");
                        var hash = SwShGfPackFile.HashMemberPath(item.Key);
                        var exists = pack.AbsoluteHashes.Any(h => h.HashFnv1aPathFull == hash);
                        if (presenceValidator is not null)
                        {
                            if (exists) pack.ValidateFileByHash(hash, 1024 * 1024);
                            Select(item, exists ? [] : null);
                            continue;
                        }
                        var payload = exists ? pack.ReadFileByHash(hash, 1024 * 1024) : null;
                        var replacement = Select(item, payload);
                        if (replacement is null) { if (payload is not null) removals.Add(item.Key); } else writes.Add(item.Key, replacement);
                    }
                    outputs.Add(group.Key, captureOnly ? current : pack.WithMembers(writes, removals).Write()); break;
                }
                case SwShRestorationWriter.ModelCatalog:
                {
                    var catalog = new SwShPokemonResourceCatalog(current); var removals = new List<SwShPokemonResourceIdentity>(); var additions = new List<SwShPokemonResourceRow>();
                    foreach (var item in items)
                    {
                        var row = JsonSerializer.Deserialize<SwShPokemonResourceRow>(item.After) ?? throw new InvalidDataException("Restored catalog entry is missing.");
                        if (CatalogKey(row.Identity) != item.Key) throw new InvalidDataException("Restored catalog identity does not match its key.");
                        var existing = catalog.Identities.Contains(row.Identity) ? JsonSerializer.SerializeToUtf8Bytes(catalog.Read(row.Identity)) : null;
                        var replacement = Select(item, existing);
                        if (existing is not null) removals.Add(row.Identity);
                        if (replacement is not null) additions.Add(JsonSerializer.Deserialize<SwShPokemonResourceRow>(replacement)!);
                    }
                    outputs.Add(group.Key, captureOnly ? current : catalog.Compose(additions, removals)); break;
                }
                case SwShRestorationWriter.Voices:
                {
                    var bank = presenceValidator is null ? new SoundBankDocument(current) : null;
                    presenceValidator?.ReadVoices(current);
                    var addObjects = new List<SoundBankObject>(); var removeObjects = new List<SoundBankObject>();
                    var addMedia = new List<SoundBankMedia>(); var removeMedia = new List<SoundBankMedia>();
                    foreach (var item in items)
                    {
                        var (type, id) = VoiceIdentity(item.Key);
                        if (presenceValidator is not null)
                        {
                            Select(item, presenceValidator.HasVoice(type, id) ? [] : null);
                            continue;
                        }
                        if (type is null)
                        {
                            var existing = bank!.Media.SingleOrDefault(m => m.Id == id); var replacement = Select(item, existing?.Bytes);
                            if (existing is not null) removeMedia.Add(existing); if (replacement is not null) addMedia.Add(new(id, replacement));
                        }
                        else
                        {
                            var matches = bank!.Objects.Where(o => o.Type == type.Value && o.Id == id).ToArray();
                            if (matches.Length > 1) throw new InvalidDataException("Owned voice object identity is ambiguous.");
                            var existing = matches.SingleOrDefault(); var replacement = Select(item, existing?.Payload);
                            if (existing is not null) removeObjects.Add(existing); if (replacement is not null) addObjects.Add(new(type.Value, id, replacement));
                        }
                    }
                    outputs.Add(group.Key, captureOnly ? current : bank!.Compose(addObjects, addMedia, removeObjects, removeMedia)); break;
                }
                default: throw new InvalidDataException("Restoration writer is not registered.");
            }
        }
        return new(outputs, retained);

        byte[]? Select(SwShRestorationContribution item, byte[]? current)
        {
            if (captureOnly)
            {
                if (current is not null) retained.Add(item with { After = current });
                return current;
            }
            var expected = enable ? item.Before : item.After; var desired = enable ? item.After : item.Before;
            if (Equal(current, desired)) return desired;
            if (!Equal(current, expected))
            {
                if (!allowOwnedCustomizations || current is null || ownedPaths is not null && !ownedPaths.Contains(item.Path))
                    throw new InvalidDataException($"Restoration contribution conflicts with the current project: {item.Path} ({item.Key}).");
                if (enable) return current;
            }
            if (!enable && current is not null) retained.Add(item with { After = current });
            return desired;
        }
    }
    public static string IconKey(int species, int form = 0) => form == 0 ? species.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{species}:{form}:0:0");
    private static byte[] IconIdentity(string key)
    {
        var parts = key.Split(':');
        if (parts.Length is not (1 or 4)) throw new InvalidDataException("Icon identity is invalid.");
        var bytes = new byte[8];
        for (var i = 0; i < parts.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), checked((ushort)Number(parts[i])));
        return bytes;
    }
    public static string CatalogKey(SwShPokemonResourceIdentity identity) => string.Create(CultureInfo.InvariantCulture,
        $"{identity.Species}:{identity.Form}:{identity.Gender}:{(identity.Shiny ? 1 : 0)}");
    private static bool Equal(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private static (byte? Type, uint Id) VoiceIdentity(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2 || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new InvalidDataException("Voice contribution identity is invalid.");
        return (parts[0] == "media" ? null : checked((byte)Number(parts[0])), id);
    }
    private static int Number(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
        ? result : throw new InvalidDataException("Restoration record key is invalid.");
    internal static void ValidatePath(string path, SwShRestorationWriter kind)
    {
        _ = new KM.Core.Semantics.RelativeOutputPath(path);
        var expected = kind switch
        {
            SwShRestorationWriter.Personal => PersonalPath, SwShRestorationWriter.Learnset => LearnsetPath, SwShRestorationWriter.Icons => IconsPath,
            SwShRestorationWriter.ModelCatalog => CatalogPath, SwShRestorationWriter.Camp => CampPath, SwShRestorationWriter.Voices => VoicesPath, _ => null,
        };
        if (expected is not null && path != expected) throw new InvalidDataException("Restoration writer cannot address this output.");
        if (kind == SwShRestorationWriter.File && !(path.StartsWith("romfs/bin/archive/pokemon/", StringComparison.Ordinal)
            || path.StartsWith("romfs/bin/archive/gpokemon/", StringComparison.Ordinal) || path.StartsWith("romfs/bin/appli/icon_pokemon/poke_icon_", StringComparison.Ordinal)
            || path.StartsWith("romfs/bin/pml/evolution/evo_", StringComparison.Ordinal) || path.StartsWith("romfs/bin/pml/tamagowaza/tamagowaza_", StringComparison.Ordinal)
            || path.StartsWith("romfs/bin/pokemon/pm", StringComparison.Ordinal)))
            throw new InvalidDataException("Restoration file target is not registered.");
    }
}
