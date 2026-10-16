// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.PLA;

public sealed class PlaItemTable
{
    private const int RecordSize = 60;
    private readonly byte[] data;
    private readonly int start;
    private readonly int[] indices;
    private readonly Dictionary<int, int> owners;
    public int Count => indices.Length;

    public PlaItemTable(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 0x4c || bytes.Length > PlaFlatBuffer.MaximumLength)
            throw new InvalidDataException("The PLA item table size is invalid.");
        data = bytes.ToArray();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        var physical = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        start = BinaryPrimitives.ReadInt32LittleEndian(bytes[0x48..]);
        if (count is 0 or > 4096 || physical == 0 || start < 0x4c + count * 2 || start > data.Length - physical * RecordSize)
            throw new InvalidDataException("The PLA item record boundaries are invalid.");
        indices = Enumerable.Range(0, count).Select(id => (int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x4c + id * 2))).ToArray();
        if (indices.Any(index => index >= physical)) throw new InvalidDataException("A PLA item index is outside the table.");
        owners = indices.GroupBy(index => index).ToDictionary(group => group.Key, group => group.Count());
    }

    public bool IsIndependent(int id) => id > 0 && id < Count && indices[id] != 0 && owners[indices[id]] == 1;
    public bool HasSameIndex(PlaItemTable other) => start == other.start && indices.AsSpan().SequenceEqual(other.indices);
    private int Offset(int id)
    {
        if (!IsIndependent(id)) throw new InvalidDataException("This PLA item shares a protected record or is unavailable.");
        return start + indices[id] * RecordSize;
    }
    public bool IsInventoryItem(int id) => IsIndependent(id) && (data[Offset(id) + 0x11] & 15) == 0
        && data[Offset(id) + 0x16] is not (7 or 9 or 10 or 255);
    public long Value(int id, string field)
    {
        var record = data.AsSpan(Offset(id), RecordSize);
        return field switch
        {
            "price" => BinaryPrimitives.ReadUInt32LittleEndian(record),
            "meritPrice" => BinaryPrimitives.ReadUInt16LittleEndian(record[8..]),
            "maxQuantity" => BinaryPrimitives.ReadUInt16LittleEndian(record[0x1c..]),
            _ => throw new InvalidDataException("The PLA item field is unsupported."),
        };
    }
    public void Set(int id, string field, long value)
    {
        if (!IsInventoryItem(id)) throw new InvalidDataException("This PLA item category is protected.");
        var record = data.AsSpan(Offset(id), RecordSize);
        switch (field)
        {
            case "price" when value is >= 0 and <= 999999: BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)value); break;
            case "meritPrice" when value is >= 0 and <= 65535: BinaryPrimitives.WriteUInt16LittleEndian(record[8..], (ushort)value); break;
            case "maxQuantity" when value is >= 1 and <= 999: BinaryPrimitives.WriteUInt16LittleEndian(record[0x1c..], (ushort)value); break;
            default: throw new InvalidDataException("The PLA item value is outside its supported range.");
        }
    }
    public byte[] ToArray() => (byte[])data.Clone();
}
