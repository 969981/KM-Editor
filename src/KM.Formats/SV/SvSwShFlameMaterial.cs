// SPDX-License-Identifier: GPL-3.0-only
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using KM.Formats.Models;

namespace KM.Formats.SV;

public sealed record SvSwShFlameTemplate(byte[] Model, int Core, int Mask);
public sealed record SvSwShFlameTextures(string Layer, string Noise, string Control, string White, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>Fits the S/V unlit flame surface to the native paired flame shader contract.</summary>
public static class SvSwShFlameMaterial
{
    public static SvSwShFlameTemplate Template(byte[] bodyModel, byte[] flameModel, byte[] sourceMaterials, byte[]? sharedMaterials = null)
    {
        var source = new ModelBuffer(sourceMaterials); var flame = Flame(source);
        if (sharedMaterials is not null)
        {
            var reference = new ModelBuffer(sharedMaterials);
            string[] Bindings(ModelBuffer data, int material) => data.Tables(material, 2, 64)
                .Select(t => data.Text(t, 0) + ":" + data.Text(t, 1)).Order(StringComparer.Ordinal).ToArray();
            if (!Bindings(source, flame).SequenceEqual(Bindings(reference, Flame(reference))))
                throw new InvalidDataException("S/V flame palettes require separate texture conversion.");
        }
        var colors = source.Tables(flame, 7, 128).ToDictionary(t => source.Text(t, 0)!, t =>
        {
            var at = source.Field(t, 1);
            return at == 0 ? new float[4] : Enumerable.Range(0, 4).Select(i => source.Float(at + 4 * i)).ToArray();
        }, StringComparer.Ordinal);
        float[] Color(string name) => colors.TryGetValue(name, out var color) && color.All(float.IsFinite)
            ? color[..3] : throw new InvalidDataException("S/V flame palette is incomplete.");
        if (!colors.GetValueOrDefault("BaseColor", []).SequenceEqual([1f, 1, 1, 1])
            || Scalar(source, flame, "LayerMaskScale1") != 1 || Scalar(source, flame, "LayerMaskScale2") != 1)
            throw new InvalidDataException("S/V flame base tint or layer weights require another adapter.");
        var native = new ModelBuffer(bodyModel); var fire = new ModelBuffer(flameModel);
        var originals = native.Tables(native.Root, 6, 128);
        var selected = new[] { "PokeFireCoreShader1", "PokeFireMaskShader1" }.Select(shader =>
            fire.Tables(fire.Root, 6, 128).Single(t => fire.Text(t, 1) == shader)).ToArray();
        var writer = new ModelTableBuilder(bodyModel); var slots = writer.Vector(native.Root, 6, originals.Length + 2);
        var bodyCopy = writer.Append(bodyModel); var fireCopy = writer.Append(flameModel);
        for (var i = 0; i < originals.Length; i++) writer.Pointer(slots[i], bodyCopy + originals[i]);
        for (var i = 0; i < selected.Length; i++) writer.Pointer(slots[originals.Length + i], fireCopy + selected[i]);
        foreach (var field in new[] { 3, 5 })
        {
            string[] Names(ModelBuffer data)
            {
                var (at, count) = data.Vector(data.Root, field, 4, 256);
                return Enumerable.Range(0, count).Select(i => data.StringAt(at + 4 * i)).ToArray();
            }
            var fireNames = Names(fire);
            var names = Names(native).Concat(fireNames).Distinct(StringComparer.Ordinal).ToArray();
            var entries = writer.Vector(native.Root, field, names.Length);
            for (var i = 0; i < names.Length; i++) writer.Pointer(entries[i], writer.String(names[i]));
            // Material program indices address the corresponding model shader list.
            // The flame donor's indices change when its list is merged with the body.
            var programField = field == 3 ? 8 : 10;
            foreach (var material in selected)
            {
                var originalIndex = checked((int)fire.Value(material, programField));
                if ((uint)originalIndex >= fireNames.Length)
                    throw new InvalidDataException("Native flame shader index is invalid.");
                writer.Int32(writer.Field(fireCopy + material, programField), Array.IndexOf(names, fireNames[originalIndex]));
            }
        }
        var palette = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            ["BaseColor0"] = Color("BaseColorLayer1"), ["BaseColor1"] = Color("BaseColorLayer1"),
            ["LayerColor0"] = Color("BaseColorLayer2"), ["LayerColor1"] = Color("BaseColorLayer2"),
            ["OnGameColor"] = [1, 1, 1], ["ConstantColor0"] = [1, 1, 1],
        };
        foreach (var material in selected)
            foreach (var field in new[] { 13, 14 })
            {
                var parameters = fire.Tables(material, field, 256); var entries = writer.Vector(fireCopy + material, field, parameters.Length);
                var parameterCopy = writer.Append(flameModel);
                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i]; var name = fire.Text(parameter, 0)!;
                    float[]? value = field == 14 ? palette.GetValueOrDefault(name) : name switch
                    {
                        "MatLinkID" or "RimStrength" or "BaseRimVal" or "LayerRimVal" or "ColorLerpValue" or "ConstantColor0Val" => [0],
                        "OnGameEmissionVal" or "OnGameColorVal" or "OnGameAlpha" => [1],
                        _ => null,
                    };
                    if (value is null) { writer.Pointer(entries[i], parameterCopy + parameter); continue; }
                    var replacement = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Floats(value) },
                        new Dictionary<int, int> { [1] = value.Length * 4 });
                    writer.Pointer(entries[i], replacement); writer.Text(replacement, 0, name);
                }
            }
        return new(writer.Finish(native.Root), originals.Length, originals.Length + 1);
    }

    public static SvSwShFlameTextures Textures(byte[] sourceMaterials, Func<string, byte[]> read, CancellationToken token = default)
    {
        var source = new ModelBuffer(sourceMaterials); var flame = Flame(source);
        var textures = source.Tables(flame, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!, StringComparer.Ordinal);
        if (textures.Count != 3 || !textures.ContainsKey("BaseColorMap") || !textures.ContainsKey("LayerMaskMap") || !textures.ContainsKey("DisplacementMap"))
            throw new InvalidDataException("S/V flame texture bindings require another adapter.");
        var sourceLayer = read(textures["LayerMaskMap"]); var layer = new ModelTextureDocument(sourceLayer); var pixels = layer.Pixels();
        if (Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 2] != 0 || pixels[i * 4 + 3] != 0))
            throw new InvalidDataException("S/V flame has unsupported additional color layers.");
        var whiteBytes = read(textures["BaseColorMap"]); var white = new ModelTextureDocument(whiteBytes);
        if (white.Pixels().Any(v => v != 255)) throw new InvalidDataException("S/V flame base color is not neutral.");
        var displacement = PreviewTexture.ReadBaseLevel(read(textures["DisplacementMap"]));
        if (displacement.Format != 0x1d01 || displacement.Width != layer.Width || displacement.Height != layer.Height)
            throw new InvalidDataException("S/V flame noise requires another texture adapter.");
        var noise = new BcDecoder().DecodeRaw(displacement.Blocks, displacement.Width, displacement.Height, CompressionFormat.Bc4);
        var height = Scalar(source, flame, "DisplacementHeight");
        if (!float.IsFinite(height) || height is <= 0 or > .11f) throw new InvalidDataException("S/V flame displacement exceeds the native approximation.");
        var noisePixels = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length / 4; i++)
        {
            var green = pixels[i * 4 + 1];
            for (var c = 0; c < 3; c++) { pixels[i * 4 + c] = green; noisePixels[i * 4 + c] = (byte)Math.Clamp((int)MathF.Round(noise[i].r * height), 0, 255); }
            pixels[i * 4 + 3] = noisePixels[i * 4 + 3] = 255;
        }
        var original = Path.GetFileNameWithoutExtension(textures["LayerMaskMap"]);
        if (!original.EndsWith("_lym", StringComparison.Ordinal)) throw new InvalidDataException("S/V flame layer identity is unsupported.");
        var layerName = original[..^3] + "col"; var noiseName = original[..^3] + "mix"; var controlName = original[..^3] + "ctl";
        byte[] Encode(string name, byte[] rgba)
        {
            var container = new ModelTextureDocument(ModelTextureIdentity.Rename(sourceLayer, original, name, linear: true));
            var mips = new byte[container.MipCount][];
            for (var mip = 0; mip < mips.Length; mip++)
            {
                var width = Math.Max(1, layer.Width >> mip); var height = Math.Max(1, layer.Height >> mip); var level = new byte[width * height * 4];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) for (var c = 0; c < 4; c++)
                {
                    var sum = 0; var count = 0;
                    for (var sy = y * layer.Height / height; sy < (y + 1) * layer.Height / height; sy++)
                        for (var sx = x * layer.Width / width; sx < (x + 1) * layer.Width / width; sx++) { sum += rgba[(sy * layer.Width + sx) * 4 + c]; count++; }
                    level[(y * width + x) * 4 + c] = (byte)((sum + count / 2) / count);
                }
                mips[mip] = level;
            }
            return container.EncodeMipImages(mips, token);
        }
        // The native shader computes clamp(control.r + (1-control.a) *
        // (layer0.r + layer1.r), 0, 1). A transparent black control keeps both
        // scrolling inputs active. Source noise perturbs the color boundary rather
        // than displacing vertices; the source silhouette is retained.
        var whiteName = Path.GetFileNameWithoutExtension(textures["BaseColorMap"]);
        return new(layerName, noiseName, controlName, whiteName, new Dictionary<string, byte[]>
        { [layerName] = Encode(layerName, pixels), [noiseName] = Encode(noiseName, noisePixels),
            [controlName] = Encode(controlName, new byte[pixels.Length]), [whiteName] = whiteBytes });
    }

    private static int Flame(ModelBuffer source)
    {
        var materials = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 0) == "fire").ToArray();
        if (materials.Length != 1 || !source.Tables(materials[0], 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["Unlit"]))
            throw new InvalidDataException("S/V flame material requires another shader adapter.");
        if (Scalar(source, materials[0], "DiscardValue") != 0 || Scalar(source, materials[0], "EmissionIntensity") != 1)
            throw new InvalidDataException("S/V flame opacity or emission requires another adapter.");
        return materials[0];
    }
    private static float Scalar(ModelBuffer source, int material, string name)
    {
        var entry = source.Tables(material, 4, 128).Single(t => source.Text(t, 0) == name);
        var at = source.Field(entry, 1); return at == 0 ? 0 : source.Float(at);
    }
}
