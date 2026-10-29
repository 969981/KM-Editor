// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SV;
using KM.Formats.ZA;

namespace KM.Formats.Models;

/// <summary>Preview bindings for the emitted restoration shader contracts.</summary>
internal static class SwShPreviewRestorationMaterial
{
    public static int Kind(ModelBuffer data, int material)
    {
        if (SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.FresnelProgramName)) return 1;
        if (SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.LavaProgramName)) return 2;
        if (!SwShPreviewLayerEmission.UsesProgram(data, material, ZaSwShEyelidMaterial.ProgramName)) return 0;
        return data.Tables(data.Root, 6, 256).Any(m => SwShPreviewLayerEmission.UsesProgram(data, m, SvSwShLayerEmission.LavaProgramName)) ? 3 : 4;
    }

    public static PreviewSurface Read(ModelBuffer data, int material, PreviewSurface surface, int kind)
    {
        if (kind == 0) return surface;
        var scalars = data.Tables(material, 13, 256).ToDictionary(p => data.Text(p, 0)!, p => data.Field(p, 1) is var at && at != 0 ? data.Float(at) : 0, StringComparer.Ordinal);
        var vectors = data.Tables(material, 14, 256).ToDictionary(p => data.Text(p, 0)!, p =>
        { var at = data.Field(p, 1); return new[] { data.Float(at), data.Float(at + 4), data.Float(at + 8), 0f }; }, StringComparer.Ordinal);
        var values = Enumerable.Range(0, 10).Select(_ => new float[4]).ToArray();
        values[0] = [scalars.GetValueOrDefault(SvSwShLavaMaterial.ExtraEmission), scalars.GetValueOrDefault("Sin01"), scalars.GetValueOrDefault("ScaleUV"), scalars.GetValueOrDefault("DistortionIntensity", 1)];
        var uv = kind is 3 or 4 ? SvSwShLavaMaterial.EyeUv : SvSwShLavaMaterial.BodyUv;
        values[1] = uv.Select((name, i) => scalars.GetValueOrDefault(name, i < 2 ? 1 : 0)).ToArray();
        values[2] = vectors.GetValueOrDefault("EffectColor01", new float[4]);
        values[3] = vectors.GetValueOrDefault("SphereMapColor", new[] { 1f, 1f, 1f, 0f });
        if (kind is 3 or 4)
        {
            values[2][3] = scalars.GetValueOrDefault("ColorBaseU");
            values[3][3] = scalars.GetValueOrDefault("ColorBaseV");
        }
        for (var i = 0; i < 4; i++) values[4 + i] = vectors.GetValueOrDefault(ZaSwShEyelidMaterial.VectorNames[i], new float[4]);
        for (var i = 0; i < 6; i++) values[8 + i / 3][i % 3] = scalars.GetValueOrDefault(ZaSwShEyelidMaterial.ScalarNames[16 + i]);
        for (var i = 0; i < 6; i++) values[4 + i][3] = scalars.GetValueOrDefault(ZaSwShEyelidMaterial.ScalarNames[10 + i]);
        // These programs evaluate mask colors independently of the lit albedo.
        surface.Values[32] = [1, 1, 1, 1]; surface.Values[33] = [0, 0, 0, 0];
        return surface with { RestorationEffect = kind, RestorationValues = values };
    }

    public static string? TextureRole(int kind, string? role) => (kind == 4 ? 3 : kind, role) switch
    {
        (1, "LyCol0Tex") => "NormalMap1", (1, "EffectTex") => "RoughnessMap",
        (2, "LyCol0Tex") or (3, "EffectTex") => "RoughnessMap",
        (3, "SphereMapTex") => "EmissionMap", (3, "EmissionMaskTex") => "NormalMap1",
        (3, "LyCol0Tex") => "SpecularMaskMap", (3, "AmbientTex") => "EyelidShadowMaskMap", _ => role
    };

    public static (int Slot, int Channel) Scalar(string name, int kind)
    {
        var component = Array.IndexOf(kind is 3 or 4 ? SvSwShLavaMaterial.EyeUv : SvSwShLavaMaterial.BodyUv, name);
        if (component >= 0) return (1, component);
        if (name == SvSwShLavaMaterial.ExtraEmission) return (0, 0);
        if (kind is not (3 or 4)) return (-1, -1);
        if (name == "ColorBaseU") return (2, 3);
        if (name == "ColorBaseV") return (3, 3);
        var eye = Array.IndexOf(ZaSwShEyelidMaterial.ScalarNames, name);
        if (eye is >= 10 and < 16) return (4 + eye - 10, 3);
        return eye >= 16 ? (8 + (eye - 16) / 3, (eye - 16) % 3) : (-1, -1);
    }
}
