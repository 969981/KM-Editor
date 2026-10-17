// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.FpsPatch;

internal static partial class SwShFpsStadiumAudiencePatcher
{
    private const string FieldRoot = "romfs/bin/archive/field/area/other/";
    private sealed record ClipDefinition(string Name, int Frames, string[] FlipbookMaterials,
        (string Material, string Parameter)[] SmoothParameters);

    private static readonly ClipDefinition WyndonClip = new("a_pl0110_00.gfbanm", 705,
        ["a_pl0110_audience00_a_pl0110", "a_t0301_g0110_audience_poke01_a_pl0110", "a_pl0110_flag01"], []);
    private static readonly IReadOnlyDictionary<string, ClipDefinition[]> FieldArchives = CreateFieldArchives();

    internal static IReadOnlyList<string> RelativePaths { get; } = Array.AsReadOnly(FieldArchives.Keys.ToArray());
    internal static bool IsManagedPath(string relativePath) => FieldArchives.ContainsKey(relativePath);

    private static IReadOnlyDictionary<string, ClipDefinition[]> CreateFieldArchives()
    {
        var areas = new Dictionary<string, ClipDefinition[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["a_pl0110"] = [WyndonClip],
            ["a_pl0120"] = [WyndonClip],
            ["a_c0101_g0210"] = Gym("a_c0101_g0210",
                ["a_c0101_g0210_audience01", "a_c0101_g0210_flag01", "a_t0301_g0110_audience_poke01_a_c0101_g0210"]),
            ["a_c0201_g0210"] = Gym("a_c0201_g0210",
                ["a_c0201_g0210_audience01", "a_c0201_g0210_flag01", "a_t0301_g0110_audience_poke01_a_c0201_g0210"]),
            ["a_t0301_g0110"] = Gym("a_t0301_g0110",
                ["a_t0301_g0110_audience07", "a_t0301_g0110_flag01", "a_t0301_g0110_audience_poke01"]),
            ["a_t0401_g0110"] = Gym("a_t0401_g0110",
                ["a_t0401_g0110_audience01", "a_t0401_g0110_flag01", "a_t0301_g0110_audience_poke01_a_t0401_g0110"]),
            ["a_t0501_g0110"] = Gym("a_t0501_g0110",
                ["a_t0501_g0110_audience01", "a_t0501_g0110_flag01", "a_t0301_g0110_audience_poke01a_t0501_g0110"]),
            ["a_t0501_g0210"] = Gym("a_t0501_g0210",
                ["a_t0501_g0210_audience01", "a_t0501_g0210_flag01", "a_t0501_g0210_audience_poke01"]),
            ["a_t0601_g0110"] = Gym("a_t0601_g0110",
                ["a_t0301_g0110_audience07_a_t0601_g0110", "a_t0301_g0110_flag01_a_t0601_g0110", "a_t0301_g0110_audience_poke01_a_t0601_g0110"]),
            ["a_t0701_g0110"] = Gym("a_t0701_g0110",
                ["a_t0701_g0110_audience01", "a_t0701_g0110_flag01_01", "a_t0701_g0210_audience_poke01"]),
            ["a_t0701_g0210"] = Gym("a_t0701_g0210",
                ["a_t0701_g0210_audience01", "a_t0701_g0210_flag01", "a_t0301_g0110_audience_poke01_a_t0701_g0210"]),
            ["a_btl23_t0802"] = [new("a_btl23_t0802.gfbanm", 129,
                ["a_btl23_t0802_audience01"], [("a_btl23_t0802_lighteffect00_eo_tp1", "TranslateU0")])],
            ["a_btl35_vs01"] =
            [
                VersusClip("a_btl35_vs01_01.gfbanm", 705),
                VersusClip("a_btl35_vs01_02.gfbanm", 704),
            ],
            ["a_btl36_vs02"] = [new("a_btl36_vs02_00.gfbanm", 705,
                ["a_pl0110_audience00_a_btl36_vs02", "a_t0301_g0110_audience_poke01_a_btl36_vs02", "a_pl0110_flag01_a_btl36_vs02"], [])],
        };
        return areas.ToDictionary(pair => FieldRoot + pair.Key + ".gfpak", pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static ClipDefinition[] Gym(string area, string[] materials) =>
    [
        new(area + "_00_audience_on.gfbanm", 705, materials, []),
        new(area + "_00_audience_off.gfbanm", 704, materials, []),
    ];

    private static ClipDefinition VersusClip(string name, int frames) => new(name, frames,
        ["a_btl35_vs01_audience01", "a_btl35_vs01_flag01", "a_t0301_g0110_audience_poke01_a_btl35_vs01"],
        [("a_btl35_vs01_floor06", "TranslateU1"), ("a_btl35_vs01_floor06", "TranslateV1")]);
}
