// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed partial class SwShItemTable
{
    public byte[] ReadRow(int itemId)
    {
        if (!recordsByItemId.TryGetValue(itemId, out var record))
            throw new InvalidDataException("Item identity is outside the table.");
        return data.AsSpan(RowsStart + record.RawRowIndex * RowSize, RowSize).ToArray();
    }

    /// <summary>Writes independent item records without modifying other aliases of a shared row.</summary>
    public byte[] WriteIndependentRows(IReadOnlyDictionary<int, byte[]> changes)
    {
        if (changes.Count > 512 || changes.Any(c => c.Key <= 0 || c.Key >= RawRowIndexes.Count || c.Value.Length != RowSize))
            throw new InvalidDataException("Independent item rows exceed their identity or size limits.");
        if (changes.All(c => ReadRow(c.Key).AsSpan().SequenceEqual(c.Value))) return data.ToArray();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(MaxRowIndexOffset));
        var rows = Enumerable.Range(0, count).Select(i => data.AsSpan(RowsStart + i * RowSize, RowSize).ToArray()).ToList();
        var indexes = RawRowIndexes.ToArray();
        var released = new HashSet<int>();
        foreach (var (id, bytes) in changes.OrderBy(c => c.Key))
        {
            var row = indexes[id];
            if (rows[row].AsSpan().SequenceEqual(bytes)) continue;
            if (rows[0].AsSpan().SequenceEqual(bytes)) { indexes[id] = 0; released.Add(row); continue; }
            if (row != 0 && indexes.Count(i => i == row) == 1) rows[row] = bytes.ToArray();
            else { indexes[id] = rows.Count; rows.Add(bytes.ToArray()); }
        }
        // Only records released by this edit may be trimmed. Preserve pre-existing
        // unreferenced rows and never renumber another item's live record.
        var referenced = indexes.ToHashSet();
        while (rows.Count > 1 && released.Contains(rows.Count - 1) && !referenced.Contains(rows.Count - 1)) rows.RemoveAt(rows.Count - 1);
        if (rows.Count > 4096) throw new InvalidDataException("Independent item rows exceed the table budget.");
        var previousEnd = RowsStart + count * RowSize;
        var delta = (rows.Count - count) * RowSize;
        var output = new byte[checked(data.Length + delta)];
        data.AsSpan(0, RowsStart).CopyTo(output);
        data.AsSpan(previousEnd).CopyTo(output.AsSpan(previousEnd + delta));
        for (var i = 0; i < rows.Count; i++) rows[i].CopyTo(output, RowsStart + i * RowSize);
        for (var i = 0; i < indexes.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(EntryTableOffset + i * 2), checked((ushort)indexes[i]));
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(MaxRowIndexOffset), checked((ushort)rows.Count));
        if (machineTableOffset >= previousEnd)
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(MachineTablePointerOffset),
                checked((ushort)((machineTableOffset + delta - EntryTableOffset) / 2)));
        _ = Parse(output, 4096);
        return output;
    }
}
