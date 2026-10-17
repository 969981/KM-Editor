// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.Lua;

/// <summary>
/// Reads standard little endian Lua 5.3 chunks without translating instructions.
/// Integer edits retain the original envelope, code, strings and debug bytes.
/// </summary>
public sealed class Lua53BinaryChunk
{
    public const int MaximumInputBytes = 64 * 1024 * 1024;
    private readonly byte[] bytes;
    public IReadOnlyList<Lua53Prototype> Prototypes { get; }

    private Lua53BinaryChunk(byte[] bytes, List<Lua53Prototype> prototypes)
    {
        this.bytes = bytes;
        Prototypes = prototypes.AsReadOnly();
    }

    public static Lua53BinaryChunk Parse(ReadOnlySpan<byte> source)
    {
        if (source.Length is 0 or > MaximumInputBytes) throw Invalid();
        var bytes = source.ToArray();
        var reader = new Reader(bytes);
        reader.Expect([0x1b, 0x4c, 0x75, 0x61, 0x53, 0, 0x19, 0x93, 0x0d, 0x0a, 0x1a, 0x0a, 4, 8, 4, 8, 8]);
        if (reader.Int64() != 0x5678 || reader.Int64() != BitConverter.DoubleToInt64Bits(370.5)) throw Invalid();
        var upvalues = reader.Byte();
        reader.Prototype("0", null, 0);
        if (reader.Position != bytes.Length || reader.Prototypes[0].UpvalueCount != upvalues) throw Invalid();
        return new(bytes, reader.Prototypes);
    }

    public byte[] ToArray() => bytes.ToArray();

    public Lua53BinaryChunk WithIntegers(IReadOnlyDictionary<(string Prototype, int Constant), long> changes)
    {
        var result = ToArray();
        foreach (var (key, value) in changes)
        {
            var prototype = Prototypes.SingleOrDefault(p => p.Path == key.Prototype);
            if (prototype is null || key.Constant < 0 || key.Constant >= prototype.Constants.Count) throw Invalid();
            var constant = prototype.Constants[key.Constant];
            if (constant.Tag != 19) throw new InvalidDataException("The Lua 5.3 constant is not an integer.");
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(constant.Offset, 8), value);
        }
        return Parse(result);
    }

    private static InvalidDataException Invalid() => new("The Lua 5.3 chunk is malformed or uses an unsupported layout.");

    private sealed class Reader(byte[] bytes)
    {
        internal int Position { get; private set; }
        internal List<Lua53Prototype> Prototypes { get; } = [];
        private ReadOnlySpan<byte> Take(int size)
        {
            if (size < 0 || size > bytes.Length - Position) throw Invalid();
            var result = bytes.AsSpan(Position, size); Position += size; return result;
        }
        internal void Expect(ReadOnlySpan<byte> expected) { if (!Take(expected.Length).SequenceEqual(expected)) throw Invalid(); }
        internal byte Byte() => Take(1)[0];
        internal long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        private int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        private int Count(int minimumBytes)
        {
            var count = Int32();
            if (count < 0 || count > 1_000_000 || count > (bytes.Length - Position) / minimumBytes) throw Invalid();
            return count;
        }
        private bool String()
        {
            ulong size = Byte();
            if (size == 255) size = BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
            if (size > (ulong)(bytes.Length - Position) + 1) throw Invalid();
            if (size > 0) Take(checked((int)(size - 1)));
            return size != 0;
        }
        internal void Prototype(string path, Lua53Prototype? parent, int depth)
        {
            if (depth > 128 || Prototypes.Count >= 100_000) throw Invalid();
            String();
            var firstLine = Int32(); var lastLine = Int32();
            var parameters = Byte(); var vararg = Byte(); var stack = Byte();
            if (firstLine < 0 || lastLine < firstLine || vararg > 1 || stack < 2 || parameters > stack) throw Invalid();
            var code = new uint[Count(4)];
            for (var i = 0; i < code.Length; i++) code[i] = BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
            var constants = new Lua53Constant[Count(1)];
            for (var i = 0; i < constants.Length; i++)
            {
                var tag = Byte(); var offset = Position; long? integer = null;
                switch (tag)
                {
                    case 0: break;
                    case 1: if (Byte() > 1) throw Invalid(); break;
                    case 3: Take(8); break;
                    case 19: integer = Int64(); break;
                    case 4: case 20: if (!String()) throw Invalid(); break;
                    default: throw Invalid();
                }
                constants[i] = new(tag, offset, integer);
            }
            var upvalueCount = Count(2);
            if (upvalueCount > 255) throw Invalid();
            for (var i = 0; i < upvalueCount; i++)
            {
                var inStack = Byte(); var index = Byte();
                if (inStack > 1 || parent is not null && index >= (inStack == 1 ? parent.StackSize : parent.UpvalueCount)) throw Invalid();
            }
            var children = Count(1);
            var prototype = new Lua53Prototype(path, parameters, stack, upvalueCount, Array.AsReadOnly(code), Array.AsReadOnly(constants));
            Prototypes.Add(prototype);
            ValidateCode(prototype, children);
            for (var i = 0; i < children; i++) Prototype(path + "/" + i, prototype, depth + 1);
            var lines = Count(4);
            if (lines != 0 && lines != code.Length) throw Invalid();
            for (var i = 0; i < lines; i++) if (Int32() < 0) throw Invalid();
            var locals = Count(9);
            for (var i = 0; i < locals; i++)
            {
                String(); var start = Int32(); var end = Int32();
                if (start < 0 || start > end || end > code.Length) throw Invalid();
            }
            var names = Count(1);
            if (names != 0 && names != upvalueCount) throw Invalid();
            for (var i = 0; i < names; i++) String();
        }
    }

    private static void ValidateCode(Lua53Prototype p, int children)
    {
        void Register(int index) { if (index < 0 || index >= p.StackSize) throw Invalid(); }
        void Constant(int index) { if (index < 0 || index >= p.Constants.Count) throw Invalid(); }
        void Upvalue(int index) { if (index >= p.UpvalueCount) throw Invalid(); }
        void Operand(int index) { if (index >= 256) Constant(index - 256); else Register(index); }
        var code = p.Instructions;
        if (code.Count == 0 || (code[^1] & 63) != 38) throw Invalid();
        for (var pc = 0; pc < code.Count; pc++)
        {
            var word = code[pc]; var op = (int)(word & 63); var a = (int)(word >> 6 & 255);
            var b = (int)(word >> 23); var c = (int)(word >> 14 & 511); var bx = (int)(word >> 14);
            if (op > 46) throw Invalid();
            if (op is not (8 or 30 or 31 or 32 or 33 or 46)) Register(a);
            switch (op)
            {
                case 0: Register(b); break;
                case 1: Constant(bx); break;
                case 2:
                    if (pc + 1 >= code.Count || (code[pc + 1] & 63) != 46) throw Invalid();
                    Constant((int)(code[pc + 1] >> 6)); break;
                case 3: if (b > 1 || c > 1 || c != 0 && pc + 2 >= code.Count) throw Invalid(); break;
                case 4: Register(a + b); break;
                case 5: Upvalue(b); break;
                case 6: Upvalue(b); Operand(c); break;
                case 7: Register(b); Operand(c); break;
                case 8: Upvalue(a); Operand(b); Operand(c); break;
                case 9: Upvalue(b); break;
                case 10: Operand(b); Operand(c); break;
                case 12: Register(a + 1); Register(b); Operand(c); break;
                case >= 13 and <= 24: Operand(b); Operand(c); break;
                case >= 25 and <= 28: Register(b); break;
                case 29: Register(b); Register(c); if (b > c) throw Invalid(); break;
                case 30: if (a > p.StackSize) throw Invalid(); goto case 39;
                case 39: case 40: case 42:
                    var target = pc + 1 + bx - 131071;
                    if (target < 0 || target >= code.Count || (code[target] & 63) == 46) throw Invalid();
                    if (op is 39 or 40) Register(a + 3);
                    if (op == 42) Register(a + 1);
                    break;
                case 31: case 32: case 33:
                    if (a > 1) throw Invalid(); Operand(b); Operand(c); goto case 34;
                case 35: Register(b); goto case 34;
                case 34:
                    if (op >= 34 && c > 1 || pc + 1 >= code.Count || (code[pc + 1] & 63) != 30) throw Invalid(); break;
                case 36: case 37:
                    if (b > 0) Register(a + b - 1);
                    if (c > 1) Register(a + c - 2);
                    if (op == 37 && c != 0) throw Invalid(); break;
                case 38: if (b > 1) Register(a + b - 2); break;
                case 41: Register(a + 2 + c); if (c == 0) throw Invalid(); break;
                case 43:
                    if (b > 0) Register(a + b);
                    if (c == 0 && (pc + 1 >= code.Count || (code[pc + 1] & 63) != 46)) throw Invalid(); break;
                case 44: if (bx >= children) throw Invalid(); break;
                case 45: if (b > 1) Register(a + b - 2); break;
                case 46:
                    if (pc == 0 || !((code[pc - 1] & 63) == 2 || (code[pc - 1] & 63) == 43 && (code[pc - 1] >> 14 & 511) == 0)) throw Invalid(); break;
            }
        }
    }
}

public sealed record Lua53Constant(byte Tag, int Offset, long? Integer);
public sealed record Lua53Prototype(string Path, int ParameterCount, int StackSize, int UpvalueCount,
    IReadOnlyList<uint> Instructions, IReadOnlyList<Lua53Constant> Constants);
