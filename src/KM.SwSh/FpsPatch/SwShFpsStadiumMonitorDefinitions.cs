// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.FpsPatch;

internal static partial class SwShFpsStadiumAudiencePatcher
{
    private static readonly IReadOnlyDictionary<string, ClipDefinition[]> MonitorClips =
        new Dictionary<string, ClipDefinition[]>(StringComparer.OrdinalIgnoreCase)
        {
            [FieldRoot + "a_btl35_vs01.gfpak"] = Monitor("a_btl35_vs01",
                ["a_t0301_g0110_monitor01_anime02_a_btl35_vs01_01"],
                "a_t0301_g0110_monitor01_a_btl35_vs01_02", "a_btl35_vs01_monitor01_03"),
            [FieldRoot + "a_btl36_vs02.gfpak"] = Monitor("a_btl36_vs02",
                ["a_t0301_g0110_monitor01_anime02_a_btl36_vs02"],
                "a_t0301_g0110_monitor01_a_btl36_vs02_01", "a_t0301_g0110_monitor01_a_btl36_vs02_02"),
            [FieldRoot + "a_c0101_g0210.gfpak"] = Monitor("a_c0101_g0210",
                ["a_t0301_g0110_monitor01_anime02_a_c0101_g0210"],
                "a_t0301_g0110_monitor01_anime01_a_c0101_g0210", "a_t0301_g0110_monitor01_anime02_a_c0101_g0210_01"),
            [FieldRoot + "a_c0201_g0210.gfpak"] = Monitor("a_c0201_g0210",
                ["a_t0301_g0110_monitor01_anime02_bld_a_c0201_g0210"],
                "a_t0301_g0110_monitor01_anime01_a_c0201_g0210", "a_t0301_g0110_monitor01_anime03_a_c0201_g0210"),
            [FieldRoot + "a_pl0110.gfpak"] = Monitor("a_pl0110",
                ["a_t0301_g0110_monitor01_anime03_bld_a_pl0110"],
                "a_t0301_g0110_monitor01_anime01_a_pl0110", "a_t0301_g0110_monitor01_anime02_a_pl0110"),
            [FieldRoot + "a_pl0120.gfpak"] = Monitor("a_pl0110",
                ["a_t0301_g0110_monitor01_anime03_bld_a_pl0110"],
                "a_t0301_g0110_monitor01_anime01_a_pl0110", "a_t0301_g0110_monitor01_anime02_a_pl0110"),
            [FieldRoot + "a_t0301_g0110.gfpak"] = Monitor("a_t0301_g0110",
                ["a_t0301_g0110_monitor01_monitor01", "a_t0301_g0110_monitor01_logo01"],
                "a_t0301_g0110_monitor01_1", "a_t0301_g0110_monitor01_2"),
            [FieldRoot + "a_t0401_g0110.gfpak"] = Monitor("a_t0401_g0110",
                ["a_t0301_g0110_monitor01_anime02_a_t0401_g0110_02"],
                "a_t0301_g0110_monitor01_a_t0401_g0110_01", "a_t0301_g0110_monitor01_a_t0401_g0110_02"),
            [FieldRoot + "a_t0501_g0110.gfpak"] = Monitor("a_t0501_g0110",
                ["a_t0301_g0110_monitor01_anime02_a_t0501_g0110_01"],
                "a_t0301_g0110_monitor01_anime01_a_t0501_g0110", "a_t0301_g0110_monitor01_anime02_a_t0501_g0110_02"),
            [FieldRoot + "a_t0501_g0210.gfpak"] = Monitor("a_t0501_g0210",
                ["a_t0301_g0110_monitor01_anime02_a_t0501_g0210"],
                "a_t0301_g0110_monitor01_a_t0501_g0210_1", "a_t0301_g0110_monitor01_a_t0501_g0210_2"),
            [FieldRoot + "a_t0601_g0110.gfpak"] = Monitor("a_t0601_g0110",
                ["a_t0301_g0110_monitor01_anime02_a_t0601_g0110"],
                "a_t0301_g0110_monitor01_a_t0601_g0110_01", "a_t0301_g0110_monitor01_a_t0601_g0110_02"),
            [FieldRoot + "a_t0701_g0110.gfpak"] = Monitor("a_t0701_g0110",
                ["a_t0301_g0110_monitor01_anime02_a_t0701_g0110"],
                "a_t0301_g0110_monitor01_a_t0701_g0110_01", "a_t0301_g0110_monitor01_a_t0701_g0110_02"),
            [FieldRoot + "a_t0701_g0210.gfpak"] = Monitor("a_t0701_g0210",
                ["a_t0301_g0110_monitor01_anime02_a_t0701_g0210_02"],
                "a_t0301_g0110_monitor01_a_t0701_g0210_01", "a_t0301_g0110_monitor01_a_t0701_g0210_02"),
        };

    private static ClipDefinition[] Monitor(string area, string[] scrollingMaterials,
        string mainMaterial, string secondaryMaterial)
    {
        int[] frames = [100, 30, 30, 30, 30, 45];
        return frames.Select((count, index) => new ClipDefinition(
            $"{area}_monitor01_{index:00}.gfbanm", count, [],
            index == 0 ? scrollingMaterials.Select(name => (name, "TranslateU1")).ToArray() : [])
        {
            DiscreteParameters = index is 0 or 5
                ? [(mainMaterial, "TranslateU0"), (mainMaterial, "TranslateV0"), (secondaryMaterial, "TranslateV0")]
                : [(mainMaterial, "TranslateU0"), (secondaryMaterial, "TranslateV0")],
        }).ToArray();
    }
}
