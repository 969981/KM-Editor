// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

public static class SwShPreviewLayerEmission
{
    public static bool UsesProgram(ModelBuffer source, int material, string programName = KM.Formats.SV.SvSwShLayerEmission.ProgramName)
    {
        var (start, count) = source.Vector(source.Root, 5, 4, 256); var index = source.Value(material, 10);
        if (index >= count) return false;
        var name = source.StringAt(start + checked((int)index) * 4);
        return name == programName || programName == KM.Formats.SV.SvSwShLayerEmission.ProgramName
            && KM.Formats.SV.SvSwShLayerEmission.TransparentPrograms.Contains(name, StringComparer.Ordinal);
    }

}
