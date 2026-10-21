using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationAssetUpdate
{
    internal sealed record Result(SwShPreparedRestoration Customized, IReadOnlyDictionary<string, byte[]?> Files);

    internal static Result Create(ProjectPaths paths, SwShPreparedRestoration previous, SwShPreparedRestoration next,
        SwShPreparedRestoration customized, SwShRestorationState state, bool enabled,
        IReadOnlySet<string> owned, Func<string, byte[]?> read)
    {
        var captured = new Dictionary<(string Path, string Key), SwShRestorationContribution>();
        if (enabled)
        {
            customized = SwShRestorationCustomizations.IncludeLooseModels(paths, customized, owned);
            foreach (var path in customized.Contributions.Select(item => item.Path).Distinct(StringComparer.Ordinal))
                if (File.Exists(Path.Combine(paths.OutputRootPath!, path)) && !owned.Contains(path))
                    throw new InvalidDataException($"Asset update cannot replace externally changed output: {path}.");
            captured = SwShRestorationComposer.Compose(customized.Contributions, read, false, captureOnly: true)
                .RetainedCustomizations.ToDictionary(item => (item.Path, item.Key));
            customized = customized with { Contributions = customized.Contributions.Select(item =>
                captured.TryGetValue((item.Path, item.Key), out var current)
                    ? SwShRestorationDependencies.Retain(item, current, state) : item).ToArray() };
        }
        var merged = Rebase(previous, next, customized);
        if (!enabled) return new(merged, new Dictionary<string, byte[]?>());
        var changes = merged.Contributions.Select(item => item with {
            Before = captured.TryGetValue((item.Path, item.Key), out var current) ? current.After : item.Before }).ToArray();
        return new(merged, SwShRestorationComposer.Compose(changes, read, true).Files);
    }

    internal static SwShPreparedRestoration Rebase(SwShPreparedRestoration previous, SwShPreparedRestoration next,
        SwShPreparedRestoration customized)
    {
        if (previous.DefinitionId != next.DefinitionId || previous.Game != next.Game
            || customized.DefinitionId != previous.DefinitionId || customized.Game != previous.Game
            || previous.DefinitionVersion != next.DefinitionVersion || previous.ConverterVersion != next.ConverterVersion
            || customized.DefinitionVersion != previous.DefinitionVersion || customized.ConverterVersion != previous.ConverterVersion)
            throw new InvalidDataException("Asset updates require matching Pokemon definitions and target games.");
        var originals = previous.Contributions.ToDictionary(item => (item.Path, item.Key));
        var replacements = next.Contributions.ToDictionary(item => (item.Path, item.Key));
        var customizations = customized.Contributions.ToDictionary(item => (item.Path, item.Key));
        if (originals.Keys.Any(key => !replacements.ContainsKey(key)))
            throw new InvalidDataException("This asset update removes an owned resource. The existing installation is preserved.");
        var modelChanged = next.Contributions.Where(IsModel).Any(item =>
            !originals.TryGetValue((item.Path, item.Key), out var original) || !Equal(original.After, item.After));
        if (modelChanged && customized.Contributions.Any(item => IsLooseModel(item)
            && (!originals.TryGetValue((item.Path, item.Key), out var original) || !Equal(original.After, item.After))))
            throw new InvalidDataException("This asset update conflicts with model overrides. Reset those model edits before updating; Pokemon assignments are preserved.");
        var merged = new List<SwShRestorationContribution>();
        foreach (var item in next.Contributions)
        {
            if (!originals.TryGetValue((item.Path, item.Key), out var original))
            {
                if (item.Before is not null || customizations.ContainsKey((item.Path, item.Key)))
                    throw new InvalidDataException("This asset update conflicts with existing resource ownership.");
                merged.Add(item);
                continue;
            }
            if (item.Writer != original.Writer || !Equal(item.Before, original.Before))
                throw new InvalidDataException("This asset update changes the original resource ownership.");
            var current = customizations.GetValueOrDefault((item.Path, item.Key), original);
            if (current.Writer != original.Writer || !Equal(current.Before, original.Before))
                throw new InvalidDataException("Retained customization ownership does not match the installed asset.");
            merged.Add(item with { After = Merge(original, item, current) });
        }
        merged.AddRange(customized.Contributions.Where(item => !originals.ContainsKey((item.Path, item.Key))
            && !replacements.ContainsKey((item.Path, item.Key))));
        return next with { Contributions = merged };
    }

    private static byte[] Merge(SwShRestorationContribution original, SwShRestorationContribution next,
        SwShRestorationContribution current)
    {
        if (Equal(current.After, original.After) || Equal(current.After, next.After)) return next.After;
        if (Equal(next.After, original.After)) return current.After;
        if (original.Writer == SwShRestorationWriter.Personal) return MergePersonal(original.After, next.After, current.After);
        if (original.Writer == SwShRestorationWriter.Learnset || original.Path.StartsWith("romfs/bin/pml/", StringComparison.Ordinal)
            || original.Path.StartsWith("romfs/bin/appli/icon_pokemon/", StringComparison.Ordinal)) return current.After;
        if (original.Writer == SwShRestorationWriter.File && original.Path.EndsWith(".gfpak", StringComparison.Ordinal))
            return MergeArchive(original.After, next.After, current.After);
        throw new InvalidDataException($"This asset update conflicts with a customized resource: {original.Path} ({original.Key}). Existing edits and Pokemon assignments are preserved.");
    }

    private static byte[] MergePersonal(byte[] previous, byte[] next, byte[] current)
    {
        if (previous.Length != SwShPersonalTable.RecordSize || next.Length != previous.Length || current.Length != previous.Length)
            throw new InvalidDataException("Updated Pokemon data has an invalid record size.");
        var result = current.ToArray();
        var widths = new Dictionary<int, int> { [0x0a] = 2, [0x0c] = 2, [0x0e] = 2, [0x10] = 2,
            [0x18] = 2, [0x1a] = 2, [0x1c] = 2, [0x1e] = 2, [0x22] = 2, [0x24] = 2, [0x26] = 2,
            [0x4c] = 4, [0x56] = 2, [0x58] = 2, [0x5a] = 2, [0x5c] = 2, [0x5e] = 2, [0xac] = 2, [0xae] = 2 };
        for (var offset = 0; offset < result.Length;)
        {
            var width = widths.GetValueOrDefault(offset, 1);
            if (current.AsSpan(offset, width).SequenceEqual(previous.AsSpan(offset, width)))
                next.AsSpan(offset, width).CopyTo(result.AsSpan(offset, width));
            offset += width;
        }
        foreach (var (offset, count) in new[] { (0x28, SwShPersonalTable.TechnicalMachineCompatibilityCount),
            (0x38, SwShPersonalTable.TypeTutorCompatibilityCount), (0x3c, SwShPersonalTable.TechnicalRecordCompatibilityCount),
            (0xa8, SwShPersonalTable.ArmorTutorCompatibilityCount) })
            for (var bit = 0; bit < count; bit++) MergeBits(offset + bit / 8, 1 << (bit % 8));
        foreach (var mask in new[] { 0x3f, 0x40, 0x80 }) MergeBits(0x21, mask);
        for (var bit = 0; bit < 12; bit += 2) MergeBits(0x0a + bit / 8, 3 << (bit % 8));
        return result;

        void MergeBits(int offset, int mask)
        {
            var chosen = (current[offset] & mask) == (previous[offset] & mask) ? next[offset] : current[offset];
            result[offset] = (byte)((result[offset] & ~mask) | (chosen & mask));
        }
    }

    private static byte[] MergeArchive(byte[] previous, byte[] next, byte[] current)
    {
        var baseline = SwShGfPackFile.ParseBoundedReadOnly(previous, 4096, 64L * 1024 * 1024);
        var updated = SwShGfPackFile.ParseBoundedReadOnly(next, 4096, 64L * 1024 * 1024);
        var customized = SwShGfPackFile.ParseBoundedReadOnly(current, 4096, 64L * 1024 * 1024);
        if (baseline.MergeMetadataIdentity != updated.MergeMetadataIdentity || baseline.MergeMetadataIdentity != customized.MergeMetadataIdentity)
            throw new InvalidDataException("This asset update changes the structure of a customized model. Reset its model edits before updating; Pokemon assignments are preserved.");
        foreach (var member in baseline.AbsoluteHashes)
        {
            var original = baseline.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024);
            var changed = customized.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024);
            if (Equal(original, changed)) continue;
            var replacement = updated.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024);
            if (!Equal(original, replacement) && !Equal(changed, replacement))
                throw new InvalidDataException("This asset update conflicts with an edited model member. Reset that model edit before updating; Pokemon assignments are preserved.");
            updated.ReplaceFileByHash(member.HashFnv1aPathFull, changed);
        }
        return updated.Write();
    }

    private static bool IsLooseModel(SwShRestorationContribution item) => item.Writer == SwShRestorationWriter.File
        && item.Path.StartsWith("romfs/bin/pokemon/pm", StringComparison.Ordinal);
    private static bool IsModel(SwShRestorationContribution item) => item.Writer == SwShRestorationWriter.ModelCatalog
        || IsLooseModel(item) || item.Writer == SwShRestorationWriter.File
        && (item.Path.StartsWith("romfs/bin/archive/pokemon/", StringComparison.Ordinal)
            || item.Path.StartsWith("romfs/bin/archive/gpokemon/", StringComparison.Ordinal));
    private static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null
        : right is not null && left.AsSpan().SequenceEqual(right);
}
