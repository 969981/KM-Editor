// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

/// <summary>Reorders existing BC7 endpoints and indices for a partial atlas block.</summary>
internal static class ModelTextureBc7Crop
{
    internal static byte[] Crop(ReadOnlySpan<byte> source, int x, int y, int width, int height)
    {
        if (source.Length != 16 || x < 0 || y < 0 || width < 1 || height < 1 || x + width > 4 || y + height > 4)
            throw new InvalidDataException("BC7 atlas crop crosses a source block.");
        var original = source.ToArray(); var mode = BitOperations.TrailingZeroCount((uint)original[0]);
        var map = Enumerable.Range(0, 16).Select(i => (y + Math.Min(i / 4, height - 1)) * 4 + x + Math.Min(i % 4, width - 1)).ToArray();
        var bytes = original.ToArray();
        if (mode == 7)
        {
            // A shifted two-subset block can require another partition. Probe the
            // decoder's format tables, then retain the exact endpoints and indices.
            var probe = original.ToArray();
            for (var endpoint = 0; endpoint < 4; endpoint++)
            {
                Set(probe, 14 + endpoint * 5, 5, endpoint % 2 * 31);
                Set(probe, 34 + endpoint * 5, 5, endpoint / 2 * 31);
                Set(probe, 94 + endpoint, 1, 0);
            }
            var assignment = Decode(probe);
            var subsets = assignment.Select(p => p.g < 128 ? 0 : 1).ToArray();
            var weights = new[] { 0, 82, 169, 251 };
            var indices = assignment.Select(p => Enumerable.Range(0, 4).MinBy(i => Math.Abs(p.r - weights[i]))).ToArray();
            for (var partition = 0; partition < 64; partition++)
            {
                var layout = Layout(partition);
                for (var swap = 0; swap < 2; swap++)
                {
                    bool Visible(int i) => i % 4 < width && i / 4 < height;
                    if (Enumerable.Range(0, 16).Any(i => Visible(i) && layout.Subsets[i] != (subsets[map[i]] ^ swap))) continue;
                    bytes = original.ToArray(); Set(bytes, 8, 6, partition);
                    var reordered = Enumerable.Range(0, 16).Select(i => Visible(i) ? indices[map[i]] : 0).ToArray();
                    for (var subset = 0; subset < 2; subset++)
                    {
                        var flip = reordered[layout.Anchors[subset]] >= 2;
                        for (var end = 0; end < 2; end++)
                        {
                            var oldEnd = (subset ^ swap) * 2 + (end ^ (flip ? 1 : 0)); var newEnd = subset * 2 + end;
                            for (var channel = 0; channel < 4; channel++)
                                Set(bytes, 14 + (channel * 4 + newEnd) * 5, 5, Get(original, 14 + (channel * 4 + oldEnd) * 5, 5));
                            Set(bytes, 94 + newEnd, 1, Get(original, 94 + oldEnd, 1));
                        }
                        if (flip) for (var i = 0; i < 16; i++) if (layout.Subsets[i] == subset) reordered[i] = 3 - reordered[i];
                    }
                    var cursor = 98;
                    for (var i = 0; i < 16; i++) { var count = layout.Anchors.Contains(i) ? 1 : 2; Set(bytes, cursor, count, reordered[i]); cursor += count; }
                    if (Matches(bytes)) return bytes;
                }
            }
            throw new InvalidDataException("The partial BC7 atlas block requires another lossless partition adapter.");
        }
        if (mode == 6)
        {
            Reorder(65, 4, Enumerable.Range(0, 4).Select(c => (7 + c * 14, 7)).ToArray(), 63);
        }
        else if (mode is 4 or 5)
        {
            var colorBits = mode == 4 ? 5 : 7;
            var colorIndexBits = mode == 4 && Get(original, 7, 1) == 1 ? 3 : 2;
            Reorder(mode == 5 ? 66 : colorIndexBits == 3 ? 81 : 50, colorIndexBits,
                Enumerable.Range(0, 3).Select(c => (8 + c * colorBits * 2, colorBits)).ToArray());
            var alphaIndexBits = mode == 5 ? 2 : 5 - colorIndexBits;
            Reorder(mode == 5 ? 97 : alphaIndexBits == 3 ? 81 : 50, alphaIndexBits, [(mode == 5 ? 50 : 38, mode == 5 ? 8 : 6)]);
        }
        else throw new InvalidDataException("The partial BC7 atlas block uses an unsupported alpha mode.");
        if (!Matches(bytes)) throw new InvalidDataException("BC7 atlas index reordering changed an authored pixel.");
        return bytes;

        void Reorder(int start, int bits, (int Start, int Bits)[] endpoints, int pbits = -1)
        {
            var indices = new int[16]; var cursor = start;
            for (var i = 0; i < 16; i++) { var count = i == 0 ? bits - 1 : bits; indices[i] = Get(original, cursor, count); cursor += count; }
            var reordered = map.Select(i => indices[i]).ToArray();
            if (reordered[0] >= 1 << (bits - 1))
            {
                foreach (var (at, count) in endpoints)
                {
                    Set(bytes, at, count, Get(original, at + count, count)); Set(bytes, at + count, count, Get(original, at, count));
                }
                if (pbits >= 0) { Set(bytes, pbits, 1, Get(original, pbits + 1, 1)); Set(bytes, pbits + 1, 1, Get(original, pbits, 1)); }
                for (var i = 0; i < 16; i++) reordered[i] = (1 << bits) - 1 - reordered[i];
            }
            cursor = start;
            for (var i = 0; i < 16; i++) { var count = i == 0 ? bits - 1 : bits; Set(bytes, cursor, count, reordered[i]); cursor += count; }
        }
        bool Matches(byte[] candidate)
        {
            var before = Decode(original); var after = Decode(candidate);
            return Enumerable.Range(0, 16).Where(i => i % 4 < width && i / 4 < height).All(i => before[map[i]].Equals(after[i]));
        }
    }

    private static (int[] Subsets, int[] Anchors) Layout(int partition)
    {
        var bytes = new byte[16]; Set(bytes, 7, 1, 1); Set(bytes, 8, 6, partition);
        for (var end = 0; end < 4; end++)
        {
            Set(bytes, 14 + end * 5, 5, end % 2 * 31); Set(bytes, 34 + end * 5, 5, end / 2 * 31);
        }
        for (var bit = 98; bit < 128; bit++) Set(bytes, bit, 1, 1);
        var decoded = Decode(bytes);
        return (decoded.Select(p => p.g < 128 ? 0 : 1).ToArray(), Enumerable.Range(0, 16).Where(i => decoded[i].r < 200).ToArray());
    }
    private static ColorRgba32[] Decode(byte[] bytes) => new BcDecoder().DecodeRaw(bytes, 4, 4, CompressionFormat.Bc7);
    private static int Get(byte[] bytes, int at, int count)
    {
        var value = 0; for (var i = 0; i < count; i++) value |= ((bytes[(at + i) / 8] >> ((at + i) % 8)) & 1) << i; return value;
    }
    private static void Set(byte[] bytes, int at, int count, int value)
    {
        for (var i = 0; i < count; i++) { var bit = at + i; bytes[bit / 8] = (byte)((bytes[bit / 8] & ~(1 << (bit % 8))) | (((value >> i) & 1) << (bit % 8))); }
    }
}
