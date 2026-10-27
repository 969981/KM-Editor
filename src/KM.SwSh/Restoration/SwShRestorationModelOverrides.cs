// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

/// <summary>Rebases loose resources against the packed defaults they override.</summary>
internal static class SwShRestorationModelOverrides
{
    internal static bool IsLoose(SwShRestorationContribution item) => item.Writer == SwShRestorationWriter.File
        && item.Path.StartsWith("romfs/bin/pokemon/pm", StringComparison.Ordinal);

    internal static bool Overrides(string loose, string archive)
    {
        var stem = loose["romfs/bin/pokemon/".Length..].Split('/')[0];
        return archive == $"romfs/bin/archive/pokemon/{stem}.gfpak"
            || archive == $"romfs/bin/archive/pokemon/{stem}_rare.gfpak"
            || archive == $"romfs/bin/archive/gpokemon/{stem}.gfpak";
    }

    internal static IReadOnlyList<SwShRestorationContribution> Rebase(SwShPreparedRestoration previous,
        SwShPreparedRestoration next, SwShPreparedRestoration customized)
    {
        var old = new Resources(previous); var updated = new Resources(next);
        var originals = previous.Contributions.Where(IsLoose).ToDictionary(item => item.Path, StringComparer.Ordinal);
        var defaults = next.Contributions.Where(IsLoose).ToDictionary(item => item.Path, StringComparer.Ordinal);
        var edits = customized.Contributions.Where(IsLoose).ToDictionary(item => item.Path, StringComparer.Ordinal);
        var result = new List<SwShRestorationContribution>();
        foreach (var path in originals.Keys.Concat(defaults.Keys).Concat(edits.Keys).Distinct(StringComparer.Ordinal))
        {
            originals.TryGetValue(path, out var original); defaults.TryGetValue(path, out var replacement);
            var current = edits.TryGetValue(path, out var edit) ? edit : original;
            if (replacement?.Before is not null && (original is null || !Equal(original.Before, replacement.Before))
                || current is not null && !Equal(current.Before, original?.Before))
                throw Conflict(path, "has conflicting resource ownership");
            if (current is null) { if (replacement is not null) result.Add(replacement); continue; }
            var before = old.Find(path); var after = updated.Find(path);
            var choices = new List<byte[]?>();
            var contexts = before.Keys.Concat(after.Keys).Where(key => key != "*").Distinct(StringComparer.Ordinal).ToArray();
            foreach (var context in contexts.Length == 0 && (before.Count != 0 || after.Count != 0) ? ["*"] : contexts)
            {
                var baseline = before.GetValueOrDefault(context, before.GetValueOrDefault("*"));
                var target = after.GetValueOrDefault(context, after.GetValueOrDefault("*"));
                if (Equal(current.After, baseline)) choices.Add(target);
                else if (Equal(baseline, target) || Equal(current.After, target)) choices.Add(current.After);
                else if (baseline is not null && target is not null
                    && SwShRestorationMaterialMerge.TryMerge(path, baseline, target, current.After) is { } merged) choices.Add(merged);
                else throw Conflict(path, target is null ? "removes your edited resource" : "also changes your edited resource");
            }
            // An auxiliary loose file absent from both package versions is unrelated.
            var chosen = choices.Count == 0 ? current.After : choices[0];
            if (choices.Any(choice => !Equal(choice, chosen)))
                throw Conflict(path, "requires different loose contents for its model variants");
            if (chosen is not null) result.Add((replacement ?? current) with { After = chosen });
        }
        return result;
    }

    private sealed class Resources(SwShPreparedRestoration package)
    {
        private readonly Dictionary<string, SwShGfPackFile> packs = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Path, ulong Hash), byte[]> members = new();
        private long decodedBytes;

        internal Dictionary<string, byte[]?> Find(string path)
        {
            var direct = package.Contributions.SingleOrDefault(item => IsLoose(item) && item.Path == path);
            if (direct is not null) return new(StringComparer.Ordinal) { ["*"] = direct.After };
            var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            var catalog = package.Contributions.Where(item => item.Writer == SwShRestorationWriter.ModelCatalog)
                .Select(item => (item.Key, Row: JsonSerializer.Deserialize<SwShPokemonResourceRow>(item.After)
                    ?? throw new InvalidDataException("Model override catalog binding is missing."))).ToArray();
            foreach (var item in package.Contributions.Where(item => item.Writer == SwShRestorationWriter.File && Overrides(path, item.Path)))
            {
                if (!packs.TryGetValue(item.Path, out var pack))
                    packs.Add(item.Path, pack = SwShGfPackFile.ParseBoundedReadOnly(item.After, 4096, 64L * 1024 * 1024));
                if (!pack.TryGetUniqueFileHash(Path.GetFileName(path), out var hash))
                {
                    if (pack.TryGetFileByName(Path.GetFileName(path), 32 * 1024 * 1024, out _))
                        throw Conflict(path, "has an ambiguous packed resource name");
                    continue;
                }
                if (!members.TryGetValue((item.Path, hash), out var bytes))
                {
                    bytes = pack.ReadFileByHash(hash, 32 * 1024 * 1024);
                    decodedBytes = checked(decodedBytes + bytes.Length);
                    if (decodedBytes > 128L * 1024 * 1024) throw new InvalidDataException("Loose model update resources exceed their decoding budget.");
                    members.Add((item.Path, hash), bytes);
                }
                var contexts = catalog.Where(row => "romfs/" + row.Row.Archive == item.Path).Select(row => "catalog:" + row.Key).ToArray();
                foreach (var context in contexts.Length == 0 ? [item.Path] : contexts) result.Add(context, bytes);
            }
            return result;
        }
    }

    private static InvalidDataException Conflict(string path, string reason) => new(
        $"Asset update {reason}: {path}. Existing model edits and gameplay data are preserved.");
    private static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null
        : right is not null && left.AsSpan().SequenceEqual(right);
}
