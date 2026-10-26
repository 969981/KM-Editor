// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.SwSh;

/// <summary>Validates the packed Vorbis WEM contract already used by native SwSh audio.</summary>
public static class SwShVorbisVoice
{
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length is < 96 or > 32 * 1024 * 1024 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8) || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != bytes.Length - 8)
            throw new InvalidDataException("Vorbis cry is not a complete RIFF container.");
        var chunks = new Dictionary<string, (int At, int Size)>(StringComparer.Ordinal);
        for (var at = 12; at < bytes.Length;)
        {
            if (at > bytes.Length - 8) throw new InvalidDataException("Vorbis cry chunk is truncated.");
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)));
            if (length > bytes.Length - at - 8 || !chunks.TryAdd(Encoding.ASCII.GetString(bytes, at, 4), (at + 8, length)))
                throw new InvalidDataException("Vorbis cry chunks are invalid or duplicated.");
            at = checked(at + 8 + length);
            if (at < bytes.Length) at += length & 1;
        }
        if (chunks.Count != 2 || !chunks.TryGetValue("fmt ", out var fmt) || fmt.Size != 66 || !chunks.TryGetValue("data", out var data))
            throw new InvalidDataException("Vorbis cry requires the native packed header and sample stream.");
        var format = bytes.AsSpan(fmt.At, fmt.Size);
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(fmt.At + at));
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]); var rate = Word(4); var samples = Word(24);
        if (BinaryPrimitives.ReadUInt16LittleEndian(format) != 0xFFFF || channels is < 1 or > 2 || rate is < 8000 or > 192000
            || BinaryPrimitives.ReadUInt16LittleEndian(format[16..]) != 48 || samples is < 1 or > 8 * 1024 * 1024
            || format[64] != 8 || format[65] != 11)
            throw new InvalidDataException("Vorbis cry format requires another native adapter.");
        var setup = checked((int)Word(40)); var audio = checked((int)Word(44));
        if (setup < 0 || setup > data.Size - 2 || audio > data.Size - 2
            || setup + 2 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(data.At + setup)) != audio)
            throw new InvalidDataException("Vorbis cry setup packet is invalid.");
        var cursor = audio; var count = 0;
        while (cursor < data.Size)
        {
            if (cursor > data.Size - 2 || ++count > 65536) throw new InvalidDataException("Vorbis cry packet stream is truncated or oversized.");
            var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(data.At + cursor)); cursor += 2;
            if (length == 0 || length > data.Size - cursor) throw new InvalidDataException("Vorbis cry packet is invalid.");
            cursor += length;
        }
        if (count == 0) throw new InvalidDataException("Vorbis cry has no audio packets.");
    }
}
