// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.YComm;

internal static class SwShYCommHudPatcher
{
    internal const string RelativePath = "romfs/bin/appli/live_comm/bin/live_comm_top_00.arc";
    private const string LayoutName = "blyt/live_comm_top_00.bflyt";
    private const string OriginalHash = "14C019A7E91C0EDB2E2EF69797113A168176D56A9ED516B146ABBF3A98233B0B";
    private const string LegacyLayoutHash = "7F7CDFC252673ECB0A175CD9BDDA92FDAFB82180A098BD754AFA442AC7497251";

    internal static (bool Disabled, int Scale, int? LegacyPosition) Read(byte[] bytes)
    {
        Range(bytes, 0, 0x14);
        if (bytes.Length > 8 * 1024 * 1024 || !Magic(bytes, 0, "SARC") || U16(bytes, 6) != 0xFEFF || U32(bytes, 8) != bytes.Length)
            throw new InvalidDataException("The Y Comm HUD archive is unsupported.");
        var payload = U32(bytes, 12); var fat = U16(bytes, 4);
        Range(bytes, fat, 12);
        if (!Magic(bytes, fat, "SFAT") || U16(bytes, fat + 4) != 12) throw new InvalidDataException("Invalid HUD archive table.");
        var count = U16(bytes, fat + 6);
        var names = checked(fat + 12 + count * 16);
        Range(bytes, names, 8);
        if (!Magic(bytes, names, "SFNT") || U16(bytes, names + 4) != 8 || payload < names + 8)
            throw new InvalidDataException("Invalid HUD archive names.");
        var entries = new List<(int Start, int End)>(); var paths = new HashSet<string>(StringComparer.Ordinal);
        (int Start, int End)? layout = null;
        for (var i = 0; i < count; i++)
        {
            var node = fat + 12 + i * 16;
            var flags = U32(bytes, node + 4);
            if (flags >> 24 != 1) throw new InvalidDataException("Unnamed HUD archive member.");
            var nameAt = checked(names + 8 + (flags & 0xFFFFFF) * 4);
            if (nameAt >= payload) throw new InvalidDataException("Invalid HUD archive member name.");
            var endName = Array.IndexOf(bytes, (byte)0, nameAt, payload - nameAt);
            if (endName < nameAt) throw new InvalidDataException("Unterminated HUD archive member name.");
            var name = Encoding.UTF8.GetString(bytes, nameAt, endName - nameAt);
            var start = checked(payload + U32(bytes, node + 8)); var end = checked(payload + U32(bytes, node + 12));
            if (!paths.Add(name) || start < payload || end < start) throw new InvalidDataException("Invalid HUD archive member.");
            Range(bytes, start, end - start); entries.Add((start, end));
            if (name == LayoutName) layout = (start, end);
        }
        var ordered = entries.OrderBy(entry => entry.Start).ToArray();
        if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.End > pair.Second.Start) || layout is null)
            throw new InvalidDataException("HUD archive members overlap or the layout is missing.");
        var (first, last) = layout.Value;
        Range(bytes, first, 20);
        if (!Magic(bytes, first, "FLYT") || U16(bytes, first + 4) != 0xFEFF || U32(bytes, first + 12) != last - first)
            throw new InvalidDataException("The Y Comm HUD layout is unsupported.");
        var cursor = checked(first + U16(bytes, first + 6)); var sections = U16(bytes, first + 16); int? pane = null;
        for (var i = 0; i < sections; i++)
        {
            Range(bytes, cursor, 8); var size = U32(bytes, cursor + 4);
            if (size < 8 || cursor > last - size) throw new InvalidDataException("Invalid HUD layout section.");
            if (Magic(bytes, cursor, "pan1"))
            {
                if (size != 84) throw new InvalidDataException("Unsupported HUD pane.");
                var name = bytes.AsSpan(cursor + 12, 24);
                if (name[..4].SequenceEqual("all\0"u8))
                {
                    if (pane is not null) throw new InvalidDataException("Ambiguous HUD parent pane.");
                    pane = cursor;
                }
            }
            cursor += size;
        }
        if (cursor != last || pane is null) throw new InvalidDataException("The HUD parent pane is missing.");
        var scale = pane.Value + 68;
        var x = U32(bytes, scale); var y = U32(bytes, scale + 4);
        if (x != y || x is not (0 or 0x3F800000)) throw new InvalidDataException("The HUD parent scale contains conflicting changes.");
        var legacy = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(first, last - first))) == LegacyLayoutHash
            ? pane.Value + 48 : (int?)null;
        return (x == 0 || legacy is not null, scale, legacy);
    }

    internal static byte[] Apply(byte[] vanilla, byte[] source, bool disabled)
    {
        if (Convert.ToHexString(SHA256.HashData(vanilla)) != OriginalHash)
            throw new InvalidDataException("Y Comm requires the original supported HUD archive in Base RomFS.");
        var state = Read(source); var output = source.ToArray();
        // Older hidden layouts can be normalized without replacing other members.
        if (state.LegacyPosition is { } position) BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(position), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(state.Scale), disabled ? 0U : 0x3F800000U);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(state.Scale + 4), disabled ? 0U : 0x3F800000U);
        if (Read(output).Disabled != disabled) throw new InvalidDataException("Y Comm HUD verification failed.");
        return output;
    }

    private static int U16(byte[] bytes, int at) { Range(bytes, at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)); }
    private static int U32(byte[] bytes, int at) { Range(bytes, at, 4); return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at))); }
    private static bool Magic(byte[] bytes, int at, string value) { Range(bytes, at, 4); return bytes.AsSpan(at, 4).SequenceEqual(Encoding.ASCII.GetBytes(value)); }
    private static void Range(byte[] bytes, int at, int length)
    { if (at < 0 || length < 0 || at > bytes.Length - length) throw new InvalidDataException("The Y Comm HUD resource is truncated."); }
}
