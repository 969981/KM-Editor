// SPDX-License-Identifier: GPL-3.0-only
namespace KM.SwSh.Restoration;

internal static class SwShRestorationFamilyOrder
{
    private static readonly int[][] Families =
    [
        [1, 2, 3],
        [4, 5, 6],
        [7, 8, 9],
        [13, 14, 15],
        [16, 17, 18],
        [19, 20],
        [172, 25, 26],
        [173, 35, 36],
        [58, 59],
        [63, 64, 65],
        [69, 70, 71],
        [74, 75, 76],
        [79, 80, 199],
        [88, 89],
        [92, 93, 94],
        [95, 208],
        [100, 101],
        [120, 121],
        [123, 212],
        [129, 130],
        [147, 148, 149],
        [152, 153, 154],
        [155, 156, 157],
        [158, 159, 160],
        [179, 180, 181],
        [194, 195],
        [215, 461],
        [228, 229],
        [246, 247, 248],
        [252, 253, 254],
        [255, 256, 257],
        [258, 259, 260],
        [280, 281, 282, 475],
        [304, 305, 306],
        [307, 308],
        [309, 310],
        [318, 319],
        [322, 323],
        [333, 334],
        [353, 354],
        [433, 358],
        [361, 362, 478],
        [371, 372, 373],
        [374, 375, 376],
        [396, 397, 398],
        [427, 428],
        [443, 444, 445],
        [447, 448],
        [459, 460],
        [498, 499, 500],
        [501, 502, 503],
        [529, 530],
        [543, 544, 545],
        [548, 549],
        [559, 560],
        [570, 571],
        [602, 603, 604],
        [607, 608, 609],
        [622, 623],
        [627, 628],
        [650, 651, 652],
        [653, 654, 655],
        [656, 657, 658],
        [667, 668],
        [669, 670, 671],
        [677, 678],
        [686, 687],
        [688, 689],
        [690, 691],
        [704, 705, 706],
        [712, 713],
        [722, 723, 724],
        [739, 740],
        [767, 768],
    ];
    private static readonly IReadOnlyDictionary<int, (int Group, int Position)> Positions = Families
        .SelectMany(family => family.Select((species, position) => (species, family.Min(), position)))
        .ToDictionary(row => row.species, row => (row.Item2, row.position));

    public static IReadOnlyList<SwShRestorationCatalogEntry> Sort(IEnumerable<SwShRestorationCatalogEntry> entries) => entries
        .OrderBy(e => Positions.TryGetValue(e.SpeciesId, out var position) ? position.Group : e.SpeciesId)
        .ThenBy(e => e.Form == 0 ? 0 : 1)
        .ThenBy(e => Positions.TryGetValue(e.SpeciesId, out var position) ? position.Position : 0)
        .ThenBy(e => e.Form).ToArray();
}
