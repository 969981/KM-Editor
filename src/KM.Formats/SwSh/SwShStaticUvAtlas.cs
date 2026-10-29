// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

/// <summary>Retains an authored UV tile at its original texel density.</summary>
public sealed record SwShStaticUvAtlas(int DivisionsX, int DivisionsY, float TileX, float TileY, int RepeatX = 0, int RepeatY = 0)
{
    public static SwShStaticUvAtlas CenteredRepeat(PreviewPrimitive primitive, Vector2 scale)
    {
        if (scale.X is not (1 or 2 or 4 or 8) || scale.Y is not (1 or 2 or 4 or 8))
            throw new InvalidDataException("Static UV allocation requires a supported tile division.");
        var coordinates = primitive.Indices.Distinct().Select(i => new Vector2(primitive.Vertices[i * 16 + 6], 1 - primitive.Vertices[i * 16 + 7])).ToArray();
        var repeatX = (int)MathF.Floor(coordinates.Min(v => v.X));
        var repeatY = (int)MathF.Floor(coordinates.Min(v => v.Y));
        // Half-cell origins retain islands crossing the center of a texture.
        // Each crop still has a power-of-two size and keeps its original texels.
        var x = MathF.Floor((coordinates.Min(v => v.X) - repeatX) * scale.X * 2) / 2;
        var y = MathF.Floor((coordinates.Min(v => v.Y) - repeatY) * scale.Y * 2) / 2;
        if (x < 0 || y < 0 || x + 1 > scale.X || y + 1 > scale.Y || coordinates.Any(v =>
            (v.X - repeatX) * scale.X <= x || (v.X - repeatX) * scale.X >= x + 1
            || (v.Y - repeatY) * scale.Y <= y || (v.Y - repeatY) * scale.Y >= y + 1))
            throw new InvalidDataException("Centered static UV allocation would remove authored mesh coverage.");
        return new((int)scale.X, (int)scale.Y, x, y, repeatX, repeatY);
    }

    public static SwShStaticUvAtlas For(PreviewPrimitive primitive, Vector2 scale)
    {
        if (scale.X is not (1 or 2 or 4 or 8) || scale.Y is not (1 or 2 or 4 or 8))
            throw new InvalidDataException("Static UV allocation requires a supported tile division.");
        var coordinates = primitive.Indices.Distinct().Select(i => new Vector2(primitive.Vertices[i * 16 + 6], primitive.Vertices[i * 16 + 7])).ToArray();
        var x = (int)MathF.Floor(coordinates.Min(v => v.X) * scale.X);
        var y = (int)MathF.Floor(coordinates.Min(v => 1 - v.Y) * scale.Y);
        if (x < 0 || y < 0 || x >= scale.X || y >= scale.Y || coordinates.Any(v =>
            v.X * scale.X <= x || v.X * scale.X >= x + 1 || (1 - v.Y) * scale.Y <= y || (1 - v.Y) * scale.Y >= y + 1))
            throw new InvalidDataException("Static UV allocation would remove authored mesh coverage.");
        return new((int)scale.X, (int)scale.Y, x, y);
    }

    public PreviewPrimitive Geometry(PreviewPrimitive primitive)
    {
        var vertices = primitive.Vertices.ToArray();
        for (var at = 0; at < vertices.Length; at += 16)
        {
            vertices[at + 6] = (vertices[at + 6] - RepeatX) * DivisionsX - TileX;
            vertices[at + 7] = vertices[at + 7] * DivisionsY - (DivisionsY - 1 - TileY - RepeatY * DivisionsY);
        }
        return primitive with { Vertices = vertices };
    }

    public (byte[][] Images, int Width, int Height) Images(IReadOnlyList<byte[]> source, int width, int height)
    {
        if (width / DivisionsX < 4 || height / DivisionsY < 4)
        {
            var pixels = source[0];
            if (Enumerable.Range(0, pixels.Length / 4).Any(i => !pixels.AsSpan(i * 4, 4).SequenceEqual(pixels.AsSpan(0, 4))))
                throw new InvalidDataException("A small nonuniform image requires another static UV adapter.");
            return (source.Select(p => p.ToArray()).ToArray(), width, height);
        }
        var w = width / DivisionsX; var h = height / DivisionsY;
        var count = 1 + (int)Math.Log2(Math.Max(w, h)); var result = new byte[count][];
        if (source.Count < count) throw new InvalidDataException("Static UV source mip chain is incomplete.");
        for (var mip = 0; mip < count; mip++)
        {
            var sw = Math.Max(1, width >> mip); var sh = Math.Max(1, height >> mip);
            var dw = Math.Max(1, w >> mip); var dh = Math.Max(1, h >> mip);
            var x = Math.Min(sw - dw, (int)(TileX * sw / DivisionsX)); var y = Math.Min(sh - dh, (int)(TileY * sh / DivisionsY));
            if (source[mip].Length != sw * sh * 4) throw new InvalidDataException("Static UV source mip dimensions disagree.");
            result[mip] = new byte[dw * dh * 4];
            for (var row = 0; row < dh; row++) source[mip].AsSpan(((y + row) * sw + x) * 4, dw * 4).CopyTo(result[mip].AsSpan(row * dw * 4));
        }
        return (result, w, h);
    }

    public byte[] Reference(byte[] sourceBytes, byte[] targetBytes)
    {
        var source = new ModelTextureDocument(sourceBytes); var target = new ModelTextureDocument(targetBytes);
        if (source.Format != target.Format || source.Format is not (0x2001 or 0x2006))
            throw new InvalidDataException("Static alpha reference requires matching BC7 containers.");
        if (target.Width != source.Width / DivisionsX || target.Height != source.Height / DivisionsY || target.MipCount > source.MipCount)
            throw new InvalidDataException("Static alpha reference dimensions are invalid.");
        var blocks = new byte[target.MipCount][];
        for (var mip = 0; mip < blocks.Length; mip++)
        {
            var old = PreviewTexture.ReadLevel(sourceBytes, mip); var current = PreviewTexture.ReadLevel(targetBytes, mip);
            blocks[mip] = current.Blocks.ToArray();
            var x = Math.Min(old.Width - current.Width, (int)(TileX * old.Width / DivisionsX));
            var y = Math.Min(old.Height - current.Height, (int)(TileY * old.Height / DivisionsY));
            var originalStride = (old.Width + 3) / 4 * 16; var stride = (current.Width + 3) / 4 * 16;
            if (x % 4 != 0 || y % 4 != 0)
            {
                if (current.Width > 4 || current.Height > 4)
                    throw new InvalidDataException("An unaligned atlas crop exceeds one BC7 block.");
                blocks[mip] = ModelTextureBc7Crop.Crop(old.Blocks.AsSpan(y / 4 * originalStride + x / 4 * 16, 16), x % 4, y % 4, current.Width, current.Height);
                continue;
            }
            for (var row = 0; row < (current.Height + 3) / 4; row++)
                old.Blocks.AsSpan((y / 4 + row) * originalStride + x / 4 * 16, stride).CopyTo(blocks[mip].AsSpan(row * stride));
        }
        return target.WithMipBlocks(blocks);
    }
}
