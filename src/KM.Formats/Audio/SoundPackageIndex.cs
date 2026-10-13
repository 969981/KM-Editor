// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.Audio;

public sealed record SoundPackageMedia(ulong Id, int Group, uint Language, long Offset, int Length);

public static class SoundPackageIndex
{
    public static IReadOnlyList<SoundPackageMedia> Read(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead || !stream.CanSeek || stream.Length < 28) throw new InvalidDataException("Audio package is not readable.");
        stream.Position = 0; var header = new byte[28]; stream.ReadExactly(header);
        uint Value(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        if (!header.AsSpan(0, 4).SequenceEqual("AKPK"u8) || Value(header, 8) != 1)
            throw new InvalidDataException("Audio package byte order or signature is unsupported.");
        var size = checked((int)Value(header, 4) + 8);
        if (size < 28 || size > 16 * 1024 * 1024 || size > stream.Length) throw new InvalidDataException("Audio package index size is invalid.");
        var index = new byte[size]; header.CopyTo(index, 0); stream.ReadExactly(index.AsSpan(28));
        var position = checked(28 + (int)Value(index, 12)); var result = new List<SoundPackageMedia>();
        for (var group = 0; group < 3; group++)
        {
            var length = checked((int)Value(index, 16 + group * 4));
            if (position < 0 || position > size - length) throw new InvalidDataException("Audio package index is truncated.");
            if (length == 0) continue;
            if (length < 4) throw new InvalidDataException("Audio package table is truncated.");
            var count = Value(index, position); var stride = group == 2 ? 24 : 20;
            if (count > 200000 || (long)count * stride + 4 != length) throw new InvalidDataException("Audio package table count is invalid.");
            for (var row = position + 4; row < position + length; row += stride)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = group == 2 ? BinaryPrimitives.ReadUInt64LittleEndian(index.AsSpan(row)) : Value(index, row);
                var at = row + (group == 2 ? 4 : 0); var alignment = Value(index, at + 4); var payloadLength = checked((int)Value(index, at + 8));
                var offset = checked((long)Value(index, at + 12) * alignment); var language = Value(index, at + 16);
                if (alignment == 0 || offset < size || offset > stream.Length - payloadLength) throw new InvalidDataException("Audio package payload range is invalid.");
                result.Add(new(id, group, language, offset, payloadLength));
            }
            position += length;
        }
        if (position != size) throw new InvalidDataException("Audio package index has unsupported trailing data.");
        return result;
    }
}
