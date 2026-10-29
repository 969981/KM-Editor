// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract> FresnelContracts(byte[] bytes)
    {
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).Contains("FresnelEffect", StringComparer.Ordinal)) continue;
            var name = source.Text(material, 0)!;
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            { var at = source.Field(p, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); });
            var textures = source.Tables(material, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!);
            if (scalars["FresnelAlphaMin"] != .8f || scalars["FresnelAlphaMax"] != 0 || scalars["FresnelAngleBias"] != .5f
                || scalars["NormalHeight"] != 1 || scalars["NormalHeight1"] != 1 || scalars["LocalSpecularProbeIntensity"] != 1
                || vectors["ParallaxUVIntensity"] != new Vector4(-.35f, -.35f, 0, 0)
                || vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0) || vectors["UVScaleOffset1"] != new Vector4(1, 1, 0, 0))
                throw new InvalidDataException("Source gem surface requires another Fresnel adapter.");
            static Vector3 Rgb(Vector4 c) => new(c.X, c.Y, c.Z);
            result.Add(name, new(name, textures["BaseColorMap1"], [Rgb(vectors["EmissionColor"]), Rgb(vectors["BaseColorLayer1"]), Vector3.Zero, Vector3.Zero])
            { FresnelNormal = textures["NormalMap"], FresnelNormal1 = textures["NormalMap1"],
                Initial = [scalars["EmissionIntensity"], scalars["EmissionIntensityLayer1"], 0, 0] });
        }
        if (result.Count != 1) throw new InvalidDataException("Source gem material is ambiguous.");
        return result;
    }
}
