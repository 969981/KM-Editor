using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

public sealed record ZaSwShEmissionTexture(string Name, byte[] Bytes, float Gain);

public static class ZaSwShEmissionMaterial
{
    public static byte[] KeepAnimationLocal(byte[] globalAnimation, string materialName)
    {
        var source = new ModelBuffer(globalAnimation); var writer = new ModelTableBuilder(globalAnimation);
        var material = source.Tables(source.Table(source.Root, 2), 0, 128).Single(table => source.Text(table, 0) == materialName);
        var parameters = source.Tables(material, 2, 256).Where(table => source.Text(table, 0) is not ("EmissionMaskVal" or "OnGameEmissionVal")).ToArray();
        var slots = writer.Vector(material, 2, parameters.Length); var copy = writer.Append(globalAnimation);
        for (var index = 0; index < parameters.Length; index++) writer.Pointer(slots[index], copy + parameters[index]);
        return writer.Finish(source.Root);
    }

    public static ZaSwShEmissionTexture Bake(byte[] sourceMaterials, string materialName, SwShBakedMaterial baked, Func<string, byte[]> read, CancellationToken token)
    {
        var source = new ModelBuffer(sourceMaterials);
        var material = source.Tables(source.Root, 1, 128).Single(table => source.Text(table, 0) == materialName);
        var vectors = source.Tables(material, 7, 128).ToDictionary(table => source.Text(table, 0)!, table =>
        {
            var offset = source.Field(table, 1);
            return new Vector4(source.Float(offset), source.Float(offset + 4), source.Float(offset + 8), source.Float(offset + 12));
        });
        if (vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0)) throw new InvalidDataException("Z-A emission coordinates require another adapter.");
        var scalars = source.Tables(material, 4, 128).ToDictionary(table => source.Text(table, 0)!,
            table => source.Field(table, 1) is var offset && offset != 0 ? source.Float(offset) : 0);
        if (scalars["LayerMaskScale2"] != 1 || scalars["EmissionIntensityLayer2"] != 0)
            throw new InvalidDataException("Z-A emission weights require another adapter.");
        var path = source.Tables(material, 2, 64).Single(table => source.Text(table, 0) == "LayerMaskMap");
        var mask = new ModelTextureDocument(read(source.Text(path, 1)!)); var maskPixels = mask.Pixels();
        var color = new ModelTextureDocument(baked.Color); var colorPixels = color.Pixels();
        var emission = vectors["EmissionColorLayer2"];
        var values = new float[colorPixels.Length]; var gain = 1f;
        float Linear(byte value) => value / 255f <= .04045f ? value / 255f / 12.92f : MathF.Pow((value / 255f + .055f) / 1.055f, 2.4f);
        for (var row = 0; row < color.Height; row++) for (var column = 0; column < color.Width; column++)
        {
            var offset = (row * color.Width + column) * 4;
            var weight = maskPixels[((row * mask.Height / color.Height) * mask.Width + column * mask.Width / color.Width) * 4 + 1] / 255f;
            for (var channel = 0; channel < 3; channel++)
            {
                var value = weight == 0 ? 0 : weight * (1 + emission[channel] / Math.Max(.01f, Linear(colorPixels[offset + channel])));
                if (!float.IsFinite(value) || value is < 0 or > 128) throw new InvalidDataException("Z-A emission gain exceeds its native adapter.");
                values[offset + channel] = value; gain = Math.Max(gain, value);
            }
        }
        var pixels = new byte[colorPixels.Length];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            for (var channel = 0; channel < 3; channel++) pixels[offset + channel] = (byte)Math.Clamp((int)MathF.Round(values[offset + channel] / gain * 255), 0, 255);
            pixels[offset + 3] = 255;
        }
        var name = "em" + baked.ColorName[2..];
        var allocation = SwShTextureAllocation.Create(baked.Color, color.Width, color.Height, false);
        var document = new ModelTextureDocument(ModelTextureIdentity.Rename(allocation, baked.ColorName, name, linear: true));
        return new(name, document.EncodeMipImages(SwShTextureFiltering.Mips(pixels, color.Width, color.Height, document.MipCount), token), gain);
    }
}
