using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

public static class ZaSwShFlameMaterial
{
    public static SwShBakedMaterial Bake(byte[] sourceMaterials, bool shiny, Func<string, byte[]> read, CancellationToken token)
    {
        var source = new ModelBuffer(sourceMaterials);
        var material = source.Tables(source.Root, 1, 128).Single(table => source.Text(table, 0) == "fire");
        if (!source.Tables(material, 1, 16).Select(table => source.Text(table, 0)).SequenceEqual(["Unlit"]))
            throw new InvalidDataException("Z-A flame requires another shader adapter.");
        var scalars = source.Tables(material, 4, 128).ToDictionary(table => source.Text(table, 0)!,
            table => source.Field(table, 1) is var offset && offset != 0 ? source.Float(offset) : 0);
        var vectors = source.Tables(material, 7, 128).ToDictionary(table => source.Text(table, 0)!, table =>
        {
            var offset = source.Field(table, 1);
            return new Vector4(source.Float(offset), source.Float(offset + 4), source.Float(offset + 8), source.Float(offset + 12));
        });
        if (scalars["DisplacementHeight"] != 0 || scalars["DiscardValue"] != 0 || scalars["EmissionIntensity"] != 1
            || vectors["BaseColor"] != Vector4.One || vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0))
            throw new InvalidDataException("Z-A flame displacement, opacity, or tint requires another adapter.");
        var paths = source.Tables(material, 2, 64).ToDictionary(table => source.Text(table, 0)!, table => source.Text(table, 1)!);
        if (paths.Count != 3 || !paths.ContainsKey("DisplacementMap") || new ModelTextureDocument(read(paths["BaseColorMap"])).Pixels().Any(value => value != 255))
            throw new InvalidDataException("Z-A flame textures require another adapter.");
        var maskBytes = read(paths["LayerMaskMap"]);
        var mask = new ModelTextureDocument(maskBytes);
        var pixels = mask.Pixels();
        byte Encode(float value) => (byte)Math.Clamp((int)MathF.Round((value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f) * 255), 0, 255);
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            token.ThrowIfCancellationRequested();
            var color = Vector3.One;
            for (var layer = 0; layer < 4; layer++)
            {
                var palette = vectors["BaseColorLayer" + (layer + 1)];
                var weight = Math.Clamp(pixels[offset + layer] / 255f * scalars["LayerMaskScale" + (layer + 1)], 0, 1);
                if (!float.IsFinite(weight) || !float.IsFinite(palette.X) || !float.IsFinite(palette.Y) || !float.IsFinite(palette.Z))
                    throw new InvalidDataException("Z-A flame palette is invalid.");
                color = Vector3.Lerp(color, new(palette.X, palette.Y, palette.Z), weight);
            }
            pixels[offset] = Encode(color.X); pixels[offset + 1] = Encode(color.Y); pixels[offset + 2] = Encode(color.Z); pixels[offset + 3] = 255;
        }
        var original = Path.GetFileNameWithoutExtension(paths["LayerMaskMap"]);
        if (!original.EndsWith("_lym", StringComparison.Ordinal)) throw new InvalidDataException("Z-A flame texture identity is unsupported.");
        var colorName = (shiny ? "zs" : "zn") + original[2..^3] + "col";
        var normalName = "nf" + original[2..^3] + "nor";
        var ambientName = "af" + original[2..^3] + "amb";
        byte[] Image(string name, int width, int height, byte[] image, bool srgb)
        {
            var allocation = SwShTextureAllocation.Create(maskBytes, width, height, srgb);
            var document = new ModelTextureDocument(ModelTextureIdentity.Rename(allocation, original, name, linear: !srgb));
            var levels = SwShTextureFiltering.Mips(image, width, height, document.MipCount, srgb);
            return document.EncodeMipImages(levels, token);
        }
        var normal = Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 128, 128, 255, 255 }).ToArray();
        var ambient = Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 0, 0, 128, 255 }).ToArray();
        return new(colorName, Image(colorName, mask.Width, mask.Height, pixels, true), normalName, Image(normalName, 4, 4, normal, false),
            ambientName, Image(ambientName, 4, 4, ambient, false), 1, 0);
    }

    public static byte[] Emission(byte[] model, string materialName = "fire", float intensity = 1, float gain = 1)
    {
        if (!float.IsFinite(intensity) || intensity is < 0 or > 1 || !float.IsFinite(gain) || gain is < 1 or > 128)
            throw new InvalidDataException("Native emission values exceed the supported range.");
        var source = new ModelBuffer(model); var output = model.ToArray();
        var material = source.Tables(source.Root, 6, 128).Single(table => source.Text(table, 0) == materialName);
        foreach (var (name, value) in new[] { ("EmissionMaskUse", 1), ("SwitchEmissionMaskTexUV", 0) })
        {
            var parameter = source.Tables(material, 12, 256).Single(table => source.Text(table, 0) == name);
            var offset = source.Field(parameter, 1);
            if (offset == 0) throw new InvalidDataException("Native emission switch is missing.");
            output[offset] = (byte)value;
        }
        var parameters = new List<(string Name, float Value)> { ("EmissionMaskVal", intensity), ("OnGameEmissionVal", gain) };
        if (materialName == "fire") parameters.AddRange(new[] { ("RimStrength", 0f), ("SpecularScale", 0f) });
        foreach (var (name, value) in parameters)
        {
            var parameter = source.Tables(material, 13, 256).Single(table => source.Text(table, 0) == name);
            var offset = source.Field(parameter, 1);
            if (offset == 0) throw new InvalidDataException("Native emission parameter is missing.");
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(offset, 4), value);
        }
        return output;
    }
}
