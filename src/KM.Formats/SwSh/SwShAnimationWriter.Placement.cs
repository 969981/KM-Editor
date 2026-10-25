// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    public static byte[] VerticalOffset(byte[] animation, string bone, float offset)
    {
        if (string.IsNullOrWhiteSpace(bone) || !float.IsFinite(offset))
            throw new InvalidDataException("Animation placement is invalid.");
        var bytes = animation.ToArray(); var data = new ModelBuffer(bytes);
        var tracks = data.Tables(data.Table(data.Root, 1), 0, 4096);
        var matches = tracks.Where(t => data.Text(t, 0) == bone).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Animation placement anchor is missing or ambiguous.");
        var kindAt = data.Field(matches[0], 5); var kind = kindAt == 0 ? 0 : data.U8(kindAt);
        if (kind is < 1 or > 4) throw new InvalidDataException("Animation placement curve is unsupported.");
        var curve = data.Table(matches[0], 6);
        var (start, count) = kind == 1 ? (data.Field(curve, 0), 1) : data.Vector(curve, kind == 2 ? 0 : 1, 12, 18000);
        if (start == 0 || count == 0) throw new InvalidDataException("Animation placement curve is empty.");
        for (var i = 0; i < count; i++)
        {
            var at = start + i * 12 + 4; var value = data.Float(at) + offset;
            if (!float.IsFinite(value)) throw new InvalidDataException("Animation placement curve is nonfinite.");
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at, 4), value);
        }
        return bytes;
    }
}
