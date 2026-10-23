// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

public static class SwShPreviewLayerEmission
{
    public static bool UsesProgram(ModelBuffer source, int material)
    {
        var (start, count) = source.Vector(source.Root, 5, 4, 256); var index = source.Value(material, 10);
        return index < count && source.StringAt(start + checked((int)index) * 4) == KM.Formats.SV.SvSwShLayerEmission.ProgramName;
    }

}
