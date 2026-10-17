// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;

namespace KM.SV.Moves;

internal static partial class SvTeraBlastMainPatcher
{
    private const int Behavior = 0x2D1BFC0;
    private const int Effectiveness = 0x2D1C190;
    private const uint Nop = 0xD503201F;

    internal static SvTeraBlastSettings Read(byte[] bytes, ProjectGame? game)
    {
        var nso = NsoFile.Parse(bytes);
        ValidateBuild(nso, game);
        var text = nso.Text.DecompressedData;
        if (Matches(text, Originals)) return SvTeraBlastSettings.Default;

        var categoryBranch = Word(text, 0x2D1C004);
        var category = categoryBranch == Word(BehaviorTemplate, 0x44) ? 0
            : categoryBranch == Branch(0x2D1C004, 0x2D1C058) ? 1
            : categoryBranch == Branch(0x2D1C004, 0x2D1C02C)
                ? Immediate(text, 0x2D1C048, 10) + 1
                : throw Conflict();
        var type = Word(text, 0x2D1C058) == Word(BehaviorTemplate, 0x98) ? 0
            : Word(text, 0x2D1C058) == Branch(0x2D1C058, 0x2D1C0AC) ? 1
            : throw Conflict();
        var settings = new SvTeraBlastSettings(type, category,
            Immediate(text, 0x2D1C16C, 9), Immediate(text, 0x2D1C208, 12),
            Immediate(text, 0x2D1C204, 11), Drop(text, 0x2D1C2BC, 1), Drop(text, 0x2D1C314, 3));
        settings.Validate();
        if (!Matches(text, BuildPatches(settings, customDefaults: true))) throw Conflict();
        ValidateDependencies(text);
        return settings;
    }

    internal static byte[] Apply(byte[] bytes, SvTeraBlastSettings settings, ProjectGame? game)
    {
        settings.Validate();
        _ = Read(bytes, game);
        var nso = NsoFile.Parse(bytes);
        ValidateDependencies(nso.Text.DecompressedData);
        var text = nso.Text.DecompressedData.ToArray();
        foreach (var (offset, patch) in BuildPatches(settings)) patch.CopyTo(text, offset);
        var output = nso.Write(textDecompressedData: text);
        if (Read(output, game) != settings) throw new InvalidDataException("Tera Blast output verification failed.");
        return output;
    }

    private static Dictionary<int, byte[]> BuildPatches(SvTeraBlastSettings s, bool customDefaults = false)
    {
        var result = Originals.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        if (!customDefaults && s == SvTeraBlastSettings.Default) return result;
        result[Behavior] = BehaviorTemplate.ToArray();
        result[Effectiveness] = EffectivenessTemplate.ToArray();
        void Set(int offset, uint word)
        {
            var region = result.Single(pair => offset >= pair.Key && offset + 4 <= pair.Key + pair.Value.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(region.Value.AsSpan(offset - region.Key), word);
        }
        if (s.CategoryBehavior != 0)
            Set(0x2D1C004, Branch(0x2D1C004, s.CategoryBehavior == 1 ? 0x2D1C058 : 0x2D1C02C));
        if (s.CategoryBehavior >= 2) Set(0x2D1C048, Mov(10, s.CategoryBehavior - 1));
        if (s.TypeBehavior == 1)
        {
            Set(0x2D1C058, Branch(0x2D1C058, 0x2D1C0AC));
            Set(0x2A19FE0, ConditionalBranch(0x2A19FE0, 0x2A1A038, 0));
            Set(0x2A1A020, Branch(0x2A1A020, 0x2A1A038));
            Set(0x2A1A1B8, ConditionalBranch(0x2A1A1B8, 0x2A1A1E4, 0));
            Set(0xF79AF8, Branch(0xF79AF8, 0xF79B50));
        }
        Set(0x2A19ACC, Branch(0x2A19ACC, 0x2D1C0B8));
        Set(0x2D1C0E8, Mov(0, Preview(s.NormalEffectiveness)));
        Set(0x2D1C0EC, Mov(9, Preview(s.StellarEffectiveness)));
        Set(0x2D1C204, Mov(11, s.NormalEffectiveness));
        Set(0x2D1C208, Mov(12, s.StellarEffectiveness));
        Set(0x2D1C16C, Mov(9, s.StellarPower));
        Set(0x2D1C2BC, Mov(8, ((-s.AttackDrop & 255) << 8) | 1));
        Set(0x2D1C314, Mov(8, ((-s.SpecialAttackDrop & 255) << 8) | 3));
        if (s.AttackDrop == 0) Set(0x2D1C310, Nop);
        if (s.SpecialAttackDrop == 0) Set(0x2D1C36C, Nop);
        return result;
    }

    private static int Preview(int code) => code == 0 ? 0 : code < 7 ? 3 : code == 7 ? 1 : 2;
    private static int Drop(byte[] text, int offset, int stat)
    {
        var value = Immediate(text, offset, 8);
        if ((value & 255) != stat) throw Conflict();
        return -unchecked((sbyte)(value >> 8));
    }
    private static int Immediate(byte[] text, int offset, int register)
    {
        var word = Word(text, offset);
        if ((word & 0xFFE0001F) != (0x52800000u | (uint)register)) throw Conflict();
        return (int)((word >> 5) & 0xFFFF);
    }
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static uint Mov(int register, int value) => 0x52800000u | ((uint)value << 5) | (uint)register;
    private static uint Branch(int from, int to) => 0x14000000u | ((uint)((to - from) / 4) & 0x3FFFFFF);
    private static uint ConditionalBranch(int from, int to, int condition) => 0x54000000u | (((uint)((to - from) / 4) & 0x7FFFF) << 5) | (uint)condition;
    private static bool Matches(byte[] text, IReadOnlyDictionary<int, byte[]> patches) =>
        patches.All(pair => pair.Key + pair.Value.Length <= text.Length
            && text.AsSpan(pair.Key, pair.Value.Length).SequenceEqual(pair.Value));
    private static InvalidDataException Conflict() => new("Tera Blast battle or preview instructions are not recognized. Restore a supported executable or compatible editor output.");

    private static void ValidateBuild(NsoFile nso, ProjectGame? game)
    {
        var expected = game switch
        {
            ProjectGame.Scarlet => "421C5411B487EB4D049DD065FEC9547773E8E598",
            ProjectGame.Violet => "709BFD66115298640155FCC4979DBA151C7CC79A",
            _ => throw new InvalidDataException("Tera Blast settings require Scarlet or Violet."),
        };
        if (!Convert.ToHexString(nso.BuildId).TrimEnd('0').Equals(expected, StringComparison.Ordinal)
            || nso.Text.Header.MemoryOffset != 0 || nso.Text.DecompressedData.Length < 0x343FC90)
            throw new InvalidDataException("Tera Blast settings require the selected game's supported 4.0.0 executable.");
        ValidateDependencies(nso.Text.DecompressedData);
    }

    private static void ValidateDependencies(byte[] text)
    {
        foreach (var (offset, length, fingerprint) in Dependencies)
        {
            var normalized = text.AsSpan(offset, length).ToArray();
            foreach (var (start, original) in Originals.Where(pair => pair.Key >= offset && pair.Key + pair.Value.Length <= offset + length))
                original.CopyTo(normalized, start - offset);
            if (!SHA256.HashData(normalized).AsSpan().SequenceEqual(Convert.FromHexString(fingerprint))) throw Conflict();
        }
    }
}
