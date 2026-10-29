// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SV;

/// <summary>Preserves the independent lava, flame and facial material channels of Slugma and Magcargo.</summary>
public static partial class SvSwShLavaMaterial
{
    public sealed record Contract(string Name, bool Eye, string? Detail, Vector4 Uv, Vector3 Color, Vector3 Tint, float Emission, float Displacement);
    public const string ExtraEmission = "Def0_OneMin1_FreCol";
    public static readonly string[] BodyUv = ["Layer1UVScaleU", "Layer1UVScaleV", "Layer1UVTranslateU", "Layer1UVTranslateV"];
    public static readonly string[] EyeUv = ["EffectTexScaleU", "EffectTexScaleV", "EffectTexTranslateU", "EffectTexTranslateV"];
    public static bool IsLava(PreviewScene scene) => scene.Primitives.Any(p => p.Name.StartsWith("pm0218_", StringComparison.Ordinal) || p.Name.StartsWith("pm0219_", StringComparison.Ordinal));

    public static IReadOnlyDictionary<string, Contract> Contracts(byte[] bytes, PreviewScene scene)
    {
        if (!IsLava(scene)) throw new InvalidDataException("Lava materials require the qualified source model.");
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            var name = source.Text(material, 0)!;
            if (!scene.Primitives.Any(p => p.Material.Name == name)) continue;
            var eye = name.Contains("eye", StringComparison.Ordinal);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            { var at = source.Field(p, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); });
            var textures = source.Tables(material, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!);
            var fire = name == "fire";
            if (!eye && !fire && !scalars.ContainsKey("EmissionIntensityLayer1")) continue;
            var detail = textures.GetValueOrDefault(fire ? "DisplacementMap" : "OpacityMap1");
            var layer = eye ? 9 : 5;
            var color = vectors.GetValueOrDefault("EmissionColorLayer" + layer, Vector4.Zero);
            var tint = vectors.GetValueOrDefault("BaseColorLayer" + layer, Vector4.One);
            if (scalars.GetValueOrDefault("LayerMaskScale" + layer, 1) != 1 || !float.IsFinite(color.X + color.Y + color.Z))
                throw new InvalidDataException("Lava detail color needs another adapter.");
            result.Add(name, new(name, eye, detail, vectors.GetValueOrDefault("UVScaleOffset" + (fire ? 3 : 5), new(1, 1, 0, 0)),
                new(color.X, color.Y, color.Z), new(tint.X, tint.Y, tint.Z), scalars.GetValueOrDefault("EmissionIntensityLayer" + layer), fire ? scalars["DisplacementHeight"] : 0));
        }
        return result;
    }

    public static IReadOnlyDictionary<string, SvSwShLayerEmission.Contract> EmissionContracts(byte[] bytes, PreviewScene scene)
    {
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, SvSwShLayerEmission.Contract>(StringComparer.Ordinal);
        foreach (var contract in Contracts(bytes, scene).Values.Where(c => !c.Eye))
        {
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == contract.Name);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            Vector3 Color(int layer)
            {
                var p = source.Tables(material, 7, 256).SingleOrDefault(v => source.Text(v, 0) == "EmissionColorLayer" + layer);
                if (p == 0 && contract.Name == "fire") return Vector3.Zero;
                if (p == 0) throw new InvalidDataException("Lava emission color is missing.");
                var at = source.Field(p, 1); return new(source.Float(at), source.Float(at + 4), source.Float(at + 8));
            }
            var mask = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "LayerMaskMap");
            var colors = Enumerable.Range(1, 4).Select(Color).ToArray();
            result.Add(contract.Name, new(contract.Name, source.Text(mask, 1)!, colors)
            { Lava = true, MaskUnused = colors.All(c => c == Vector3.Zero),
                Initial = Enumerable.Range(1, 4).Select(i => scalars.GetValueOrDefault("EmissionIntensityLayer" + i)).ToArray() });
        }
        return result;
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts, IReadOnlyDictionary<string, string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            if (!names.TryGetValue(source.Text(material, 0)!, out var original) || !contracts.TryGetValue(original, out var contract)) continue;
            var parameters = contract.Eye ? EyeUv : BodyUv;
            var values = new Dictionary<string, float> { [ExtraEmission] = contract.Emission, ["SphereMapColorVal"] = 0 };
            for (var c = 0; c < 4; c++) values[parameters[c]] = contract.Uv[c];
            if (!contract.Eye)
            {
                values["Sin01"] = contract.Displacement;
                values["ScaleUV"] = contract.Name == "fire" ? 1 : 0;
                values["DistortionIntensity"] = contract.Name == "fire" ? 4 : 1;
            }
            foreach (var p in source.Tables(material, 13, 256))
                if (values.TryGetValue(source.Text(p, 0)!, out var value)) writer.Single(source.Field(p, 1), value);
            foreach (var p in source.Tables(material, 14, 256))
            {
                var value = source.Text(p, 0) == "EffectColor01" ? contract.Color : source.Text(p, 0) == "SphereMapColor" ? contract.Tint : (Vector3?)null;
                if (value is { } vector) for (var c = 0; c < 3; c++) writer.Single(source.Field(p, 1) + c * 4, vector[c]);
            }
        }
        return writer.Finish(source.Root);
    }

    // Keep the HDR flame palette in range during texture encoding; the shader
    // restores its authored exposure after sampling the linear color.
    public static byte[] BakingMaterials(byte[] bytes)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var material in source.Tables(source.Root, 1, 128).Where(m => source.Text(m, 0) == "fire"))
            foreach (var p in source.Tables(material, 7, 256).Where(p => source.Text(p, 0) is "BaseColorLayer1" or "BaseColorLayer2"))
                for (var c = 0; c < 3; c++) { var at = source.Field(p, 1) + c * 4; writer.Single(at, source.Float(at) / 4); }
        return writer.Finish(source.Root);
    }
}
