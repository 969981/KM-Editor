// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public sealed record SwShBakedMaterial(string ColorName, byte[] Color, string NormalName, byte[] Normal,
    string AmbientName, byte[] Ambient, double CoveredFraction, int ConflictingSamples);

/// <summary>Bakes source material channels into a native object normal and ambient shader contract.</summary>
public static class SwShMaterialBaker
{
    private sealed record Image(int Width, int Height, bool Srgb, byte[] Pixels);
    public static SwShBakedMaterial Bake(PreviewPrimitive primitive, byte[] sourceMaterials, int materialIndex,
        Func<string, byte[]> readTexture, CancellationToken cancellationToken = default, bool sourceNormalDimensions = false)
    {
        if (materialIndex is < 0 or > 9) throw new InvalidDataException("Material texture identity is outside the supported range.");
        _ = SwShModelWriter.Bounds(primitive, 1);
        var source = new ModelBuffer(sourceMaterials);
        var tables = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 0) == primitive.Material.Name).ToArray();
        if (tables.Length != 1) throw new InvalidDataException("Source material identity is ambiguous.");
        var table = tables[0];
        var textures = source.Tables(table, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!, StringComparer.Ordinal);
        var eye = primitive.Material.Name.Contains("eye", StringComparison.Ordinal);
        var templatePath = textures[eye ? "LayerMaskMap" : "BaseColorMap"];
        var template = readTexture(templatePath); var container = new ModelTextureDocument(template);
        var colorWidth = container.Width; var colorHeight = container.Height;
        foreach (var role in new[] { "BaseColorMap", "LayerMaskMap" })
            if (textures.TryGetValue(role, out var path))
            {
                var image = PreviewTexture.ReadBaseLevel(readTexture(path));
                colorWidth = Math.Max(colorWidth, image.Width); colorHeight = Math.Max(colorHeight, image.Height);
            }
        template = SwShTextureAllocation.Create(template, colorWidth, colorHeight, container.Srgb);
        container = new ModelTextureDocument(template);
        var normalBytes = readTexture(textures["NormalMap"]);
        var normalSource = Read(normalBytes);
        var width = normalSource.Width;
        var height = normalSource.Height;
        var tangentNormals = Resize(normalSource.Pixels, normalSource.Width, normalSource.Height, width, height);
        var normals = new Vector3[width * height]; var covered = new bool[normals.Length]; var conflicts = 0;
        var vertices = primitive.Vertices;
        Vector3 Position(int i) => new(vertices[i * 16], vertices[i * 16 + 1], vertices[i * 16 + 2]);
        Vector3 Normal(int i) => new(vertices[i * 16 + 3], vertices[i * 16 + 4], vertices[i * 16 + 5]);
        Vector2 Uv(int i) => new(vertices[i * 16 + 6], vertices[i * 16 + 7]);
        for (var triangle = 0; triangle < primitive.Indices.Length; triangle += 3)
        {
            if ((triangle & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = checked((int)primitive.Indices[triangle]); var b = checked((int)primitive.Indices[triangle + 1]); var c = checked((int)primitive.Indices[triangle + 2]);
            var uvA = Uv(a); var uvB = Uv(b); var uvC = Uv(c);
            var wrap = MathF.Floor(MathF.Min(uvA.Y, MathF.Min(uvB.Y, uvC.Y))); uvA.Y -= wrap; uvB.Y -= wrap; uvC.Y -= wrap;
            var u = uvB - uvA; var v = uvC - uvA; var determinant = Cross(u, v);
            if (MathF.Abs(determinant) < 1e-12f) continue;
            var e1 = Position(b) - Position(a); var e2 = Position(c) - Position(a);
            var tangent = (e1 * v.Y - e2 * u.Y) / determinant; var bitangent = (e2 * u.X - e1 * v.X) / determinant;
            Vector2 ImagePoint(Vector2 uv) => new(uv.X * width, (1 - uv.Y) * height);
            var p = ImagePoint(uvA); var q = ImagePoint(uvB); var r = ImagePoint(uvC); var ab = q - p; var ac = r - p;
            var area = Cross(ab, ac);
            var left = Math.Max(0, (int)MathF.Floor(MathF.Min(p.X, MathF.Min(q.X, r.X))));
            var right = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p.X, MathF.Max(q.X, r.X))));
            var top = Math.Max(0, (int)MathF.Floor(MathF.Min(p.Y, MathF.Min(q.Y, r.Y))));
            var bottom = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p.Y, MathF.Max(q.Y, r.Y))));
            for (var y = top; y <= bottom; y++) for (var x = left; x <= right; x++)
            {
                var point = new Vector2(x + .5f, y + .5f) - p; var beta = Cross(point, ac) / area; var gamma = Cross(ab, point) / area; var alpha = 1 - beta - gamma;
                if (MathF.Min(alpha, MathF.Min(beta, gamma)) < -.00001f) continue;
                var normal = Unit(Normal(a) * alpha + Normal(b) * beta + Normal(c) * gamma);
                var t = Unit(tangent - normal * Vector3.Dot(normal, tangent)); var bt = Unit(Vector3.Cross(normal, t));
                if (Vector3.Dot(bt, bitangent) < 0) bt = -bt;
                var index = y * width + x; var nx = tangentNormals[index * 4] / 127.5f - 1; var ny = tangentNormals[index * 4 + 1] / 127.5f - 1;
                var value = Unit(t * nx + bt * ny + normal * MathF.Sqrt(MathF.Max(0, 1 - nx * nx - ny * ny)));
                if (covered[index] && Vector3.Dot(normals[index], value) < .8f) conflicts++;
                normals[index] = value; covered[index] = true;
            }
        }
        var coverage = covered.Count(v => v);
        if (coverage == 0) throw new InvalidDataException("Material has no rasterizable UV coverage.");
        var nearest = Nearest(covered, width, height, cancellationToken);
        var normalPixels = new byte[width * height * 4];
        var ambientTemplateName = Path.GetFileNameWithoutExtension(templatePath);
        var ambientWidth = 4; var ambientHeight = 4;
        byte[]? occlusion = null;
        if (textures.TryGetValue("AOMap", out var aoPath))
        {
            var ao = Read(readTexture(aoPath));
            ambientWidth = ao.Width; ambientHeight = ao.Height;
            occlusion = Resize(ao.Pixels, ao.Width, ao.Height, ambientWidth, ambientHeight);
        }
        var ambientPixels = new byte[ambientWidth * ambientHeight * 4];
        for (var i = 0; i < nearest.Length; i++)
        {
            var n = normals[nearest[i]];
            normalPixels[i * 4] = Channel((n.X + 1) / 2); normalPixels[i * 4 + 1] = Channel((n.Y + 1) / 2); normalPixels[i * 4 + 2] = Channel((n.Z + 1) / 2); normalPixels[i * 4 + 3] = 255;
        }
        for (var i = 0; i < ambientWidth * ambientHeight; i++)
        {
            ambientPixels[i * 4] = occlusion is null ? (byte)0 : (byte)(255 - occlusion[i * 4]);
            ambientPixels[i * 4 + 2] = eye ? (byte)166 : (byte)128; ambientPixels[i * 4 + 3] = 255;
        }
        var colorName = Path.GetFileNameWithoutExtension(templatePath);
        if (!colorName.EndsWith(eye ? "_lym" : "_alb", StringComparison.Ordinal)) throw new InvalidDataException("Material texture naming needs a definition adapter.");
        var normalName = "n" + materialIndex + colorName[2..^3] + "nor";
        var ambientName = "a" + materialIndex + ambientTemplateName[2..^3] + "amb";
        var normalTemplate = SwShTextureAllocation.Create(template, width, height, false);
        var ambientTemplate = SwShTextureAllocation.Create(template, ambientWidth, ambientHeight, false);
        var normalContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(normalTemplate, colorName, normalName, linear: true));
        var ambientContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(ambientTemplate, ambientTemplateName, ambientName, linear: true));
        var colorPixels = Read(readTexture(templatePath)).Pixels;
        if (eye || textures.ContainsKey("LayerMaskMap"))
        {
            var baseImage = Read(readTexture(textures["BaseColorMap"])); var basePixels = Resize(baseImage.Pixels, baseImage.Width, baseImage.Height, colorWidth, colorHeight);
            var layerImage = Read(readTexture(textures["LayerMaskMap"]));
            var mask = Resize(layerImage.Pixels, layerImage.Width, layerImage.Height, colorWidth, colorHeight);
            var colors = source.Tables(table, 7, 128).ToDictionary(t => source.Text(t, 0)!, t =>
            { var at = source.Field(t, 1); return new Vector3(source.Float(at), source.Float(at + 4), source.Float(at + 8)); }, StringComparer.Ordinal);
            var scalars = source.Tables(table, 4, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0, StringComparer.Ordinal);
            var srgb = (PreviewTexture.ReadBaseLevel(template).Format & 255) == 6;
            var pixels = new byte[mask.Length];
            float Linear(byte value) { var f = value / 255f; return !baseImage.Srgb ? f : f <= .04045f ? f / 12.92f : MathF.Pow((f + .055f) / 1.055f, 2.4f); }
            byte Encoded(float value) => Channel(!srgb ? value : value <= .0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f);
            for (var i = 0; i < colorWidth * colorHeight; i++)
            {
                var original = new Vector3(Linear(basePixels[i * 4]), Linear(basePixels[i * 4 + 1]), Linear(basePixels[i * 4 + 2])); var rgb = original;
                for (var layer = 0; layer < 4; layer++)
                {
                    if (!colors.TryGetValue("BaseColorLayer" + (layer + 1), out var tint)) throw new InvalidDataException("Material color layer is missing.");
                    rgb = Vector3.Lerp(rgb, original * tint, Math.Clamp(mask[i * 4 + layer] / 255f * scalars.GetValueOrDefault("LayerMaskScale" + (layer + 1), 1), 0, 1));
                }
                pixels[i * 4] = Encoded(rgb.X); pixels[i * 4 + 1] = Encoded(rgb.Y); pixels[i * 4 + 2] = Encoded(rgb.Z); pixels[i * 4 + 3] = basePixels[i * 4 + 3];
            }
            colorPixels = pixels;
        }
        var color = container.EncodeMipImages(SwShTextureFiltering.Mips(colorPixels, colorWidth, colorHeight, container.MipCount, container.Srgb, sharpen: true), cancellationToken);
        return new(colorName, color, normalName, normalContainer.EncodeMipImages(SwShTextureFiltering.Mips(normalPixels, width, height, normalContainer.MipCount, normals: true), cancellationToken),
            ambientName, ambientContainer.EncodeMipImages(SwShTextureFiltering.Mips(ambientPixels, ambientWidth, ambientHeight, ambientContainer.MipCount), cancellationToken), coverage / (double)covered.Length, conflicts);
    }

    public static byte[] Icon(byte[] sourceTexture, byte[] targetTemplate, CancellationToken cancellationToken = default)
    {
        var source = Read(sourceTexture); var target = new ModelTextureDocument(targetTemplate);
        if (source.Width > target.Width || source.Height > target.Height) throw new InvalidDataException("Source icon does not fit the target icon frame.");
        var pixels = new byte[target.Width * target.Height * 4]; var x = (target.Width - source.Width) / 2; var y = (target.Height - source.Height) / 2;
        for (var row = 0; row < source.Height; row++) source.Pixels.AsSpan(row * source.Width * 4, source.Width * 4).CopyTo(pixels.AsSpan(((row + y) * target.Width + x) * 4));
        return target.EncodeMipImages(Mips(pixels, target.Width, target.Height, target.MipCount), cancellationToken);
    }
    private static Image Read(byte[] bytes)
    {
        var texture = PreviewTexture.ReadBaseLevel(bytes);
        var format = texture.Format switch
        {
            0x0b01 or 0x0b06 => CompressionFormat.Rgba, 0x1a01 or 0x1a06 => CompressionFormat.Bc1WithAlpha,
            0x1b01 or 0x1b06 => CompressionFormat.Bc2, 0x1c01 or 0x1c06 => CompressionFormat.Bc3,
            0x1d01 => CompressionFormat.Bc4, 0x1e01 => CompressionFormat.Bc5, 0x2001 or 0x2006 => CompressionFormat.Bc7,
            _ => throw new InvalidDataException("Source material texture is unsupported."),
        };
        if ((long)texture.Width * texture.Height > 4 * 1024 * 1024) throw new InvalidDataException("Source material image exceeds its budget.");
        var pixels = format == CompressionFormat.Rgba ? texture.Blocks : new BcDecoder().DecodeRaw(texture.Blocks, texture.Width, texture.Height, format)
            .SelectMany(p => new[] { p.r, p.g, p.b, p.a }).ToArray();
        return new(texture.Width, texture.Height, (texture.Format & 255) == 6, pixels);
    }
    private static byte[][] Mips(byte[] pixels, int width, int height, int count) => Enumerable.Range(0, count)
        .Select(m => Resize(pixels, width, height, Math.Max(1, width >> m), Math.Max(1, height >> m))).ToArray();
    private static byte[] Resize(byte[] source, int width, int height, int w, int h)
    {
        if (w == width && h == height) return source.ToArray();
        var output = new byte[w * h * 4];
        // A tent filter expands with the reduction ratio so small mip levels
        // include their source footprint instead of aliasing four texels.
        var sx = width / (double)w; var sy = height / (double)h; var rx = Math.Max(1, sx); var ry = Math.Max(1, sy);
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var cx = (x + .5) * sx - .5; var cy = (y + .5) * sy - .5;
            var sums = new double[4]; double weight = 0;
            for (var iy = Math.Max(0, (int)Math.Ceiling(cy - ry)); iy <= Math.Min(height - 1, (int)Math.Floor(cy + ry)); iy++)
            for (var ix = Math.Max(0, (int)Math.Ceiling(cx - rx)); ix <= Math.Min(width - 1, (int)Math.Floor(cx + rx)); ix++)
            {
                var k = Math.Max(0, 1 - Math.Abs(ix - cx) / rx) * Math.Max(0, 1 - Math.Abs(iy - cy) / ry); weight += k;
                for (var c = 0; c < 4; c++) sums[c] += source[(iy * width + ix) * 4 + c] * k;
            }
            for (var c = 0; c < 4; c++) output[(y * w + x) * 4 + c] = (byte)Math.Clamp((int)Math.Round(sums[c] / weight), 0, 255);
        }
        return output;
    }
    private static int[] Nearest(bool[] covered, int width, int height, CancellationToken token)
    {
        var nearestX = new int[covered.Length]; Array.Fill(nearestX, -1);
        for (var y = 0; y < height; y++)
        {
            var found = -1;
            for (var x = 0; x < width; x++) { if (covered[y * width + x]) found = x; nearestX[y * width + x] = found; }
            found = -1;
            for (var x = width - 1; x >= 0; x--)
            {
                if (covered[y * width + x]) found = x;
                var old = nearestX[y * width + x]; if (found >= 0 && (old < 0 || found - x < x - old)) nearestX[y * width + x] = found;
            }
        }
        var result = new int[covered.Length]; var envelope = new int[height]; var starts = new double[height];
        for (var x = 0; x < width; x++)
        {
            token.ThrowIfCancellationRequested(); var count = 0;
            double Cost(int y) { var dx = nearestX[y * width + x] - x; return (double)y * y + (double)dx * dx; }
            for (var y = 0; y < height; y++)
            {
                if (nearestX[y * width + x] < 0) continue;
                double start = double.NegativeInfinity;
                while (count > 0)
                {
                    var old = envelope[count - 1]; start = (Cost(y) - Cost(old)) / (2 * (y - old));
                    if (start > starts[count - 1]) break; count--;
                }
                envelope[count] = y; starts[count] = count == 0 ? double.NegativeInfinity : start; count++;
            }
            var active = 0;
            for (var y = 0; y < height; y++)
            {
                while (active + 1 < count && starts[active + 1] < y) active++;
                var row = envelope[active]; result[y * width + x] = row * width + nearestX[row * width + x];
            }
        }
        return result;
    }
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static Vector3 Unit(Vector3 v) => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : throw new InvalidDataException("Material tangent frame is degenerate.");
    private static byte Channel(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
