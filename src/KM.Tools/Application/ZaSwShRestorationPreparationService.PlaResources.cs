// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private static IReadOnlyDictionary<string, string> PlaMotions(int species)
    {
        var result = NativeClipsForPla();
        result["eye01"] = species == 47 ? "00000" : "08000";
        return result;
    }

    private static Dictionary<string, string> NativeClipsForPla() => new(StringComparer.Ordinal)
    {
        ["ba01_landA01"] = "00152", ["ba01_landB01"] = "00153", ["ba01_landC01"] = "00155",
        ["ba10_waitA01"] = "00000", ["ba10_waitB01"] = "00000", ["ba20_buturi01"] = "00400", ["ba21_tokusyu01"] = "00450",
        ["ba30_damageS01"] = "00500", ["ba41_down01"] = "00520", ["mouth01"] = "08100", ["ba02_roar01"] = "00300",
        ["fi20_walk01"] = "00030", ["fi21_run01"] = "00100", ["kw20_drowseA01"] = "00280", ["kw20_drowseB01"] = "00281",
        ["kw20_drowseC01"] = "00282", ["kw21_sleepA01"] = "00280", ["kw21_sleepB01"] = "00281",
        ["kw30_hate01"] = "03100", ["kw32_happyA01"] = "00550", ["kw32_happyB01"] = "00000", ["kw32_happyC01"] = "00550",
        ["kw33_moveA01"] = "00150", ["kw33_moveC01"] = "00155", ["kw34_lonely01"] = "00560", ["kw35_playA01"] = "00550",
        ["kw36_mad01"] = "03100", ["kw50_eatA01"] = "00290", ["kw50_eatB01"] = "00291", ["kw50_eatC01"] = "00292", ["kw60_touch01"] = "00560",
    };
}
