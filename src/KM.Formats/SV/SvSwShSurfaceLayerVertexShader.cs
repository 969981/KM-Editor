// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SV;

/// <summary>Passes the qualified source mask coordinates through unused native varying components.</summary>
public static class SvSwShSurfaceLayerVertexShader
{
    public static byte[] Convert(byte[] template)
    {
        int Integer(int at) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at, 4));
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(at, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8) || Integer(0x7c) != 1)
            throw new InvalidDataException("Surface layer vertex container is unsupported.");
        var stage = Pointer(Pointer(Pointer(0x80) + 16) + 8);
        var control = Pointer(stage + 8); var data = Pointer(stage + 16); var length = Integer(stage + 24);
        if (System.Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length))) != "5692C7A693293F7C076613DC7F5C4E57095EB29E9A4E7269552285681EFA1004"
            || Integer(stage + 28) < 0x848 || Integer(control) != unchecked((int)0x98761234)
            || Integer(data) != 0x12345678 || Integer(control + 0x708) != 0x30 || Integer(control + 0x704) != length
            || Integer(control + 0x70c) != 40)
            throw new InvalidDataException("Surface layer vertex program needs another adapter.");
        var end = checked(Integer(control + 0x6f8) + 0x30); var constants = Integer(control + 0x700);
        if (end % 32 != 0 || end <= 0x80 || constants > length || constants - end < 64
            || template.AsSpan(data + end, constants - end).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Surface layer vertex extension storage is unavailable.");
        var code = new List<(ulong Instruction, ulong Control)>(); var exits = 0;
        void Emit(ulong word) => code.Add((word, 15UL | 7UL << 5 | 7UL << 8 | 63UL << 11));
        for (var block = 0x80; block < end; block += 32)
        {
            var scheduling = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var instruction = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                if (instruction == 0xE30000000007000FUL)
                {
                    exits++;
                    Emit(0xEFD87F800C07FF38UL); Emit(0xEFD87F800C47FF39UL);
                    Emit(0xEFF07F801187FF38UL); Emit(0xEFF07F8011C7FF39UL);
                }
                code.Add((instruction, scheduling >> (lane * 21) & 0x1ffff));
            }
        }
        if (exits != 1) throw new InvalidDataException("Surface layer vertex exit is ambiguous.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = 0x80 + code.Count / 3 * 32;
        if (newEnd > constants) throw new InvalidDataException("Surface layer vertex extension exceeds its allocation.");
        var output = template.ToArray(); output.AsSpan(data + 0x80, constants - 0x80).Clear();
        // Shader program header input and output maps, retaining all existing components.
        output[data + 0x30 + 24 + 4 / 2] |= 3;
        output[data + 0x30 + 54 + 9 / 2] |= 0xc0;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(control + 0x6f8), newEnd - 0x30);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(control + 0x70c), 58);
        for (var index = 0; index < code.Count; index += 3)
        {
            var at = data + 0x80 + index / 3 * 32;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(at), code[index].Control | code[index + 1].Control << 21 | code[index + 2].Control << 42);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(at + 8 + lane * 8), code[index + lane].Instruction);
        }
        return output;
    }
}
