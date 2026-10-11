// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.Scripts;

namespace KM.SwSh.HyperTraining;

internal static class SwShHyperTrainingItemsPatcher
{
    internal const string Owner = "Hyper Training Items";
    internal static readonly int[] BottleCells = [2185, 2486, 2491, 2498, 2615];
    internal static readonly int[] GoldCells = [2175, 2455, 2460, 2467, 2606];
    internal static readonly SwShHyperTrainingItems Defaults = new(795, 796);

    internal static void Validate(SwShHyperTrainingItems items)
    {
        if (items.BottleCapItemId is < 1 or > ushort.MaxValue || items.GoldBottleCapItemId is < 1 or > ushort.MaxValue)
            throw new InvalidDataException("Hyper Training requires two valid bag item IDs.");
    }

    internal static SwShHyperTrainingItems ReadScript(byte[] data)
    {
        var reader = SwShAmxCellPatcher.OpenCodeCellReader(data);
        int ReadGroup(int[] cells)
        {
            var values = cells.Select(cell => reader.TryReadPackedInstructionOperand(cell, 188, out var item)
                ? item : throw new InvalidDataException("Hyper Training item operand is incompatible.")).Distinct().ToArray();
            if (values.Length != 1) throw new InvalidDataException("Hyper Training item checks disagree.");
            return values[0];
        }
        var items = new SwShHyperTrainingItems(ReadGroup(BottleCells), ReadGroup(GoldCells));
        Validate(items);
        // Verify the count calls and menu name arguments independently of their editable IDs.
        foreach (var cell in BottleCells.Concat(GoldCells).Except([2460, 2491]))
        {
            RequireCell(cell + 1, 135); RequireCell(cell + 2, 52); RequireCell(cell + 3, 8);
        }
        foreach (var cell in new[] { 2460, 2491 })
        {
            RequireCell(cell + 1, 188); RequireCell(cell + 2, (24L << 32) | 188);
            RequireCell(cell + 3, 49);
        }
        RequireCell(2464, -13768); RequireCell(2495, -14016);
        RequireCell(2473, 135); RequireCell(2474, 54); RequireCell(2475, 32);
        RequireCell(2504, 135); RequireCell(2505, 54); RequireCell(2506, 32);
        return items;

        void RequireCell(int cell, long expected)
        {
            var valid = expected >= 0 && ((ulong)expected >> 32 != 0 || expected == 188)
                ? reader.TryReadPackedInstructionOperand(cell, unchecked((int)expected), out var operand) && operand == (int)(expected >> 32)
                : reader.TryReadInt(cell, out var value, out var packed) && !packed && value == expected;
            if (!valid)
                throw new InvalidDataException("Hyper Training item script shape is incompatible.");
        }
    }

    internal static byte[] ApplyScript(byte[] data, SwShHyperTrainingItems items)
    {
        Validate(items); _ = ReadScript(data);
        var output = SwShAmxCellPatcher.ApplyCodeCellPatches(data,
            BottleCells.Select(cell => new SwShAmxCellPatch(cell, items.BottleCapItemId, true))
                .Concat(GoldCells.Select(cell => new SwShAmxCellPatch(cell, items.GoldBottleCapItemId, true))).ToArray());
        if (ReadScript(output) != items) throw new InvalidDataException("Hyper Training item script verification failed.");
        return output;
    }

    internal static SwShHyperTrainingItems ReadMain(byte[] bytes, ProjectGame? game)
    {
        var analysis = SwShHyperTrainingMainPatcher.Analyze(bytes, game);
        if (analysis.Kind is not (SwShHyperTrainingMainKind.NotInstalled or SwShHyperTrainingMainKind.CustomMinimumLevel))
            throw new InvalidDataException(analysis.Message);
        var main = NsoFile.Parse(bytes);
        var at = Offset(analysis.DetectedGame!.Value);
        var text = main.Text.DecompressedData;
        Require(at - 4, 0x7100011F); Require(at + 8, 0x7901B668);
        Require(at + 0x1C, 0xD65F03C0); Require(at + 0x2C, 0xD10143FF);
        var installed = Word(at + 4) != 0x1A880508;
        var items = installed ? new SwShHyperTrainingItems(MoveValue(Word(at), 8), MoveValue(Word(at + 0x20), 9)) : Defaults;
        Validate(items);
        var expected = Instructions(at, items);
        foreach (var (offset, value) in expected) Require(offset, value);
        return items;
        uint Word(int offset) => offset >= 0 && offset <= text.Length - 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset))
            : throw new InvalidDataException("Hyper Training item runtime is truncated.");
        void Require(int offset, uint value)
        {
            if (Word(offset) != value) throw new InvalidDataException("Hyper Training item runtime is incompatible or incomplete.");
        }
    }

    internal static byte[] ApplyMain(byte[] bytes, ProjectGame? game, SwShHyperTrainingItems items)
    {
        Validate(items); _ = ReadMain(bytes, game);
        var main = NsoFile.Parse(bytes);
        var selected = SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId))!.Value;
        var text = main.Text.DecompressedData.ToArray();
        foreach (var (at, word) in Instructions(Offset(selected), items)) BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(at), word);
        var output = main.Write(textDecompressedData: text);
        if (ReadMain(output, game) != items) throw new InvalidDataException("Hyper Training item runtime verification failed.");
        return output;
    }

    internal static bool HasInstalledHook(byte[] bytes)
    {
        try { return ReadMain(bytes, null) != Defaults; }
        catch (InvalidDataException) { return false; }
    }

    internal static IEnumerable<SwShExeFsReservedRegion> Reservations()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        foreach (var (start, length) in new[] { (Offset(game), 8), (Offset(game) + 0x20, 12) })
            yield return new(Owner, $"hyper-training-items-{game.ToString().ToLowerInvariant()}-{start:X}", "exefs/main", "main.text", start, length,
                "Hyper Training payment item selection", "do-not-overwrite");
    }

    private static int Offset(ProjectGame game) => game == ProjectGame.Sword ? 0x00F7ECA4 : 0x00F7ECD4;
    private static uint Move(int value, int register) => 0x52800000U | ((uint)value << 5) | (uint)register;
    private static int MoveValue(uint word, int register) => (word & 0xFFE0001F) == (0x52800000U | (uint)register)
        ? (int)((word >> 5) & 0xFFFF) : throw new InvalidDataException("Hyper Training item immediate is invalid.");
    private static (int, uint)[] Instructions(int at, SwShHyperTrainingItems items) => items == Defaults
        ? [(at, 0x52806368), (at + 4, 0x1A880508), (at + 0x20, 0), (at + 0x24, 0), (at + 0x28, 0)]
        : [(at, Move(items.BottleCapItemId, 8)), (at + 4, 0x14000007),
            (at + 0x20, Move(items.GoldBottleCapItemId, 9)), (at + 0x24, 0x1A890108), (at + 0x28, 0x17FFFFF8)];
}
