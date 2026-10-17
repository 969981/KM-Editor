// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;
using KM.Formats.PLA;

namespace KM.PLA.Models;

internal sealed record PlaModelEntry(string Model, int Species, int Form, int Gender, string? Materials, string[] Dependencies, string[] Animations);

/// <summary>Owns PLA catalog identities and exact archive member resolution.</summary>
internal sealed class PlaModelSource(PlaSourceScope scope)
{
    internal const string CatalogPath = "bin/pokemon/table/poke_resource_table.trpmcatalog";
    private readonly Dictionary<(string Path, bool Vanilla), PlaFieldArchive> archives = [];
    private readonly HashSet<(string Path, bool Vanilla)> observed = [];
    private long decodedBytes;
    private long archiveBytes;

    internal PlaModelEntry[] Catalog(bool vanilla = false)
    {
        var current = scope.Read(CatalogPath);
        var data = new ModelBuffer(vanilla ? scope.Base(CatalogPath) : current);
        var version = data.Table(data.Root, 0);
        if (version == 0 || data.Value(version, 0) != 5 || data.Value(version, 1) != 4)
            throw new InvalidDataException("The PLA model catalog version is unsupported.");
        var entries = data.Tables(data.Root, 1, 4096).Select(row =>
        {
            var identity = data.Table(row, 0);
            if (identity == 0) throw new InvalidDataException("The PLA model catalog identity is missing.");
            int Number(int field, bool wide = true) => data.Field(identity, field) is var at && at != 0 ? wide ? data.U16(at) : data.U8(at) : 0;
            string PathAt(int table, int field)
            {
                var path = data.Text(table, field) ?? throw new InvalidDataException("A PLA model catalog dependency is missing.");
                _ = ArchivePath(path);
                return path;
            }
            var model = PathAt(row, 1);
            if (!model.EndsWith(".trmdl", StringComparison.Ordinal) || Number(0) <= 0)
                throw new InvalidDataException("The PLA model catalog identity is invalid.");
            var dependencies = new List<string> { model };
            for (var field = 2; field <= 4; field++)
                if (data.Text(row, field) is { Length: > 0 }) dependencies.Add(PathAt(row, field));
            var animations = data.Tables(row, 5, 64).Select(item => PathAt(item, 1)).ToArray();
            if (animations.Any(path => !path.EndsWith(".tracn", StringComparison.Ordinal)))
                throw new InvalidDataException("The PLA animation catalog is unsupported.");
            dependencies.AddRange(animations);
            dependencies.AddRange(data.Tables(row, 6, 64).Select(item => PathAt(item, 1)));
            return new PlaModelEntry(model, Number(0), Number(1), Number(2, false), data.Text(row, 2), dependencies.ToArray(), animations);
        }).ToArray();
        if (entries.Length == 0 || entries.Select(entry => (entry.Species, entry.Form, entry.Gender)).Distinct().Count() != entries.Length
            || entries.GroupBy(entry => entry.Model, StringComparer.Ordinal).Any(group => group.Any(entry => entry.Species != group.First().Species
                || !entry.Dependencies.SequenceEqual(group.First().Dependencies))))
            throw new InvalidDataException("The PLA model catalog has missing or ambiguous identities.");
        // Several forms share the same complete resource graph. Show that asset once.
        return entries.DistinctBy(entry => entry.Model, StringComparer.Ordinal).ToArray();
    }

    internal PlaModelEntry Select(string id, bool vanilla = false)
    {
        var shiny = id.EndsWith("#shiny", StringComparison.Ordinal);
        var model = shiny ? id[..^6] : id;
        return Catalog(vanilla).SingleOrDefault(entry => entry.Model == model && (!shiny || !string.IsNullOrEmpty(entry.Materials)))
            ?? throw new InvalidDataException("Select a model from the current PLA catalog.");
    }

    internal static string ArchivePath(string path)
    {
        _ = PlaFieldArchive.HashPath(path);
        var parts = path.Split('/');
        bool Digits(string value) => value.All(char.IsAsciiDigit);
        if (parts.Length < 5 || parts[0] != "bin" || parts[1] != "pokemon" || parts[2].Length != 6
            || !parts[2].StartsWith("pm", StringComparison.Ordinal) || !Digits(parts[2][2..])
            || parts[3].Length != 12 || !parts[3].StartsWith(parts[2] + "_", StringComparison.Ordinal)
            || parts[3][9] != '_' || !Digits(parts[3][7..9]) || !Digits(parts[3][10..12]))
            throw new InvalidDataException("The PLA model resource is outside the supported catalog.");
        return "bin/archive/pokemon/" + parts[3] + ".gfpak";
    }

    internal PlaFieldArchive Archive(string path, bool vanilla = false)
    {
        if (archives.TryGetValue((path, vanilla), out var existing)) return existing;
        var current = scope.Read(path);
        var bytes = vanilla ? scope.Base(path) : current;
        archiveBytes += bytes.Length;
        if (archives.Count >= 32 || archiveBytes > 128L * 1024 * 1024)
            throw new InvalidDataException("The PLA model archive budget is exceeded.");
        var archive = new PlaFieldArchive(bytes);
        if (!vanilla && !archive.HasSameDirectory(new(scope.Base(path))))
            throw new InvalidDataException("The PLA model archive directory changed. Restore the original archive before editing.");
        archives.Add((path, vanilla), archive);
        return archive;
    }

    internal byte[] Read(string path, bool vanilla = false)
    {
        var archive = ArchivePath(path);
        scope.RequireAbsent(path);
        var bytes = Archive(archive, vanilla).Read(path, scope.Compression);
        if (observed.Add((path, vanilla)))
        {
            decodedBytes += bytes.Length;
            if (observed.Count > 4096 || decodedBytes > 256L * 1024 * 1024)
                throw new InvalidDataException("The PLA model dependency budget is exceeded.");
        }
        return bytes;
    }

    internal (PreviewClipReference[] Clips, string[] Dependencies) Animations(PlaModelEntry entry, bool vanilla = false)
    {
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in entry.Animations)
        {
            var data = new ModelBuffer(Read(path, vanilla));
            foreach (var item in data.Tables(data.Root, 0, 256))
                if (data.Text(item, 1) is { } relative && relative.EndsWith(".tracr", StringComparison.Ordinal))
                    groups.Add(TrinityPreviewReader.Resolve(path, relative));
        }
        var clips = new Dictionary<string, PreviewClipReference>(StringComparer.Ordinal);
        var identities = new HashSet<(string Group, string Name)>();
        var dependencies = new HashSet<string>(groups, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var data = new ModelBuffer(Read(group, vanilla));
            foreach (var track in data.Tables(data.Table(data.Root, 0), 0, 2048))
            {
                var name = data.Text(track, 0) ?? throw new InvalidDataException("The PLA animation name is missing.");
                if (name.Length is < 1 or > 256 || !identities.Add((group, name)))
                    throw new InvalidDataException("The PLA animation track identity is invalid.");
                var resources = data.Table(track, 3);
                if (resources == 0) continue;
                string? Dependency(int field)
                {
                    var reference = data.Table(resources, field);
                    if (reference == 0) return null;
                    var path = TrinityPreviewReader.Resolve(group, data.Text(reference, 0)
                        ?? throw new InvalidDataException("The PLA animation dependency is missing."));
                    _ = ArchivePath(path);
                    dependencies.Add(path);
                    if (dependencies.Count > 2048) throw new InvalidDataException("The PLA animation dependency budget is exceeded.");
                    return path;
                }
                var skeletal = Dependency(0);
                var material = Dependency(1);
                _ = Dependency(2); // Effect event timelines are retained, without authoring their game events.
                if (skeletal is null) continue;
                var clip = new PreviewClipReference(name, skeletal, material);
                if (clips.TryGetValue(name, out var prior) && prior != clip)
                {
                    // Ordinary and special battle groups can reuse a track name.
                    // Keep both resources selectable instead of replacing either motion.
                    name = Path.GetFileNameWithoutExtension(group) + "/" + name;
                    clip = clip with { Id = name };
                    if (clips.TryGetValue(name, out prior) && prior != clip)
                        throw new InvalidDataException("The PLA animation identity is ambiguous.");
                }
                clips[name] = clip;
                if (clips.Count > 2048) throw new InvalidDataException("The PLA animation catalog exceeds the preview budget.");
            }
        }
        return (clips.Values.OrderBy(clip => clip.Id, StringComparer.Ordinal).ToArray(), dependencies.ToArray());
    }

    internal ModelTextureResource[] Assets(string id, bool vanilla = false)
    {
        var entry = Select(id, vanilla);
        var animation = Animations(entry, vanilla);
        return ModelAssetGraph.Read(entry.Dependencies.Concat(animation.Dependencies)
            .Select(path => (path, (string?)ArchivePath(path))), (path, _) => Read(path, vanilla))
            .Select(asset => asset with { Archive = ArchivePath(asset.Id) }).ToArray();
    }
}
