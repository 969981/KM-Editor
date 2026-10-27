// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

/// <summary>Moves the legacy icon claim off the native Runerigus resource.</summary>
internal sealed record SwShRestorationIconMigration(SwShRestorationContribution LegacyIcon,
    SwShRestorationContribution LegacyRegistry, SwShRestorationContribution Icon)
{
    private const string LegacyPath = "romfs/bin/appli/icon_pokemon/poke_icon_0979_00s_n.bntx";

    internal static SwShRestorationIconMigration? Find(SwShPreparedRestoration previous, SwShPreparedRestoration next)
    {
        if (previous.DefinitionId != "annihilape") return null;
        var legacy = previous.Contributions.SingleOrDefault(item => item.Path == LegacyPath);
        if (legacy is null || next.Contributions.Any(item => item.Path == LegacyPath)) return null;
        var registry = previous.Contributions.SingleOrDefault(item => item.Writer == SwShRestorationWriter.Icons && item.Key == "979");
        var icon = next.Contributions.SingleOrDefault(item => item.Path == SwShRestorationComposer.IconPath(SwShRestorationSpecies.Annihilape));
        var nextRegistry = next.Contributions.SingleOrDefault(item => item.Writer == SwShRestorationWriter.Icons && item.Key == "1010");
        if (legacy.Writer != SwShRestorationWriter.File || legacy.Before is null || registry is null
            || !IsIdentity(registry.After, 979) || icon is null || icon.Writer != SwShRestorationWriter.File || icon.Before is not null
            || nextRegistry is null || nextRegistry.Before is not null || !IsIdentity(nextRegistry.After, 1010))
            throw new InvalidDataException("The legacy icon ownership migration is incomplete or ambiguous.");
        foreach (var (package, resource) in new[] { (previous, 979u), (next, 1010u) })
        {
            var personal = package.Contributions.SingleOrDefault(item => item.Writer == SwShRestorationWriter.Personal && item.Key == "1081");
            if (personal?.After.Length != SwShPersonalTable.RecordSize
                || BinaryPrimitives.ReadUInt32LittleEndian(personal.After.AsSpan(0x4c)) != resource)
                throw new InvalidDataException("The legacy icon migration has an inconsistent resource route.");
        }
        return new(legacy, registry, icon);
    }

    internal bool Retires(SwShRestorationContribution item) => item == LegacyIcon || item == LegacyRegistry;

    internal SwShRestorationContribution Transfer(SwShRestorationContribution item,
        IReadOnlyDictionary<(string Path, string Key), SwShRestorationContribution> customized)
    {
        if (item.Path != Icon.Path) return item;
        var current = customized.GetValueOrDefault((LegacyIcon.Path, LegacyIcon.Key), LegacyIcon);
        if (current.Writer != LegacyIcon.Writer || current.Before is null || !current.Before.AsSpan().SequenceEqual(LegacyIcon.Before))
            throw new InvalidDataException("The legacy icon customization has conflicting ownership.");
        return current.After.AsSpan().SequenceEqual(LegacyIcon.After) ? item : item with { After = current.After };
    }

    internal void RestoreNative(ProjectPaths paths, Func<string, byte[]?> read, IDictionary<string, byte[]?> files,
        bool enabled, IReadOnlySet<string> owned)
    {
        var native = ReadNative(paths, LegacyPath);
        var registry = ReadNative(paths, SwShRestorationComposer.IconsPath);
        if (!native.AsSpan().SequenceEqual(LegacyIcon.Before) || registry.Length % 8 != 0
            || Count(registry, 979) != 1 || Count(registry, 1010) != 0
            || File.Exists(Path.Combine(paths.BaseRomFsPath!, Icon.Path[6..])))
            throw new InvalidDataException("Native resources do not match the legacy icon ownership migration.");
        var personal = read(SwShRestorationComposer.PersonalPath)
            ?? throw new InvalidDataException("Personal data is missing for the icon ownership migration.");
        foreach (var records in new[] { ReadNative(paths, SwShRestorationComposer.PersonalPath), personal })
        {
            if (records.Length % SwShPersonalTable.RecordSize != 0)
                throw new InvalidDataException("Personal data is invalid for the icon ownership migration.");
            for (var row = 0; row < records.Length / SwShPersonalTable.RecordSize; row++)
                if (row != SwShRestorationSpecies.AnnihilapeRow
                    && BinaryPrimitives.ReadUInt32LittleEndian(records.AsSpan(row * SwShPersonalTable.RecordSize + 0x4c)) == 1010)
                    throw new InvalidDataException("Another Pokemon claims the updated icon resource.");
        }
        // A disabled legacy installation already restored the native file. Do not
        // replace any subsequent edits to Runerigus while updating disabled assets.
        if (enabled) files[LegacyPath] = native;
        var currentRegistry = files.TryGetValue(SwShRestorationComposer.IconsPath, out var pending) ? pending
            : read(SwShRestorationComposer.IconsPath);
        if (currentRegistry is null || currentRegistry.Length % 8 != 0 || Count(currentRegistry, 979) > 1)
            throw new InvalidDataException("The native icon registry is missing or ambiguous.");
        if (Count(currentRegistry, 979) == 0)
        {
            if (!owned.Contains(SwShRestorationComposer.IconsPath))
                throw new InvalidDataException("The legacy icon registry was changed externally.");
            files[SwShRestorationComposer.IconsPath] = currentRegistry.Concat(LegacyRegistry.After).ToArray();
        }
    }

    private static bool IsIdentity(byte[] row, ushort species) => row.Length == 8
        && BinaryPrimitives.ReadUInt16LittleEndian(row) == species && row.AsSpan(2).IndexOfAnyExcept((byte)0) < 0;
    private static int Count(byte[] registry, ushort species) => Enumerable.Range(0, registry.Length / 8)
        .Count(index => IsIdentity(registry.AsSpan(index * 8, 8).ToArray(), species));

    private static byte[] ReadNative(ProjectPaths paths, string relative)
    {
        using var stream = File.OpenRead(Path.Combine(paths.BaseRomFsPath!, relative[6..]));
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Native icon migration input exceeds its file budget.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
}
