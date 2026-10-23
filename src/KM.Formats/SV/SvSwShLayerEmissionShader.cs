// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SV;

public static class SvSwShLayerEmissionShader
{
    public static byte[] Convert(byte[] template)
    {
        int Integer(int offset) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(offset, 4));
        int Pointer(int offset) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(offset, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8) || Integer(0x7c) != 1)
            throw new InvalidDataException("Layer emission shader container is unsupported.");
        var program = Pointer(Pointer(0x80) + 16);
        var stage = Pointer(program + 0x28);
        var control = Pointer(stage + 8);
        var data = Pointer(stage + 16);
        var length = Integer(stage + 24);
        var controlLength = Integer(stage + 28);
        var identity = System.Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length)));
        if (identity is not ("1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37" or "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2"))
            throw new InvalidDataException("Layer emission shader program needs another adapter.");
        if (controlLength < 0x848 || Integer(control) != unchecked((int)0x98761234) || Integer(data) != 0x12345678
            || Integer(control + 0x708) != 0x30 || Integer(control + 0x704) != length)
            throw new InvalidDataException("Layer emission shader binary contract is unsupported.");
        var instructionsEnd = checked(Integer(control + 0x6f8) + 0x30);
        var constants = Integer(control + 0x700);
        var constantLength = Integer(control + 0x6fc);
        if (instructionsEnd <= 0x80 || instructionsEnd > constants || constants + constantLength > length
            || Integer(control + 0x70c) > 56 || instructionsEnd % 32 != 0)
            throw new InvalidDataException("Layer emission shader allocation is unsupported.");
        var relocation = Integer(24);
        if (!template.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || Integer(relocation + 8) != 6)
            throw new InvalidDataException("Layer emission shader relocation is unsupported.");
        var sections = Enumerable.Range(0, 6).Select(index => relocation + 16 + index * 24)
            .Where(section => Integer(section + 8) == data).ToArray();
        if (sections.Length != 1) throw new InvalidDataException("Layer emission shader data section is ambiguous.");
        var capacity = Integer(sections[0] + 12);
        if (length > capacity || data + capacity > template.Length || template.AsSpan(data + length, capacity - length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Layer emission shader extension storage is unavailable.");
        var code = new List<(ulong Instruction, ulong Control)>();
        var colorCaptured = false; var emissionCaptured = false; var components = new HashSet<int>();
        void Emit(ulong word, bool asynchronous = false) => code.Add((word, 15UL | 7UL << 5 | (asynchronous ? 5UL : 7UL) << 8 | 63UL << 11));
        void Move(int destination, int source) => Emit(0x5c98078000070000UL | (ulong)source << 20 | (uint)destination);
        void Constant(int destination, float value) => Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(value) << 20 | (uint)destination);
        void Uniform(int destination, int buffer, int word) => Emit(0x4c98078000070000UL | (ulong)buffer << 34 | (ulong)word << 20 | (uint)destination);
        void Multiply(int destination, int left, int right) => Emit(0x5c68100000070000UL | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Add(int destination, int left, int right, bool subtract = false) => Emit(0x5c58100000070000UL | (subtract ? 1UL << 45 : 0) | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Fma(int destination, int left, int right, int addend, bool negative = false, bool negativeAddend = false) =>
            Emit(0x59a0000000070000UL | (negative ? 1UL << 48 : 0) | (negativeAddend ? 1UL << 49 : 0) | (ulong)addend << 39 | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        for (var block = 0x80; block < instructionsEnd; block += 32)
        {
            var scheduling = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var instruction = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                var prefix = instruction >> 48; var destination = (int)(instruction & 255);
                var buffer = (int)(instruction >> 34 & 31); var word = (int)(instruction >> 20 & 16383);
                var uniform = prefix is 0x4c98 or 0x4c68 || prefix is >= 0x49a0 and <= 0x49a3;
                // These unused layer controls carry four independent source emission channels.
                // Keep their original neutral values in the inherited diffuse calculation.
                if (uniform && (buffer == 5 && word is >= 18 and <= 21 || buffer == 6 && word is >= 16 and <= 30 && word % 4 != 3))
                {
                    var neutral = buffer == 5 ? (word % 2 == 0 ? 1f : 0f) : (word / 4 % 2 == 0 ? 1f : 0f);
                    Constant(79, neutral);
                    if (prefix == 0x4c98) Move(destination, 79);
                    else if (prefix == 0x4c68) Multiply(destination, (int)(instruction >> 8 & 255), 79);
                    else Fma(destination, (int)(instruction >> 8 & 255), 79, (int)(instruction >> 39 & 255), (instruction >> 48 & 1) != 0, (instruction >> 49 & 1) != 0);
                    continue;
                }
                if (uniform && buffer == 6 && word is >= 60 and <= 62 && prefix is >= 0x49a0 and <= 0x49a3)
                {
                    var channel = word - 60; var value = (int)(instruction >> 8 & 255);
                    if (!emissionCaptured || (int)(instruction >> 39 & 255) != value || !components.Add(channel))
                        throw new InvalidDataException("Layer emission output binding is ambiguous.");
                    Add(value, value, 68 + channel);
                }
                code.Add((instruction, scheduling >> (lane * 21) & 0x1ffff));
                if (instruction >> 56 != 0xd8) continue;
                var slot = (int)(instruction >> 36 & 8191); var second = (int)(instruction >> 28 & 255);
                if (slot == 8)
                {
                    if (colorCaptured || second == 255) throw new InvalidDataException("Layer emission color input is ambiguous.");
                    Move(56, destination); Move(57, destination + 1); Move(58, second); colorCaptured = true;
                }
                if (slot != 10) continue;
                if (!colorCaptured || emissionCaptured) throw new InvalidDataException("Layer emission mask input is ambiguous.");
                // Read all four source mask channels using the native color coordinates.
                Emit(0xd830000000070000UL | 10UL << 36 | 62UL << 28 | (instruction & (255UL << 20 | 255UL << 8)) | 60, true);
                Constant(68, 0); Constant(69, 0); Constant(70, 0); Constant(71, 1); Constant(74, 1);
                for (var layer = 3; layer >= 0; layer--)
                {
                    Multiply(72, 60 + layer, 71); Uniform(73, 5, 18 + layer); Multiply(72, 72, 73);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        Uniform(73, 6, (4 + layer) * 4 + channel); Add(73, 73, 56 + channel);
                        Fma(68 + channel, 73, 72, 68 + channel);
                    }
                    Add(73, 74, 60 + layer, true); Multiply(71, 71, 73);
                }
                emissionCaptured = true;
            }
        }
        if (!colorCaptured || !emissionCaptured || !components.SetEquals(new[] { 0, 1, 2 }))
            throw new InvalidDataException("Layer emission shader contract is incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = checked(0x80 + code.Count / 3 * 32);
        var newConstants = (newEnd + 255) & ~255;
        var newLength = (newConstants + constantLength + 255) & ~255;
        if (newLength > capacity) throw new InvalidDataException("Layer emission shader extension exceeds its native allocation.");
        var output = template.ToArray();
        output.AsSpan(data + 0x80, capacity - 0x80).Clear();
        template.AsSpan(data + constants, constantLength).CopyTo(output.AsSpan(data + newConstants));
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, 4), value);
        Write(stage + 24, newLength); Write(control + 0x6f8, newEnd - 0x30);
        Write(control + 0x700, newConstants); Write(control + 0x704, newLength); Write(control + 0x70c, 80);
        for (var index = 0; index < code.Count; index += 3)
        {
            var offset = data + 0x80 + index / 3 * 32;
            var scheduling = code[index].Control | code[index + 1].Control << 21 | code[index + 2].Control << 42;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(offset, 8), scheduling);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(offset + 8 + lane * 8, 8), code[index + lane].Instruction);
        }
        return output;
    }

}
