// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;

namespace KM.SwSh.Restoration;

/// <summary>Applies pinned converted defaults without including later project customizations.</summary>
public static class SwShRestorationBaseline
{
    public static byte[] Personal(ProjectPaths paths, byte[] native)
    {
        var contributions = Contributions(paths).Where(c => c.Writer == SwShRestorationWriter.Personal).ToArray();
        return contributions.Length == 0 ? native : SwShRestorationComposer.Compose(contributions,
            path => path == SwShRestorationComposer.PersonalPath ? native : null, enable: true).Files[SwShRestorationComposer.PersonalPath]!;
    }

    public static IReadOnlyDictionary<string, byte[]> Models(ProjectPaths paths)
    {
        var contributions = Contributions(paths).Where(c => c.Writer == SwShRestorationWriter.ModelCatalog
            || c.Writer == SwShRestorationWriter.File && c.Path.StartsWith("romfs/bin/archive/pokemon/", StringComparison.Ordinal)).ToArray();
        if (contributions.Length == 0) return new Dictionary<string, byte[]>();
        var files = SwShRestorationComposer.Compose(contributions, path =>
        {
            if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath)) throw new InvalidDataException("Restored defaults require the target base catalog.");
            var source = Path.Combine(paths.BaseRomFsPath, path[6..].Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(source);
            if (!info.Exists) return null;
            if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("Restoration baseline source exceeds its size limit.");
            return File.ReadAllBytes(source);
        }, enable: true).Files;
        return files.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value!, StringComparer.Ordinal);
    }

    private static IEnumerable<SwShRestorationContribution> Contributions(ProjectPaths paths)
    {
        var state = SwShRestorationStateStore.Read(paths).State;
        SwShRestorationReadScope.Find(paths)?.Prefetch(state.Selections.Where(s => s.Enabled).Select(s => s.PreparedHash));
        foreach (var selection in state.Selections.Where(s => s.Enabled))
        {
            var prepared = SwShRestorationStateStore.ReadPrepared(paths, selection.PreparedHash);
            if (prepared.DefinitionId != selection.DefinitionId) throw new InvalidDataException("Installed restoration defaults do not match their selection.");
            foreach (var contribution in prepared.Contributions) yield return SwShRestorationDependencies.Effective(contribution, state);
        }
    }
}
