// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace KM.Formats.SwSh;

/// <summary>Filters color and direction images in their declared spaces without widening small mip footprints.</summary>
public static class SwShTextureFiltering
{
    public static byte[][] Mips(byte[] pixels, int width, int height, int count, bool srgb = false, bool normals = false, bool sharpen = false)
    {
        if (pixels.Length != checked(width * height * 4) || count is < 1 or > 16)
            throw new InvalidDataException("Converted texture pixels are invalid.");
        var source = sharpen ? Detail(pixels, width, height) : pixels;
        var result = new byte[count][]; result[0] = source.ToArray();
        for (var mip = 1; mip < count; mip++)
        {
            var w = Math.Max(1, width >> mip); var h = Math.Max(1, height >> mip); var output = new byte[w * h * 4];
            var sx = width / w; var sy = height / h;
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
            {
                var color = Vector3.Zero; float alpha = 0;
                for (var dy = 0; dy < sy; dy++) for (var dx = 0; dx < sx; dx++)
                {
                    var at = ((y * sy + dy) * width + x * sx + dx) * 4;
                    var rgb = new Vector3(Decode(source[at], srgb), Decode(source[at + 1], srgb), Decode(source[at + 2], srgb));
                    var a = source[at + 3] / 255f;
                    color += normals ? rgb * 2 - Vector3.One : rgb * a; alpha += a;
                }
                color = normals ? (color.LengthSquared() > 1e-12f ? Vector3.Normalize(color) : Vector3.UnitZ) * .5f + new Vector3(.5f)
                    : alpha > 0 ? color / alpha : Vector3.Zero;
                var target = (y * w + x) * 4;
                output[target] = Encode(color.X, srgb); output[target + 1] = Encode(color.Y, srgb); output[target + 2] = Encode(color.Z, srgb);
                output[target + 3] = Encode(alpha / (sx * sy), false);
            }
            result[mip] = output;
        }
        return result;
    }

    private static byte[] Detail(byte[] source, int width, int height)
    {
        var result = source.ToArray();
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            if (source[at + 3] != 255) continue;
            for (var c = 0; c < 3; c++)
            {
                float blur = 0; var min = 255; var max = 0;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var p = (Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)) * 4;
                    var value = source[p + c]; min = Math.Min(min, value); max = Math.Max(max, value);
                    blur += value * (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1) / 16f;
                }
                var difference = source[at + c] - blur;
                // Leave low contrast texture noise alone and avoid new halos outside the local range.
                if (MathF.Abs(difference) >= 4)
                    result[at + c] = (byte)Math.Clamp((int)MathF.Round(source[at + c] + .3f * difference), min, max);
            }
        }
        return result;
    }

    private static float Decode(byte value, bool srgb)
    {
        var f = value / 255f;
        return !srgb ? f : f <= .04045f ? f / 12.92f : MathF.Pow((f + .055f) / 1.055f, 2.4f);
    }
    private static byte Encode(float value, bool srgb)
    {
        if (srgb) value = value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
        return (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    }
}
