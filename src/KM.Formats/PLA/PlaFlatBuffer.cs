// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.PLA;

/// <summary>Bounded access and preservation edits for PLA's loose scalar tables.</summary>
public sealed class PlaFlatBuffer
{
    public const int MaximumLength = 64 * 1024 * 1024;
    private byte[] data;

    public PlaFlatBuffer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 8 or > MaximumLength)
            throw new InvalidDataException("The PLA table size is invalid.");
        data = bytes.ToArray();
        _ = TableAtReference(0);
    }

    public byte[] ToArray() => (byte[])data.Clone();
    public int Root => TableAtReference(0);

    private void Bounds(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException("A PLA table reference is outside the file.");
    }

    private ushort U16(int offset) { Bounds(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)); }
    private int I32(int offset) { Bounds(offset, 4); return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)); }
    private uint U32(int offset) { Bounds(offset, 4); return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)); }
    private void W16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
    private void W32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);

    private (int Vtable, int Size, int Count) Table(int table)
    {
        var vtable = checked(table - I32(table));
        var size = U16(vtable);
        if (size < 4 || size % 2 != 0 || U16(vtable + 2) < 4)
            throw new InvalidDataException("The PLA vtable is invalid.");
        Bounds(vtable, size);
        Bounds(table, U16(vtable + 2));
        return (vtable, U16(vtable + 2), (size - 4) / 2);
    }

    public int TableAtReference(int reference)
    {
        var displacement = U32(reference);
        if (displacement < 4 || displacement > int.MaxValue)
            throw new InvalidDataException("The PLA table offset is invalid.");
        var table = checked(reference + (int)displacement);
        _ = Table(table);
        return table;
    }

    public int Field(int table, int index, int width)
    {
        if (index < 0 || width is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(index));
        var info = Table(table);
        if (index >= info.Count) return 0;
        var offset = U16(info.Vtable + 4 + index * 2);
        if (offset == 0) return 0;
        if (offset < 4 || offset > info.Size - width)
            throw new InvalidDataException("A PLA field extends outside its table.");
        return table + offset;
    }

    public long Scalar(int table, int index, int width = 2, bool signed = false)
    {
        var offset = Field(table, index, width);
        if (offset == 0) return 0;
        return width switch
        {
            1 => signed ? (sbyte)data[offset] : data[offset],
            2 => signed ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset)) : U16(offset),
            4 => signed ? I32(offset) : U32(offset),
            _ => throw new ArgumentOutOfRangeException(nameof(width)),
        };
    }

    public int[] VectorReferences(int table, int field, int maximum = 100_000)
    {
        var pointer = Field(table, field, 4);
        if (pointer == 0) return [];
        var displacement = U32(pointer);
        if (displacement < 4 || displacement > int.MaxValue)
            throw new InvalidDataException("The PLA vector offset is invalid.");
        var vector = checked(pointer + (int)displacement);
        var count = U32(vector);
        if (count > maximum) throw new InvalidDataException("The PLA vector is too large.");
        Bounds(vector + 4, checked((int)count * 4));
        var references = new int[count];
        for (var i = 0; i < references.Length; i++)
        {
            references[i] = vector + 4 + i * 4;
            _ = TableAtReference(references[i]);
        }
        return references;
    }

    private (int Offset, int Length) StringRange(int pointer, int maximum)
    {
        var displacement = U32(pointer);
        if (displacement < 4 || displacement > int.MaxValue)
            throw new InvalidDataException("The PLA string offset is invalid.");
        var start = checked(pointer + (int)displacement);
        var length = U32(start);
        if (length > maximum) throw new InvalidDataException("The PLA string is too large.");
        Bounds(start, checked(5 + (int)length));
        if (data[start + 4 + (int)length] != 0) throw new InvalidDataException("The PLA string terminator is invalid.");
        return (start, (int)length);
    }

    public string String(int table, int field, int maximum = 4096)
    {
        var pointer = Field(table, field, 4);
        if (pointer == 0) return "";
        var range = StringRange(pointer, maximum);
        try { return new UTF8Encoding(false, true).GetString(data, range.Offset + 4, range.Length); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("The PLA string is not valid UTF-8.", exception); }
    }

    /// <summary>Copy a known object subtree before editing, preserving strings and scalar child tables.</summary>
    public void SetObjectScalars(int reference, IReadOnlyList<int> widths, IReadOnlyDictionary<int, long> values,
        IReadOnlyList<int> stringFields, IReadOnlyDictionary<int, int[]> scalarChildren)
    {
        var original = TableAtReference(reference);
        RequireKnownFields(original, widths);
        if (values.Keys.Any(key => key < 0 || key >= widths.Count || stringFields.Contains(key) || scalarChildren.ContainsKey(key))
            || stringFields.Concat(scalarChildren.Keys).Any(key => key < 0 || key >= widths.Count || widths[key] != 4)
            || stringFields.Distinct().Count() != stringFields.Count || stringFields.Any(scalarChildren.ContainsKey))
            throw new InvalidDataException("The PLA object schema is invalid.");
        var strings = stringFields.Where(key => Field(original, key, 4) != 0)
            .ToDictionary(key => key, key => StringRange(Field(original, key, 4), MaximumLength - 5));
        var children = scalarChildren.Where(pair => Field(original, pair.Key, 4) != 0)
            .ToDictionary(pair => pair.Key, pair => TableAtReference(Field(original, pair.Key, 4)));
        foreach (var child in children) RequireKnownFields(child.Value, scalarChildren[child.Key]);

        // References are forward offsets. Every referenced value must follow the
        // copied parent, even when the original objects shared their storage.
        var copy = CopyObject(original, widths, values);
        foreach (var pair in strings)
        {
            var range = pair.Value;
            var target = Allocate(5 + range.Length);
            data.AsSpan(range.Offset, 5 + range.Length).CopyTo(data.AsSpan(target));
            var pointer = Field(copy, pair.Key, 4);
            W32(pointer, target - pointer);
        }
        foreach (var pair in children)
        {
            var target = CopyObject(pair.Value, scalarChildren[pair.Key], new Dictionary<int, long>());
            var pointer = Field(copy, pair.Key, 4);
            W32(pointer, target - pointer);
        }
        W32(reference, copy - reference);
        SetScalars(reference, widths, values);
    }

    private int CopyObject(int original, IReadOnlyList<int> widths, IReadOnlyDictionary<int, long> values)
    {
        var info = Table(original);
        var offsets = Enumerable.Range(0, widths.Count).Select(i => Field(original, i, widths[i]) is var f && f != 0 ? f - original : 0).ToArray();
        var size = info.Size;
        foreach (var pair in values.Where(pair => pair.Value != 0 && offsets[pair.Key] == 0))
        {
            var width = widths[pair.Key];
            size = (size + width - 1) / width * width;
            offsets[pair.Key] = size;
            size += width;
        }
        if (size > ushort.MaxValue) throw new InvalidDataException("The PLA object is too large.");
        var vtable = Allocate(4 + widths.Count * 2, 2);
        var alignmentOffset = original % 8;
        var copy = Allocate(size + alignmentOffset, 8) + alignmentOffset;
        data.AsSpan(original, info.Size).CopyTo(data.AsSpan(copy));
        W16(vtable, checked((ushort)(4 + widths.Count * 2)));
        W16(vtable + 2, (ushort)size);
        for (var i = 0; i < offsets.Length; i++) W16(vtable + 4 + i * 2, (ushort)offsets[i]);
        W32(copy, copy - vtable);
        return copy;
    }

    private int Allocate(int count, int alignment = 4)
    {
        var start = checked((data.Length + alignment - 1) / alignment * alignment);
        var length = checked(start + count);
        if (length > MaximumLength) throw new InvalidDataException("The edited PLA table exceeds the size limit.");
        Array.Resize(ref data, length);
        return start;
    }

    private void RequireKnownFields(int table, IReadOnlyList<int> widths)
    {
        var info = Table(table);
        for (var i = 0; i < info.Count; i++)
            if (i >= widths.Count && U16(info.Vtable + 4 + 2 * i) != 0)
                throw new InvalidDataException("An unknown PLA field prevents a structural edit.");
        var occupied = new HashSet<int>();
        for (var i = 0; i < widths.Count; i++)
        {
            var field = Field(table, i, widths[i]);
            if (field == 0) continue;
            for (var p = field; p < field + widths[i]; p++)
                if (!occupied.Add(p)) throw new InvalidDataException("PLA fields overlap.");
        }
    }

    // The supplied schema must contain only scalars. Existing padding and unknown
    // bytes remain in the original buffer; missing defaults get their own table.
    public void SetScalars(int reference, IReadOnlyList<int> widths, IReadOnlyDictionary<int, long> values)
    {
        var table = TableAtReference(reference);
        RequireKnownFields(table, widths);
        foreach (var pair in values)
            if (pair.Key < 0 || pair.Key >= widths.Count)
                throw new InvalidDataException("The PLA field is not supported.");
        if (values.Any(pair => pair.Value != 0 && Field(table, pair.Key, widths[pair.Key]) == 0))
        {
            var info = Table(table);
            var offsets = Enumerable.Range(0, widths.Count)
                .Select(i => Field(table, i, widths[i]) is var f && f != 0 ? f - table : 0).ToArray();
            var size = info.Size;
            foreach (var pair in values.Where(pair => pair.Value != 0 && offsets[pair.Key] == 0))
            {
                var width = widths[pair.Key];
                size = (size + width - 1) / width * width;
                offsets[pair.Key] = size;
                size += width;
            }
            if (size > ushort.MaxValue) throw new InvalidDataException("The PLA table is too large.");
            var vtable = Allocate(4 + widths.Count * 2, 2);
            var alignmentOffset = table % 8;
            var copy = Allocate(size + alignmentOffset, 8) + alignmentOffset;
            data.AsSpan(table, info.Size).CopyTo(data.AsSpan(copy));
            W16(vtable, checked((ushort)(4 + widths.Count * 2)));
            W16(vtable + 2, (ushort)size);
            for (var i = 0; i < offsets.Length; i++) W16(vtable + 4 + i * 2, (ushort)offsets[i]);
            W32(copy, copy - vtable);
            W32(reference, copy - reference);
            table = copy;
        }
        foreach (var pair in values)
        {
            var offset = Field(table, pair.Key, widths[pair.Key]);
            if (offset == 0) continue;
            var value = pair.Value;
            switch (widths[pair.Key])
            {
                case 1 when value is >= sbyte.MinValue and <= byte.MaxValue: data[offset] = unchecked((byte)value); break;
                case 2 when value is >= short.MinValue and <= ushort.MaxValue: W16(offset, unchecked((ushort)value)); break;
                case 4 when value is >= int.MinValue and <= uint.MaxValue: W32(offset, unchecked((int)value)); break;
                default: throw new InvalidDataException("The PLA scalar is outside its storage range.");
            }
        }
    }

    /// <summary>Replace a fully understood vector, retaining every other byte.</summary>
    public void ReplaceUShortRows(int parentReference, int field, IReadOnlyList<ushort[]> rows, int columns)
    {
        if (rows.Count > 1024 || rows.Any(row => row.Length != columns) || columns is < 1 or > 8)
            throw new InvalidDataException("The PLA row collection is invalid.");
        var table = TableAtReference(parentReference);
        var pointer = Field(table, field, 4);
        if (pointer == 0)
        {
            // Evolution sets have two identity scalars and one optional vector.
            // No other references may be relocated by this operation.
            if (field != 2) throw new InvalidDataException("The PLA vector is absent in this layout.");
            SetScalars(parentReference, [2, 2, 4], new Dictionary<int, long> { [2] = 1 });
            table = TableAtReference(parentReference);
            pointer = Field(table, field, 4);
        }
        else
        {
            foreach (var item in VectorReferences(table, field, 1024))
                RequireKnownFields(TableAtReference(item), Enumerable.Repeat(2, columns).ToArray());
        }
        var vector = Allocate(4 + rows.Count * 4);
        W32(vector, rows.Count);
        W32(pointer, vector - pointer);
        for (var i = 0; i < rows.Count; i++)
        {
            var vtable = Allocate(4 + columns * 2, 2);
            var row = Allocate(4 + columns * 2);
            W16(vtable, (ushort)(4 + columns * 2));
            W16(vtable + 2, (ushort)(4 + columns * 2));
            W32(row, row - vtable);
            W32(vector + 4 + i * 4, row - (vector + 4 + i * 4));
            for (var j = 0; j < columns; j++)
            {
                W16(vtable + 4 + j * 2, (ushort)(4 + j * 2));
                W16(row + 4 + j * 2, rows[i][j]);
            }
        }
    }
}
