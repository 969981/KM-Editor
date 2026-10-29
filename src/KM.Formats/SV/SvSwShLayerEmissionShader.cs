// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SV;

public static class SvSwShLayerEmissionShader
{
    public static byte[] Convert(byte[] template)
        => Convert(template, 0);

    public static byte[] SurfaceFlow(byte[] template)
        => Convert(template, 1);

    public static byte[] SurfaceLayers(byte[] template)
        => Convert(template, 2);

    public static byte[] ColorMap(byte[] template)
        => Convert(template, 3);

    public static byte[] Fresnel(byte[] template)
        => Convert(template, 4);

    public static byte[] Lava(byte[] template)
        => Convert(template, 5);

    private static byte[] Convert(byte[] template, int mode)
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
        var transparent = identity is "CB0AB209AA69541813F7395E6D3355608E047E5D1D4C66617D66E7ACF8055276" or "A01BA67B64010C74E9A8D1C7FB3CEDC5EFAD07F18049F57D1595B26B45A4B699";
        if (identity is not ("1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37" or "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2") && (!transparent || mode != 0))
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
        var flowInputs = 0;
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
                if (mode == 5 && (instruction & 0xffffffffff00ff00UL) == 0xe043ff910100ff00UL && destination == 1)
                {
                    code.Add((instruction, scheduling >> (lane * 21) & 0x1ffff));
                    Move(96, 1); Move(97, 0); continue;
                }
                if (mode is 1 or 2)
                {
                    // This otherwise unused control carries source distortion strength.
                    // Keep its neutral value in the inherited color equation.
                    if (uniform && buffer == 5 && word == 17)
                    {
                        Constant(95, 0);
                        if (prefix == 0x4c98) Move(destination, 95);
                        else if (prefix == 0x4c68) Multiply(destination, (int)(instruction >> 8 & 255), 95);
                        else Fma(destination, (int)(instruction >> 8 & 255), 95, (int)(instruction >> 39 & 255), (instruction >> 48 & 1) != 0, (instruction >> 49 & 1) != 0);
                        continue;
                    }
                    if (mode == 2 && uniform && (buffer == 5 && word is >= 18 and <= 21 || buffer == 6 && word is >= 16 and <= 30 && word % 4 != 3))
                    {
                        var neutral = buffer == 5 ? (word % 2 == 0 ? 1f : 0f) : (word / 4 % 2 == 0 ? 1f : 0f);
                        Constant(95, neutral);
                        if (prefix == 0x4c98) Move(destination, 95);
                        else if (prefix == 0x4c68) Multiply(destination, (int)(instruction >> 8 & 255), 95);
                        else Fma(destination, (int)(instruction >> 8 & 255), 95, (int)(instruction >> 39 & 255), (instruction >> 48 & 1) != 0, (instruction >> 49 & 1) != 0);
                        continue;
                    }
                    if (instruction >> 56 == 0xd8 && (instruction >> 36 & 8191) == 8)
                    {
                        var coordinate = (int)(instruction >> 8 & 255);
                        var vertical = (int)(instruction >> 20 & 255);
                        if (++flowInputs != 1 || coordinate >= 55 || vertical >= 55)
                            throw new InvalidDataException("Surface flow color coordinates are unsupported.");
                        Move(80, coordinate); Move(81, vertical);
                        if (mode == 2)
                        {
                            // The independent source mask uses UV1, passed by the paired vertex program.
                            Emit(0xE043FF918157FF50UL); Emit(0xE043FF91C157FF51UL);
                            var colorTail = (int)(instruction >> 28 & 255);
                            if (colorTail >= 55) throw new InvalidDataException("Surface layer color output is unsupported.");
                            code.Add((instruction, scheduling >> (lane * 21) & 0x1ffff));
                            Move(56, destination); Move(57, destination + 1); Move(58, colorTail);
                            Uniform(88, 5, 7); Uniform(89, 5, 9); Fma(82, 80, 88, 89);
                            Uniform(88, 5, 8); Uniform(89, 5, 11); Fma(83, 81, 88, 89);
                            // This source mask is tiled across UV1 and its loop translates
                            // by one full tile. Reduce the animated tile before sampling.
                            Emit(0x5CA8148000070A00UL | 82UL << 20 | 84);
                            Emit(0x5CA8148000070A00UL | 83UL << 20 | 85);
                            Add(82, 82, 84, true); Add(83, 83, 85, true);
                            Constant(88, 1); Add(83, 88, 83, true);
                            // Tile boundaries make implicit derivatives select averaged mask
                            // mips. Sample level zero to keep the palette continuous.
                            Emit(0xd850000000070000UL | 12UL << 36 | 62UL << 28 | 83UL << 20 | 82UL << 8 | 60, true);
                            Constant(68, 0); Constant(69, 0); Constant(70, 0); Constant(71, 1); Constant(74, 1);
                            for (var layer = 3; layer >= 0; layer--)
                            {
                                Multiply(72, 60 + layer, 71);
                                for (var channel = 0; channel < 3; channel++)
                                {
                                    Uniform(73, 6, (4 + layer) * 4 + channel);
                                    Fma(68 + channel, 73, 72, 68 + channel);
                                }
                                Add(73, 74, 60 + layer, true); Multiply(71, 71, 73);
                            }
                            // Trinity tints the base image in every layer; it does
                            // not replace that image with the palette uniform.
                            Add(68, 68, 71); Add(69, 69, 71); Add(70, 70, 71);
                            Multiply(destination, 56, 68); Multiply(destination + 1, 57, 69); Multiply(colorTail, 58, 70);
                            continue;
                        }
                        Uniform(88, 5, 7); Uniform(89, 5, 9); Fma(82, 80, 88, 89);
                        Uniform(88, 5, 8); Uniform(89, 5, 11); Fma(83, 81, 88, 89);
                        // The unused native layer texture supplies independently scrolling noise.
                        Emit(0xd830000000070000UL | 12UL << 36 | 86UL << 28 | 83UL << 20 | 82UL << 8 | 84, true);
                        Uniform(88, 5, 17); Fma(80, 84, 88, 80); Fma(81, 84, 88, 81);
                        instruction = (instruction & ~(255UL << 8 | 255UL << 20)) | 80UL << 8 | 81UL << 20;
                    }
                    code.Add((instruction, scheduling >> (lane * 21) & 0x1ffff));
                    continue;
                }
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
                var emissionWord = transparent ? 64 : 60;
                if (uniform && buffer == 6 && word >= emissionWord && word <= emissionWord + 2 && prefix is >= 0x49a0 and <= 0x49a3)
                {
                    var channel = word - emissionWord; var value = (int)(instruction >> 8 & 255);
                    if (!emissionCaptured || (int)(instruction >> 39 & 255) != value || !components.Add(channel))
                        throw new InvalidDataException("Layer emission output binding is ambiguous.");
                    Add(value, value, 68 + channel);
                }
                if (mode is 4 or 5 && instruction >> 56 == 0xd8 && (instruction >> 36 & 8191) == 8)
                { Move(80, (int)(instruction >> 8 & 255)); Move(81, (int)(instruction >> 20 & 255)); }
                if (mode == 5 && instruction >> 56 == 0xd8 && (instruction >> 36 & 8191) == 8)
                {
                    Uniform(88, 5, 7); Uniform(89, 5, 9); Fma(82, 96, 88, 89);
                    Uniform(88, 5, 8); Uniform(89, 5, 11); Fma(83, 97, 88, 89);
                    Constant(88, 1); Add(83, 88, 83, true);
                    Emit(0xd830000000070000UL | 12UL << 36 | 86UL << 28 | 83UL << 20 | 82UL << 8 | 84, true);
                    Move(90, 84);
                    Constant(88, .5f); Add(84, 84, 88, true); Uniform(88, 5, 44);
                    Fma(80, 84, 88, 80); Fma(81, 84, 88, 81);
                    instruction = (instruction & ~(255UL << 8 | 255UL << 20)) | 80UL << 8 | 81UL << 20;
                }
                code.Add((instruction, scheduling >> (lane * 21) & 0x1ffff));
                if (instruction >> 56 != 0xd8) continue;
                var slot = (int)(instruction >> 36 & 8191); var second = (int)(instruction >> 28 & 255);
                if (slot == 8)
                {
                    if (colorCaptured || second == 255) throw new InvalidDataException("Layer emission color input is ambiguous.");
                    Move(56, destination); Move(57, destination + 1); Move(58, second); colorCaptured = true;
                    if (mode == 5)
                    {
                        Uniform(88, 5, 43); Uniform(89, 5, 45); Constant(91, 1); Add(91, 91, 89, true);
                        for (var c = 0; c < 3; c++)
                        {
                            Multiply(56 + c, 56 + c, 88);
                            Uniform(92, 6, 48 + c); Constant(93, 1); Add(92, 92, 93, true); Fma(92, 92, 90, 93);
                            Multiply(56 + c, 56 + c, 92);
                            Multiply(c == 2 ? second : destination + c, 56 + c, 91);
                        }
                    }
                    if (mode == 4)
                    {
                        // Keep the two authored normal fields and the refracted secondary
                        // color independent of the diffuse and object-normal bake.
                        Emit(0xd830000000070000UL | 12UL << 36 | 86UL << 28 | 81UL << 20 | 80UL << 8 | 84, true);
                        Constant(88, 2); Constant(89, -1);
                        Fma(84, 84, 88, 89); Fma(85, 85, 88, 89); Constant(88, -.35f);
                        Fma(82, 84, 88, 80); Fma(83, 85, 88, 81, negative: true);
                        Emit(0xd830000000070000UL | 22UL << 36 | 86UL << 28 | 83UL << 20 | 82UL << 8 | 84, true);
                        // The second normal displaces the reflected detail within the gem.
                        Constant(88, 2); Fma(84, 84, 88, 89); Fma(85, 85, 88, 89); Constant(88, -.35f);
                        Fma(82, 84, 88, 82); Fma(83, 85, 88, 83, negative: true);
                        Emit(0xd830000000070000UL | 10UL << 36 | 62UL << 28 | 83UL << 20 | 82UL << 8 | 60, true);
                        for (var c = 0; c < 3; c++) { Uniform(88, 6, 20 + c); Multiply(60 + c, 60 + c, 88); }
                        // View-dependent Fresnel opacity uses the native world position
                        // and geometric normal, with the source angle bias and endpoints.
                        for (var c = 0; c < 3; c++)
                        {
                            Emit(0xe043ff8e0157ff00UL | (ulong)c << 30 | (uint)(90 + c));
                            Uniform(94 + c, 3, 76 + c); Add(90 + c, 94 + c, 90 + c, true);
                            Emit(0xe043ff8c0157ff00UL | (ulong)c << 30 | (uint)(98 + c));
                        }
                        Multiply(93, 90, 90); Fma(93, 91, 91, 93); Fma(93, 92, 92, 93);
                        Emit(0x5080000000570000UL | 93UL << 8 | 93);
                        Multiply(97, 98, 98); Fma(97, 99, 99, 97); Fma(97, 100, 100, 97);
                        Emit(0x5080000000570000UL | 97UL << 8 | 97);
                        Multiply(94, 90, 98); Fma(94, 91, 99, 94); Fma(94, 92, 100, 94);
                        Multiply(94, 94, 93); Multiply(94, 94, 97);
                        Constant(88, 0); Emit(0x5c5c100000070000UL | 88UL << 20 | 94UL << 8 | 94);
                        Constant(88, -.999999f); Constant(89, 1); Fma(94, 94, 88, 89);
                        Emit(0x5080000000570000UL | 94UL << 8 | 95); Multiply(94, 94, 95);
                        Constant(88, -.8f); Constant(89, .8f); Fma(94, 94, 88, 89);
                        Uniform(96, 5, 18); Uniform(97, 5, 19); Multiply(97, 97, 94);
                        for (var c = 0; c < 3; c++)
                        {
                            Uniform(88, 6, 16 + c); Multiply(68 + c, 56 + c, 88); Multiply(68 + c, 68 + c, 96);
                            Fma(68 + c, 60 + c, 97, 68 + c);
                            Add(88, 60 + c, 56 + c, true); Fma(c == 2 ? second : destination + c, 88, 94, 56 + c);
                        }
                        emissionCaptured = true;
                    }
                }
                if (mode == 4) continue;
                if (slot != 10) continue;
                if (!colorCaptured || emissionCaptured) throw new InvalidDataException("Layer emission mask input is ambiguous.");
                // Read all four source mask channels using the native color coordinates.
                Emit(0xd830000000070000UL | 10UL << 36 | 62UL << 28 | (mode == 5 ? 81UL << 20 | 80UL << 8 : instruction & (255UL << 20 | 255UL << 8)) | 60, true);
                if (mode == 3)
                {
                    Uniform(73, 5, 18);
                    for (var channel = 0; channel < 3; channel++) Multiply(68 + channel, 60 + channel, 73);
                    emissionCaptured = true;
                    continue;
                }
                Constant(68, 0); Constant(69, 0); Constant(70, 0); Constant(71, 1); Constant(74, 1);
                for (var layer = 3; layer >= 0; layer--)
                {
                    Multiply(72, 60 + layer, 71); Uniform(73, 5, 18 + layer); Multiply(72, 72, 73);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        Uniform(73, 6, (4 + layer) * 4 + channel);
                        if (mode != 5) Add(73, 73, 56 + channel);
                        Fma(68 + channel, 73, 72, 68 + channel);
                    }
                    Add(73, 74, 60 + layer, true); Multiply(71, 71, 73);
                }
                if (mode == 5)
                {
                    Uniform(73, 5, 42); Multiply(72, 90, 73); Uniform(74, 5, 45);
                    for (var c = 0; c < 3; c++)
                    { Uniform(73, 6, 72 + c); Fma(68 + c, 73, 72, 68 + c); Fma(68 + c, 56 + c, 74, 68 + c); }
                }
                emissionCaptured = true;
            }
        }
        if (mode is 1 or 2 ? flowInputs != 1 : !colorCaptured || !emissionCaptured || !components.SetEquals(new[] { 0, 1, 2 }))
            throw new InvalidDataException("Layer emission shader contract is incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = checked(0x80 + code.Count / 3 * 32);
        var newConstants = (newEnd + 255) & ~255;
        var newLength = (newConstants + constantLength + 255) & ~255;
        if (newLength > capacity) throw new InvalidDataException("Layer emission shader extension exceeds its native allocation.");
        var output = template.ToArray();
        if (mode == 2) output[data + 0x30 + 24 + 9] |= 0xa0;
        output.AsSpan(data + 0x80, capacity - 0x80).Clear();
        template.AsSpan(data + constants, constantLength).CopyTo(output.AsSpan(data + newConstants));
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, 4), value);
        Write(stage + 24, newLength); Write(control + 0x6f8, newEnd - 0x30);
        Write(control + 0x700, newConstants); Write(control + 0x704, newLength); Write(control + 0x70c, mode is 4 or 5 ? 112 : mode is 1 or 2 ? 96 : 80);
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
