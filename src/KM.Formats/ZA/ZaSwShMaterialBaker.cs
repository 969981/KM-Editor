// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

/// <summary>Bakes Legends Z-A layered colors, eye highlights, and independent normal UVs into native SwSh textures.</summary>
public static class ZaSwShMaterialBaker
{
    private sealed record Image(int Width, int Height, bool Srgb, byte[] Pixels);
    public static byte[] Icon(byte[] sourceTexture, byte[] targetTemplate, CancellationToken cancellationToken = default)
    {
        var source = Read(sourceTexture); var target = new ModelTextureDocument(targetTemplate);
        var scale = Math.Min(1d, Math.Min(target.Width / (double)source.Width, target.Height / (double)source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale)); var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var pixels = new byte[target.Width * target.Height * 4]; var left = (target.Width - width) / 2; var top = (target.Height - height) / 2;
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var minX = x * source.Width / (double)width; var maxX = (x + 1) * source.Width / (double)width;
                var minY = y * source.Height / (double)height; var maxY = (y + 1) * source.Height / (double)height;
                var color = Vector3.Zero; double alpha = 0, total = 0;
                for (var sy = (int)minY; sy < Math.Ceiling(maxY); sy++) for (var sx = (int)minX; sx < Math.Ceiling(maxX); sx++)
                {
                    var weight = (Math.Min(maxX, sx + 1) - Math.Max(minX, sx)) * (Math.Min(maxY, sy + 1) - Math.Max(minY, sy));
                    var at = (sy * source.Width + sx) * 4; var opacity = source.Pixels[at + 3] / 255d;
                    color += new Vector3(source.Pixels[at], source.Pixels[at + 1], source.Pixels[at + 2]) * (float)(opacity * weight);
                    alpha += opacity * weight; total += weight;
                }
                var output = ((y + top) * target.Width + x + left) * 4;
                if (alpha > 0)
                {
                    color /= (float)alpha;
                    pixels[output] = Channel(color.X / 255); pixels[output + 1] = Channel(color.Y / 255); pixels[output + 2] = Channel(color.Z / 255);
                }
                pixels[output + 3] = Channel((float)(alpha / total));
            }
        }
        return target.EncodeMipImages(Mips(pixels, target.Width, target.Height, target.MipCount), cancellationToken);
    }
    public static SwShBakedMaterial Bake(PreviewPrimitive primitive, byte[] sourceMaterials, int materialIndex, bool shiny, byte[] textureTemplate, string templateName,
        Func<string, byte[]> readTexture, CancellationToken cancellationToken = default)
    {
        if (materialIndex is < 0 or > 9) throw new InvalidDataException("Material texture identity is outside the supported range.");
        _ = SwShModelWriter.Bounds(primitive, 1);
        var source = new ModelBuffer(sourceMaterials);
        var tables = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 0) == primitive.Material.Name).ToArray();
        if (tables.Length != 1) throw new InvalidDataException("Source material identity is ambiguous.");
        var table = tables[0];
        var textures = source.Tables(table, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!, StringComparer.Ordinal);
        var eye = primitive.Material.Name.Contains("eye", StringComparison.Ordinal);
        var template = textureTemplate; var container = new ModelTextureDocument(template);
        var parameters = source.Tables(table, 7, 128).ToDictionary(t => source.Text(t, 0)!, t =>
        { var at = source.Field(t, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); }, StringComparer.Ordinal);
        var scalarParameters = source.Tables(table, 4, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0, StringComparer.Ordinal);
        var uvTransform = parameters.GetValueOrDefault("UVScaleOffset", new(1, 1, 0, 0));
        // -1 inherits the transformed base coordinates; 0 explicitly selects
        // the original UV0. Croconaw's asymmetric belly mask uses the latter
        // even though its base texture doubles and mirrors U.
        var maskUvIndex = source.Tables(table, 9, 128).SingleOrDefault(t => source.Text(t, 0) == "UVIndexLayerMask");
        var maskTransform = (maskUvIndex == 0 ? uint.MaxValue : source.Value(maskUvIndex, 1)) switch
        {
            uint.MaxValue => uvTransform,
            0 => new Vector4(1, 1, 0, 0),
            _ => throw new InvalidDataException("Z-A layer mask UV set needs another material adapter."),
        };
        var normalUv = parameters.GetValueOrDefault("UVScaleOffsetNormal", new(1, 1, 0, 0));
        if (normalUv != new Vector4(1, 1, 0, 0)) throw new InvalidDataException("Z-A normal UV transform needs another material adapter.");
        var normalSource = Read(readTexture(textures["NormalMap"]));
        // Object normal images are unique to each primitive. Bound those
        // repeated allocations independently of the layered color detail.
        var width = Math.Min(512, normalSource.Width); var height = Math.Min(512, normalSource.Height);
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
        var ambientWidth = 4; var ambientHeight = 4;
        byte[]? occlusion = null;
        if (textures.TryGetValue("OcclusionMap", out var aoPath))
        { var ao = Read(readTexture(aoPath)); ambientWidth = ao.Width; ambientHeight = ao.Height; occlusion = ao.Pixels; }
        for (var i = 0; i < nearest.Length; i++)
        {
            var n = normals[nearest[i]];
            normalPixels[i * 4] = Channel((n.X + 1) / 2); normalPixels[i * 4 + 1] = Channel((n.Y + 1) / 2); normalPixels[i * 4 + 2] = Channel((n.Z + 1) / 2); normalPixels[i * 4 + 3] = 255;
        }
        var ambientPixels = new byte[ambientWidth * ambientHeight * 4];
        for (var i = 0; i < ambientWidth * ambientHeight; i++)
        {
            ambientPixels[i * 4] = occlusion is null ? (byte)0 : (byte)(255 - occlusion[i * 4]);
            ambientPixels[i * 4 + 2] = eye ? (byte)166 : (byte)128; ambientPixels[i * 4 + 3] = 255;
        }
        var colorName = $"z{materialIndex}{(shiny ? 's' : 'n')}" + templateName[3..];
        var normalName = $"n{materialIndex}n" + templateName[3..];
        var ambientName = $"a{materialIndex}n" + templateName[3..];
        var baseImage = Read(readTexture(textures["BaseColorMap"]));
        var maskImage = Read(readTexture(textures["LayerMaskMap"]));
        var highlightImage = textures.TryGetValue("HighlightMaskMap", out var highlightPath) ? Read(readTexture(highlightPath)) : null;
        var colorWidth = Math.Max(baseImage.Width, Math.Max(maskImage.Width, highlightImage?.Width ?? 4));
        var colorHeight = Math.Max(baseImage.Height, Math.Max(maskImage.Height, highlightImage?.Height ?? 4));
        var colorContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, colorWidth, colorHeight, true), templateName, colorName));
        var normalContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, width, height, false), templateName, normalName));
        var ambientContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, ambientWidth, ambientHeight, false), templateName, ambientName));
        var highlightTransform = parameters.GetValueOrDefault("UVScaleOffset1", new(1, 1, 0, 0));
        var baseColor = parameters.GetValueOrDefault("BaseColor", Vector4.One);
        var highlightColor = parameters.GetValueOrDefault("EmissionColorLayer5", Vector4.Zero) * scalarParameters.GetValueOrDefault("EmissionIntensityLayer5");
        var pixels = new byte[colorWidth * colorHeight * 4];
        byte ColorChannel(float value) => Channel(value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f);
        for (var y = 0; y < colorHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < colorWidth; x++)
            {
                var uv = new Vector2((x + .5f) / colorWidth, (y + .5f) / colorHeight);
                var original = Sample(baseImage, uv, uvTransform, new(primitive.Material.Wrap.X, primitive.Material.Wrap.Y)) * baseColor;
                var mask = Sample(maskImage, uv, maskTransform, new(primitive.Material.Wrap.Z, primitive.Material.Wrap.W));
                var rgb = original;
                for (var layer = 0; layer < 4; layer++)
                {
                    var name = "BaseColorLayer" + (layer + 1);
                    if (!parameters.TryGetValue(name, out var tint)) throw new InvalidDataException("Z-A color layer is missing.");
                    var weight = Math.Clamp(mask[layer] * scalarParameters.GetValueOrDefault("LayerMaskScale" + (layer + 1), 1), 0, 1);
                    rgb = Vector4.Lerp(rgb, original * tint, weight);
                }
                if (highlightImage is not null)
                {
                    rgb += Sample(highlightImage, uv, highlightTransform, new(primitive.Material.HighlightWrap.X, primitive.Material.HighlightWrap.Y)).X * highlightColor;
                }
                var at = (y * colorWidth + x) * 4;
                pixels[at] = ColorChannel(rgb.X); pixels[at + 1] = ColorChannel(rgb.Y); pixels[at + 2] = ColorChannel(rgb.Z); pixels[at + 3] = 255;
            }
        }
        var color = colorContainer.EncodeMipImages(SwShTextureFiltering.Mips(pixels, colorWidth, colorHeight, colorContainer.MipCount, srgb: true, sharpen: true), cancellationToken);
        return new(colorName, color, normalName, normalContainer.EncodeMipImages(SwShTextureFiltering.Mips(normalPixels, width, height, normalContainer.MipCount, normals: true), cancellationToken),
            ambientName, ambientContainer.EncodeMipImages(SwShTextureFiltering.Mips(ambientPixels, ambientWidth, ambientHeight, ambientContainer.MipCount), cancellationToken), coverage / (double)covered.Length, conflicts);
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
    private static Vector4 Sample(Image image, Vector2 uv, Vector4 transform, Vector2 wrap)
    {
        static float Fract(float value) => value - MathF.Floor(value);
        static float Address(float value, float mode) => mode switch
        {
            0 => Fract(value), 1 => Math.Clamp(value, 0, 1),
            6 => 1 - MathF.Abs(Fract(value * .5f) * 2 - 1), 7 => Math.Clamp(MathF.Abs(value), 0, 1),
            _ => throw new InvalidDataException("Z-A texture addressing mode is unsupported."),
        };
        var x = Address(uv.X * transform.X + transform.Z, wrap.X) * image.Width - .5f;
        var y = (1 - Address((1 - uv.Y) * transform.Y + 1 - transform.Y - transform.W, wrap.Y)) * image.Height - .5f;
        var ix = (int)MathF.Floor(x); var iy = (int)MathF.Floor(y);
        Vector4 Pixel(int px, int py)
        {
            px = Math.Clamp(px, 0, image.Width - 1); py = Math.Clamp(py, 0, image.Height - 1);
            var at = (py * image.Width + px) * 4;
            float C(int c)
            {
                var v = image.Pixels[at + c] / 255f;
                return !image.Srgb || c == 3 ? v : v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
            }
            return new(C(0), C(1), C(2), C(3));
        }
        return Vector4.Lerp(Vector4.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), x - ix),
            Vector4.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), x - ix), y - iy);
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
