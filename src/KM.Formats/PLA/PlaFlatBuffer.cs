// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.PLA;

/// <summary>Bounded access and preservation edits for PLA tables.</summary>
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

    public ulong Hash(int table, int index)
    {
        var offset = Field(table, index, 8);
        return offset == 0 ? 0 : BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
    }

    public float Float(int table, int index) => BitConverter.Int32BitsToSingle(unchecked((int)Scalar(table, index, 4)));

    public sealed record ObjectLayout(int[] Widths, int[] Strings,
        IReadOnlyDictionary<int, ObjectLayout> Children, IReadOnlyDictionary<int, ObjectLayout> Vectors);

    // Clone the entire known subtree so editing one slot never changes shared children.
    // Edit keys are reference positions, not object positions: aliases remain independent.
    public void EditObjectTree(int reference, ObjectLayout layout,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, long>> edits)
    {
        var applied = new HashSet<int>();
        int Copy(int sourceReference, ObjectLayout schema, int depth)
        {
            if (depth > 8) throw new InvalidDataException("The PLA object tree is too deep.");
            var original = TableAtReference(sourceReference);
            RequireKnownFields(original, schema.Widths);
            var pointers = schema.Strings.Concat(schema.Children.Keys).Concat(schema.Vectors.Keys).ToArray();
            var values = edits.GetValueOrDefault(sourceReference) ?? new Dictionary<int, long>();
            if (pointers.Distinct().Count() != pointers.Length || pointers.Any(key => key < 0 || key >= schema.Widths.Length || schema.Widths[key] != 4)
                || values.Keys.Any(key => key < 0 || key >= schema.Widths.Length || pointers.Contains(key)))
                throw new InvalidDataException("The PLA object edit is invalid.");
            if (edits.ContainsKey(sourceReference)) applied.Add(sourceReference);
            var strings = schema.Strings.Where(key => Field(original, key, 4) != 0)
                .ToDictionary(key => key, key => StringRange(Field(original, key, 4), 4096));
            var children = schema.Children.Where(pair => Field(original, pair.Key, 4) != 0)
                .ToDictionary(pair => pair.Key, pair => Field(original, pair.Key, 4));
            var vectors = schema.Vectors.Where(pair => Field(original, pair.Key, 4) != 0)
                .ToDictionary(pair => pair.Key, pair => VectorReferences(original, pair.Key, 4096));
            var copy = CopyObject(original, schema.Widths, values);
            foreach (var (key, value) in values)
            {
                var field = Field(copy, key, schema.Widths[key]);
                if (field == 0) continue;
                switch (schema.Widths[key])
                {
                    case 1 when value is >= sbyte.MinValue and <= byte.MaxValue: data[field] = unchecked((byte)value); break;
                    case 2 when value is >= short.MinValue and <= ushort.MaxValue: W16(field, unchecked((ushort)value)); break;
                    case 4 when value is >= int.MinValue and <= uint.MaxValue: W32(field, unchecked((int)value)); break;
                    default: throw new InvalidDataException("The PLA scalar is outside its storage range.");
                }
            }
            foreach (var (key, range) in strings)
            {
                var target = Allocate(5 + range.Length);
                data.AsSpan(range.Offset, 5 + range.Length).CopyTo(data.AsSpan(target));
                var pointer = Field(copy, key, 4); W32(pointer, target - pointer);
            }
            foreach (var (key, child) in children)
            {
                var target = Copy(child, schema.Children[key], depth + 1);
                var pointer = Field(copy, key, 4); W32(pointer, target - pointer);
            }
            foreach (var (key, entries) in vectors)
            {
                var vector = Allocate(4 + entries.Length * 4); W32(vector, entries.Length);
                var pointer = Field(copy, key, 4); W32(pointer, vector - pointer);
                for (var i = 0; i < entries.Length; i++)
                {
                    var target = Copy(entries[i], schema.Vectors[key], depth + 1);
                    var entry = vector + 4 + i * 4; W32(entry, target - entry);
                }
            }
            return copy;
        }
        var result = Copy(reference, layout, 0);
        if (applied.Count != edits.Count) throw new InvalidDataException("A PLA object edit does not belong to the selected tree.");
        W32(reference, result - reference);
    }

    // For qualified fixed records with opaque children, change only an existing scalar.
    public void SetExistingInt32(int reference, IReadOnlyList<int> widths, int index, int value)
    {
        var table = TableAtReference(reference);
        RequireKnownFields(table, widths);
        if (index < 0 || index >= widths.Count || widths[index] != 4) throw new InvalidDataException("The PLA integer field is unsupported.");
        var field = Field(table, index, 4);
        if (field == 0) throw new InvalidDataException("The PLA field is absent in this record.");
        var info = Table(table);
        for (var i = 0; i < info.Count; i++)
        {
            if (i == index) continue;
            var offset = U16(info.Vtable + 4 + i * 2);
            if (offset != 0 && table + offset >= field && table + offset < field + 4)
                throw new InvalidDataException("PLA scalar fields overlap.");
        }
        W32(field, value);
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

    public string[] StringVector(int table, int field, int maximum = 64)
    {
        var pointer = Field(table, field, 4);
        if (pointer == 0) return [];
        var displacement = U32(pointer);
        if (displacement < 4 || displacement > int.MaxValue) throw new InvalidDataException("The PLA string vector offset is invalid.");
        var vector = checked(pointer + (int)displacement);
        var count = U32(vector);
        if (count > maximum) throw new InvalidDataException("The PLA string vector is too large.");
        Bounds(vector + 4, checked((int)count * 4));
        return Enumerable.Range(0, (int)count).Select(i =>
        {
            var range = StringRange(vector + 4 + i * 4, 4096);
            try { return new UTF8Encoding(false, true).GetString(data, range.Offset + 4, range.Length); }
            catch (DecoderFallbackException exception) { throw new InvalidDataException("The PLA string is not valid UTF-8.", exception); }
        }).ToArray();
    }

    /// <summary>Copy a known object subtree before editing, preserving strings and scalar child tables.</summary>
    public void SetObjectScalars(int reference, IReadOnlyList<int> widths, IReadOnlyDictionary<int, long> values,
        IReadOnlyList<int> stringFields, IReadOnlyDictionary<int, int[]> scalarChildren,
        IReadOnlyDictionary<int, int[]>? scalarVectors = null)
    {
        scalarVectors ??= new Dictionary<int, int[]>();
        var original = TableAtReference(reference);
        RequireKnownFields(original, widths);
        var references = stringFields.Concat(scalarChildren.Keys).Concat(scalarVectors.Keys).ToArray();
        if (values.Keys.Any(key => key < 0 || key >= widths.Count || references.Contains(key))
            || references.Any(key => key < 0 || key >= widths.Count || widths[key] != 4)
            || references.Distinct().Count() != references.Length)
            throw new InvalidDataException("The PLA object schema is invalid.");
        var strings = stringFields.Where(key => Field(original, key, 4) != 0)
            .ToDictionary(key => key, key => StringRange(Field(original, key, 4), MaximumLength - 5));
        var children = scalarChildren.Where(pair => Field(original, pair.Key, 4) != 0)
            .ToDictionary(pair => pair.Key, pair => TableAtReference(Field(original, pair.Key, 4)));
        foreach (var child in children) RequireKnownFields(child.Value, scalarChildren[child.Key]);
        var vectors = scalarVectors.Where(pair => Field(original, pair.Key, 4) != 0)
            .ToDictionary(pair => pair.Key, pair => VectorReferences(original, pair.Key, 1024).Select(TableAtReference).ToArray());
        foreach (var vector in vectors)
            foreach (var child in vector.Value) RequireKnownFields(child, scalarVectors[vector.Key]);

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
        foreach (var pair in vectors)
        {
            var vector = Allocate(4 + pair.Value.Length * 4);
            W32(vector, pair.Value.Length);
            var pointer = Field(copy, pair.Key, 4);
            W32(pointer, vector - pointer);
            for (var i = 0; i < pair.Value.Length; i++)
            {
                var target = CopyObject(pair.Value[i], scalarVectors[pair.Key], new Dictionary<int, long>());
                var entry = vector + 4 + i * 4;
                W32(entry, target - entry);
            }
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
