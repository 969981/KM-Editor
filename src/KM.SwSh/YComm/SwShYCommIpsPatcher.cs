// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;

namespace KM.SwSh.YComm;

internal static class SwShYCommIpsPatcher
{
    internal static string RelativePath(ProjectGame game) => "exefs/" + (game == ProjectGame.Sword
        ? "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471" : "A16802625E7826BF83B6F9708E475B912A9AB7DF") + ".ips";

    // Remove only complete recognized input records. Retain every independent record verbatim.
    internal static (bool Disabled, byte[]? Remaining) Read(byte[]? source, ProjectGame game)
    {
        if (source is null) return (false, null);
        if (source.Length is < 8 or > 4 * 1024 * 1024) throw new InvalidDataException("The executable patch file is unsupported.");
        var wide = source.AsSpan(0, 5).SequenceEqual("IPS32"u8);
        if (!wide && !source.AsSpan(0, 5).SequenceEqual("PATCH"u8)) throw new InvalidDataException("The executable patch header is invalid.");
        var addressLength = wide ? 4 : 3; var endMarker = wide ? "EEOF"u8.ToArray() : "EOF"u8.ToArray();
        var at = 5; var found = false; var records = 0;
        using var result = new MemoryStream(); result.Write(source.AsSpan(0, 5));
        while (at <= source.Length - endMarker.Length && !source.AsSpan(at, endMarker.Length).SequenceEqual(endMarker))
        {
            var start = at;
            if (++records > 65536 || at > source.Length - addressLength - 2) throw new InvalidDataException("The executable patch record is truncated.");
            var address = wide ? BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(at))
                : (uint)(source[at] << 16 | source[at + 1] << 8 | source[at + 2]);
            at += addressLength;
            var length = (int)BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(at)); at += 2;
            var data = at;
            if (length == 0)
            {
                if (at > source.Length - 3) throw new InvalidDataException("The executable patch run is truncated.");
                length = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(at)); at += 3;
                if (length == 0) throw new InvalidDataException("The executable patch run is empty.");
            }
            else
            {
                if (at > source.Length - length) throw new InvalidDataException("The executable patch data is truncated.");
                at += length;
            }
            var owned = SwShYCommMainPatcher.Offset(game) + 0x100L;
            if (address < owned + 4 && address + (long)length > owned)
            {
                if (address != owned || length != 4 || at - data != 4 || !source.AsSpan(data, 4).SequenceEqual(new byte[] { 0x1F, 0x20, 0x03, 0xD5 }))
                    throw new InvalidDataException("An executable patch conflicts with the Y Comm input gate.");
                found = true;
            }
            else result.Write(source.AsSpan(start, at - start));
        }
        if (at != source.Length - endMarker.Length || !source.AsSpan(at).SequenceEqual(endMarker))
            throw new InvalidDataException("The executable patch terminator is invalid.");
        if (!found) return (false, source.ToArray());
        if (result.Length == 5) return (true, null);
        result.Write(endMarker); return (true, result.ToArray());
    }
}
