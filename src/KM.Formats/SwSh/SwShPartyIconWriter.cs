// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.IO.Compression;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

/// <summary>Converts bounded static sprites to native party icon textures.</summary>
public static class SwShPartyIconWriter
{
    /// <summary>Copies a dedicated miniature onto the native two pixel grid without fitting or cropping.</summary>
    public static byte[] CreateMiniature(byte[] sprite, byte[] nativeTemplate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (width, height, source) = ReadSprite(sprite);
        var target = new ModelTextureDocument(nativeTemplate);
        if (width != 68 || height != 56 || target.Width != 136 || target.Height != 112
            || target.MipCount != 1 || target.Format is not (0x1a01 or 0x1a06))
            throw new InvalidDataException("Party miniature must match the native icon canvas and format.");
        _ = Bounds(source, width, height);
        var pixels = new byte[target.Width * target.Height * 4];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                if (source[at + 3] is not (0 or 255)) throw new InvalidDataException("Party miniature must use binary transparency.");
                if (source[at + 3] == 0) continue;
                for (var dy = 0; dy < 2; dy++) for (var dx = 0; dx < 2; dx++)
                    source.AsSpan(at, 4).CopyTo(pixels.AsSpan(((y * 2 + dy) * target.Width + x * 2 + dx) * 4, 4));
            }
        }
        return target.EncodeMipImages([pixels], cancellationToken);
    }

    public static byte[] Create(byte[] sprite, byte[] nativeTemplate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (width, height, source) = ReadSprite(sprite);
        var target = new ModelTextureDocument(nativeTemplate);
        if (target.Width != 136 || target.Height != 112 || target.MipCount != 1)
            throw new InvalidDataException("Native party icon layout is unsupported.");
        var frame = Bounds(target.Pixels(), target.Width, target.Height);
        var body = Bounds(source, width, height);
        // Native icons use a two pixel grid. Keep that grid and the template's
        // bottom edge, with transparent padding above the complete silhouette.
        var scale = Math.Min(frame.Width / (double)body.Width, frame.Height / (double)body.Height);
        var w = Math.Max(1, (int)Math.Floor(body.Width * scale / 2));
        var h = Math.Max(1, (int)Math.Floor(body.Height * scale / 2));
        var left = (target.Width / 2 - w) / 2 * 2;
        var top = (frame.Y + frame.Height - h * 2) / 2 * 2;
        var pixels = new byte[target.Width * target.Height * 4];
        for (var y = 0; y < h; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < w; x++)
            {
                var sx = body.X + Math.Min(body.Width - 1, (int)((x + .5) * body.Width / w));
                var sy = body.Y + Math.Min(body.Height - 1, (int)((y + .5) * body.Height / h));
                var at = (sy * width + sx) * 4;
                if (source[at + 3] < 128) continue;
                for (var dy = 0; dy < 2; dy++) for (var dx = 0; dx < 2; dx++)
                    source.AsSpan(at, 4).CopyTo(pixels.AsSpan(((top + y * 2 + dy) * target.Width + left + x * 2 + dx) * 4, 4));
            }
        }
        _ = Bounds(pixels, target.Width, target.Height);
        return target.EncodeMipImages([pixels], cancellationToken);
    }

    private static (int X, int Y, int Width, int Height) Bounds(byte[] pixels, int width, int height)
    {
        var left = width; var top = height; var right = -1; var bottom = -1;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            if (pixels[(y * width + x) * 4 + 3] >= 128)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (right < left) throw new InvalidDataException("Party icon has no visible sprite.");
        return (left, top, right - left + 1, bottom - top + 1);
    }

    private static (int Width, int Height, byte[] Pixels) ReadSprite(byte[] bytes)
    {
        if (bytes.Length is < 33 or > 1024 * 1024 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Party sprite is not a bounded PNG.");
        var width = 0; var height = 0; var channels = 1; byte[]? palette = null; byte[]? alpha = null; var ended = false;
        using var compressed = new MemoryStream();
        for (var at = 8; at < bytes.Length;)
        {
            if (bytes.Length - at < 12) throw new InvalidDataException("Party sprite chunk is truncated.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4));
            if (length > bytes.Length - at - 12) throw new InvalidDataException("Party sprite chunk is truncated.");
            var size = (int)length; var kind = bytes.AsSpan(at + 4, 4); var payload = bytes.AsSpan(at + 8, size);
            uint crc = uint.MaxValue;
            foreach (var value in bytes.AsSpan(at + 4, size + 4))
            { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1)); }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 8 + size, 4)))
                throw new InvalidDataException("Party sprite checksum is invalid.");
            if (kind.SequenceEqual("IHDR"u8))
            {
                if (at != 8 || size != 13 || payload[8] != 8 || payload[9] is not (3 or 6) || payload[10] != 0 || payload[11] != 0 || payload[12] != 0)
                    throw new InvalidDataException("Party sprite must use eight bit palette or RGBA pixels without interlacing.");
                channels = payload[9] == 6 ? 4 : 1;
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(payload)); height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(payload[4..]));
                if (width is < 1 or > 256 || height is < 1 or > 256) throw new InvalidDataException("Party sprite dimensions exceed their budget.");
            }
            else if (width == 0) throw new InvalidDataException("Party sprite header is missing.");
            else if (kind.SequenceEqual("PLTE"u8))
            {
                if (palette is not null || compressed.Length != 0 || size is < 3 or > 768 || size % 3 != 0) throw new InvalidDataException("Party sprite palette is invalid.");
                palette = payload.ToArray();
            }
            else if (kind.SequenceEqual("tRNS"u8))
            {
                if (channels != 1 || palette is null || alpha is not null || compressed.Length != 0 || size > palette.Length / 3) throw new InvalidDataException("Party sprite transparency is invalid.");
                alpha = payload.ToArray();
            }
            else if (kind.SequenceEqual("IDAT"u8))
            { if (channels == 1 && palette is null) throw new InvalidDataException("Party sprite palette is missing."); compressed.Write(payload); }
            else if (kind.SequenceEqual("IEND"u8))
            { if (size != 0 || at + 12 != bytes.Length) throw new InvalidDataException("Party sprite ending is invalid."); ended = true; }
            else if (kind.SequenceEqual("acTL"u8) || kind.SequenceEqual("fcTL"u8) || kind.SequenceEqual("fdAT"u8))
                throw new InvalidDataException("Party sprite must be a static image.");
            else if ((kind[0] & 32) == 0) throw new InvalidDataException("Party sprite has an unsupported critical chunk.");
            at += size + 12;
        }
        if (!ended || channels == 1 && palette is null || compressed.Length == 0) throw new InvalidDataException("Party sprite image is incomplete.");
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        var stride = width * channels;
        var scanlines = new byte[(stride + 1) * height]; inflater.ReadExactly(scanlines);
        if (inflater.ReadByte() != -1) throw new InvalidDataException("Party sprite image exceeds its dimensions.");
        var decoded = new byte[stride * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < stride; x++)
        {
            var at = y * stride + x; var a = x >= channels ? decoded[at - channels] : 0; var b = y > 0 ? decoded[at - stride] : 0; var c = x >= channels && y > 0 ? decoded[at - stride - channels] : 0;
            var predictor = scanlines[y * (stride + 1)] switch
            { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => throw new InvalidDataException("Party sprite filter is unsupported.") };
            decoded[at] = unchecked((byte)(scanlines[y * (stride + 1) + x + 1] + predictor));
        }
        if (channels == 4) return (width, height, decoded);
        var pixels = new byte[width * height * 4];
        for (var at = 0; at < decoded.Length; at++)
        {
            var index = decoded[at];
            if (index >= palette!.Length / 3) throw new InvalidDataException("Party sprite color index is invalid.");
            palette.AsSpan(index * 3, 3).CopyTo(pixels.AsSpan(at * 4, 3)); pixels[at * 4 + 3] = alpha is not null && index < alpha.Length ? alpha[index] : (byte)255;
        }
        return (width, height, pixels);
    }
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c; var da = Math.Abs(p - a); var db = Math.Abs(p - b); var dc = Math.Abs(p - c);
        return da <= db && da <= dc ? a : db <= dc ? b : c;
    }
}
