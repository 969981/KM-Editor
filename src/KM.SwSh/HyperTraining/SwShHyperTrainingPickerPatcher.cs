// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;
using KM.SwSh.ExeFs;

namespace KM.SwSh.HyperTraining;

internal static class SwShHyperTrainingPickerPatcher
{
    internal const int Compare = 0xAC8638;
    private const int Exact = 0xAC9154;
    private const int Minimum = 0xAC9584;
    private const int Result = 0xAC9C74;
    private const uint VanillaLevel = 0x52800C88;

    internal static int LevelOffset(ProjectGame game) => game == ProjectGame.Shield ? 0xE06C54 : 0xE06C24;

    internal static (int Offset, int Length)[] Regions(ProjectGame game) =>
        [(LevelOffset(game), 4), (Compare, 8), (Exact, 12), (Minimum, 12), (Result, 12)];

    internal static int Read(ReadOnlySpan<byte> text, ProjectGame game)
    {
        var offset = LevelOffset(game);
        Require(text, offset - 8, 0x320003E8);
        Require(text, offset - 4, 0x39000268);
        Require(text, offset + 4, 0xB9001668);
        Require(text, offset + 8, Branch(offset + 8, offset + 0x290));
        Require(text, Compare - 12, Branch(Compare - 12, SwShHyperTrainingMainPatcher.LevelGetterOffset, true));
        Require(text, Compare - 8, 0xF942EE68);
        Require(text, Compare - 4, 0xB9401509);
        Require(text, Compare + 8, 0xB9401909);
        Require(text, 0xAC8A90, 0x2A1F03E0);
        var instruction = Word(text, offset);
        var level = instruction == VanillaLevel ? 100 : DecodeLevel(instruction);
        foreach (var (at, value) in Instructions(game, level)) Require(text, at, value);
        return level;
    }

    internal static void Write(byte[] text, ProjectGame game, int level)
    {
        _ = Read(text, game);
        foreach (var (at, value) in Instructions(game, level))
            BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(at, 4), value);
    }

    private static int DecodeLevel(uint instruction)
    {
        // The complemented minimum is distinct from the stock -1 (no filter) sentinel.
        if ((instruction & 0xFFE0001F) != 0x12800008)
            throw new InvalidDataException("Hyper Training box level filter is incompatible.");
        var level = (int)((instruction >> 5) & 0xFFFF);
        if (level is < 1 or > 99)
            throw new InvalidDataException("Hyper Training box minimum level is invalid.");
        return level;
    }

    private static IEnumerable<(int Offset, uint Value)> Instructions(ProjectGame game, int level)
    {
        var installed = level != 100;
        yield return (LevelOffset(game), installed ? 0x12800008 | ((uint)level << 5) : VanillaLevel);
        uint[] hook = installed
            ? [0x36F80009 | (uint)(((Exact - Compare) / 4) << 5), Branch(Compare + 4, Minimum)]
            : [0x6B09001F, 0x540022A1];
        uint[] exact = installed ? [0x6B09001F, Conditional(Exact + 4, 0xAC8A90, 1), Branch(Exact + 8, 0xAC8640)] : [0, 0, 0];
        uint[] minimum = installed ? [0x2A2903E9, 0x6B09001F, Branch(Minimum + 8, Result)] : [0, 0, 0];
        uint[] result = installed ? [Conditional(Result, 0xAC8A90, 3), Branch(Result + 4, 0xAC8640), 0xD503201F] : [0, 0, 0];
        foreach (var (at, words) in new[] { (Compare, hook), (Exact, exact), (Minimum, minimum), (Result, result) })
            for (var i = 0; i < words.Length; i++) yield return (at + 4 * i, words[i]);
    }

    internal static IEnumerable<SwShExeFsReservedRegion> Reservations()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            var edition = game == ProjectGame.Sword ? "sword" : "shield";
            yield return new(SwShExeFsReservedRegionLedger.OwnerHyperTraining, $"hyper-training-{edition}-box-level", "exefs/main", "main.text", LevelOffset(game), 4, "Hyper Training box minimum level", "do-not-overwrite");
        }
        foreach (var (at, length) in Regions(ProjectGame.Sword).Skip(1))
            yield return new(SwShExeFsReservedRegionLedger.OwnerHyperTraining, $"hyper-training-box-filter-{at:X}", "exefs/main", "main.text", at, length, "Hyper Training box and party level filter", "do-not-overwrite");
    }

    private static uint Branch(int at, int target, bool link = false) =>
        (link ? 0x94000000u : 0x14000000u) | ((uint)((target - at) / 4) & 0x3FFFFFF);
    private static uint Conditional(int at, int target, uint condition) =>
        0x54000000 | (((uint)((target - at) / 4) & 0x7FFFF) << 5) | condition;
    private static uint Word(ReadOnlySpan<byte> text, int at)
    {
        if (at < 0 || at > text.Length - sizeof(uint))
            throw new InvalidDataException("Hyper Training box filter is outside the main text segment.");
        return BinaryPrimitives.ReadUInt32LittleEndian(text.Slice(at, sizeof(uint)));
    }
    private static void Require(ReadOnlySpan<byte> text, int at, uint expected)
    {
        if (Word(text, at) != expected)
            throw new InvalidDataException($"Hyper Training box filter is incompatible at main.text+0x{at:X}.");
    }
}
