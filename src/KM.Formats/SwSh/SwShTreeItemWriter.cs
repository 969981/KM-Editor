// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;

namespace KM.Formats.SwSh;

public sealed record SwShTreeItemEdit(int ZoneIndex, int ObjectIndex, int Slot, ulong ItemHash, uint Percent);

/// <summary>Replaces individual scalar item rows without changing shared rows or opaque placement data.</summary>
public static class SwShTreeItemWriter
{
    public static byte[] Write(byte[] source, IReadOnlyList<SwShTreeItemEdit> edits)
    {
        var archive = SwShPlacementZoneArchive.Parse(source);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(source);
        var owned = new HashSet<int>();
        foreach (var edit in edits)
        {
            if ((uint)edit.ZoneIndex >= archive.Zones.Count || edit.Percent > 100 || edit.Slot is < 0 or > 9)
                throw new InvalidDataException("Invalid tree item slot.");
            var tree = archive.Zones[edit.ZoneIndex].RawObjects.Single(row => row.ObjectType == "BerryTree" && row.ObjectIndex == edit.ObjectIndex);
            var prefix = $"raw.BerryTree.Field_01[{edit.Slot}]";
            var hash = tree.Fields.Single(row => row.Field == prefix + ".Hash");
            var quantity = tree.Fields.Single(row => row.Field == prefix + ".Quantity");
            var reference = hash.TableReferenceOffset;
            var table = hash.TableOffset;
            if (reference <= 0 || reference > source.Length - 4 || table <= 0 || !owned.Add(reference))
                throw new InvalidDataException("The tree item row reference is unavailable or repeated.");
            var vt = checked(table - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(table)));
            if (vt < 0 || vt > source.Length - 10 || BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vt)) > 10
                || reference + BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(reference)) != table)
                throw new InvalidDataException("The tree item row contains unsupported fields or references.");
            // Its vector must belong to one tree. Sharing scalar rows is safe because each row is copied.
            if (archive.Zones.SelectMany(zone => zone.RawObjects).SelectMany(row => row.Fields)
                .Count(field => field.Field.EndsWith($"Field_01[{edit.Slot}].Hash", StringComparison.Ordinal) && field.TableReferenceOffset == reference) != 1)
                throw new InvalidDataException("The tree item vector is shared by another placement object.");
            while (stream.Position % 8 != 0) writer.Write((byte)0);
            writer.Write((ushort)10); writer.Write((ushort)24);
            writer.Write((ushort)8); writer.Write((ushort)16); writer.Write((ushort)20);
            while (stream.Position % 8 != 0) writer.Write((byte)0);
            var start = checked((int)stream.Position);
            writer.Write(16); writer.Write(0);
            writer.Write(edit.ItemHash); writer.Write(edit.Percent);
            writer.Write(uint.Parse(quantity.Value, CultureInfo.InvariantCulture));
            var end = stream.Position;
            stream.Position = reference; writer.Write(checked((uint)(start - reference))); stream.Position = end;
        }
        var result = stream.ToArray();
        var verified = SwShPlacementZoneArchive.Parse(result);
        foreach (var edit in edits)
        {
            var tree = verified.Zones[edit.ZoneIndex].RawObjects.Single(row => row.ObjectType == "BerryTree" && row.ObjectIndex == edit.ObjectIndex);
            var prefix = $"raw.BerryTree.Field_01[{edit.Slot}]";
            var hash = tree.Fields.Single(row => row.Field == prefix + ".Hash").Value;
            var percent = tree.Fields.Single(row => row.Field == prefix + ".Rate").Value;
            if (ulong.Parse(hash.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) != edit.ItemHash
                || uint.Parse(percent, CultureInfo.InvariantCulture) != edit.Percent)
                throw new InvalidDataException("Tree item output failed readback.");
        }
        return result;
    }
}
