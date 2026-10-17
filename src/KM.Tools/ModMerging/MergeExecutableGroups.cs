// SPDX-License-Identifier: GPL-3.0-only
using KM.SwSh.ExeFs;

namespace KM.Tools.ModMerging;

internal static class MergeExecutableGroups
{
    internal sealed record Range(int Offset, int Length);
    internal sealed record Group(string Label, Range[] Ranges);

    internal static Group[] Read(string build, int segment = 0)
    {
        var regions = segment switch
        {
            0 => SwShExecutableMergeSupport.Regions(build),
            1 => SwShExecutableMergeSupport.RoRegions(build),
            2 => SwShExecutableMergeSupport.DataRegions(build),
            _ => throw new ArgumentOutOfRangeException(nameof(segment)),
        };
        var groups = regions.GroupBy(r => r.Owner)
            .Select(g => new Group(g.Key, g.Select(r => new Range(r.Offset & ~3, checked((r.Offset + r.Length + 3 & ~3) - (r.Offset & ~3)))).Distinct().ToArray())).ToList();
        // Shared reservations must be resolved together, never applied twice with different choices.
        for (var first = 0; first < groups.Count; first++)
            for (var second = first + 1; second < groups.Count; second++)
                if (groups[first].Ranges.Any(a => groups[second].Ranges.Any(b => a.Offset < b.Offset + b.Length && b.Offset < a.Offset + a.Length)))
                {
                    groups[first] = new(groups[first].Label + ", " + groups[second].Label, groups[first].Ranges.Concat(groups[second].Ranges).Distinct().ToArray());
                    groups.RemoveAt(second);
                    first = -1;
                    break;
                }
        return groups.ToArray();
    }
}
