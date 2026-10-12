// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

/// <summary>Copies only one tree subtable, preserving all original and unrelated archive bytes.</summary>
public static class SwShTreeEncounterWriter
{
    public static byte[] Write(byte[] source, int tableIndex, SwShWildEncounterSubTable desired)
    {
        var buffer = new ModelBuffer(source);
        var tables = buffer.Tables(buffer.Root, 1, 4096);
        if ((uint)tableIndex >= tables.Length || desired.Slots.Count is < 1 or > 20)
            throw new InvalidDataException("The tree encounter table is unavailable.");
        var vectors = tables.Select(table => buffer.Vector(table, 1, 4, 32)).ToArray();
        var vector = vectors[tableIndex];
        if (vector.Count <= 9 || vectors.Count(other => other.Start == vector.Start) != 1)
            throw new InvalidDataException("The tree encounter vector is missing or shared.");
        var reference = vector.Start + 9 * 4;
        var subtable = buffer.Offset(reference);
        var slots = buffer.Tables(subtable, 2, 20);
        if (slots.Length != desired.Slots.Count) throw new InvalidDataException("The tree encounter slot count changed.");
        foreach (var table in slots.Append(subtable))
            if (buffer.U16(table - buffer.I32(table)) > 10)
                throw new InvalidDataException("The tree encounter table has unsupported additional fields.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(source);
        Align();
        var tableStart = Table(4, 5, 8);
        writer.Write(desired.LevelMin); writer.Write(desired.LevelMax); writer.Write((ushort)0);
        writer.Write(4U);
        writer.Write(desired.Slots.Count);
        var references = (int)stream.Position;
        foreach (var _ in slots) writer.Write(0U);
        for (var index = 0; index < slots.Length; index++)
        {
            Align();
            var at = Table(4, 8, 5);
            var row = desired.Slots[index];
            writer.Write(row.Probability); writer.Write(row.Form); writer.Write((ushort)0); writer.Write(row.Species);
            Patch(references + index * 4, at);
        }
        Patch(reference, tableStart);
        var output = stream.ToArray();
        var actual = SwShWildEncounterArchive.Parse(output).Tables[tableIndex].SubTables[9];
        if (actual.LevelMin != desired.LevelMin || actual.LevelMax != desired.LevelMax || !actual.Slots.SequenceEqual(desired.Slots))
            throw new InvalidDataException("Tree encounter output failed readback.");
        return output;

        void Align() { while (stream.Position % 4 != 0) writer.Write((byte)0); }
        int Table(ushort first, ushort second, ushort third)
        {
            writer.Write((ushort)10); writer.Write((ushort)12); writer.Write(first); writer.Write(second); writer.Write(third);
            writer.Write((ushort)0);
            var at = checked((int)stream.Position); writer.Write(12); return at;
        }
        void Patch(int at, int target)
        {
            var end = stream.Position; stream.Position = at; writer.Write(checked((uint)(target - at))); stream.Position = end;
        }
    }
}
