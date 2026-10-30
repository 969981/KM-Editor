// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Formats.SwSh;
using KM.SwSh.Scripts;

namespace KM.SwSh.PokeJobs;

public static class SwShPokeJobsPatcher
{
    public const string RelativePath = "romfs/bin/script/amx/pokemoncenter.amx";
    private const string VanillaSha256 = "12972D2EF01C86790BC9F2127BEE9046DDE583CAF70C88FAA16B2A05F7B5CF70";
    private const int VisibilityCell = 7653;
    private const int TableCell = 7906;
    private const int HandlerCell = 7698;
    private const int CleanupCell = 7917;
    private const int ConditionalJump = 53;
    private const int Jump = 51;

    public static bool ReadDisabled(byte[] vanilla, byte[] source) => Inspect(vanilla, source).Disabled;

    public static byte[] Apply(byte[] vanilla, byte[] source, bool disabled)
    {
        var state = Inspect(vanilla, source);
        if (state.Disabled == disabled) return source.ToArray();
        var output = Replace(source, state.Disabled, disabled, state.CaseIndex);
        if (Inspect(vanilla, output).Disabled != disabled
            || !Replace(output, disabled, state.Disabled, state.CaseIndex).AsSpan().SequenceEqual(source))
            throw new InvalidDataException("Poke Jobs output changed data outside its owned menu settings.");
        return output;
    }

    private static byte[] Replace(byte[] source, bool before, bool after, int caseIndex)
    {
        var output = SwShAmxCellPatcher.ReplaceSignedCodeCell(source, VisibilityCell,
            before ? Jump : ConditionalJump, after ? Jump : ConditionalJump);
        // Case destinations are byte offsets relative to their case value cell.
        var valueCell = TableCell + 3 + caseIndex * 2;
        return SwShAmxCellPatcher.ReplaceSignedCodeCell(output, valueCell + 1,
            ((before ? CleanupCell : HandlerCell) - valueCell) * 8L,
            ((after ? CleanupCell : HandlerCell) - valueCell) * 8L);
    }

    private static (bool Disabled, int CaseIndex) Inspect(byte[] vanilla, byte[] source)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        ArgumentNullException.ThrowIfNull(source);
        if (Convert.ToHexString(SHA256.HashData(vanilla)) != VanillaSha256)
            throw new InvalidDataException("Poke Jobs requires the supported original Sword/Shield Pokemon Center script in Base RomFS.");
        var baseline = SwShAmxDocument.Parse(vanilla);
        var current = SwShAmxDocument.Parse(source);
        var expectedCells = baseline.Instructions.ToDictionary(instruction => instruction.OriginalCell!.Value);
        var cells = current.Instructions.ToDictionary(instruction => instruction.OriginalCell!.Value);
        // Guard the menu helpers, owned registration, selection dispatch and common return.
        // Other options, script procedures, data and appended code remain independent.
        var anchors = expectedCells.Keys.Where(cell => cell is >= 1102 and < 1142
            or >= 7648 and < 7662 or >= 7683 and < 7698 or >= CleanupCell and < 7925)
            .Where(cell => cell != VisibilityCell);
        foreach (var cell in anchors)
        {
            var expected = expectedCells[cell];
            if (!cells.TryGetValue(cell, out var actual) || Signature(expected) != Signature(actual))
                throw new InvalidDataException("The Pokemon Center terminal menu or return path contains incompatible changes.");
            if (expected.Mnemonic == "sysreq.n")
            {
                var native = checked((int)expected.Operands[0].LiteralValue);
                if (current.NativeHashes.Count <= native || current.NativeHashes[native] != baseline.NativeHashes[native])
                    throw new InvalidDataException("The Pokemon Center terminal menu native binding is incompatible.");
            }
        }
        if (!cells.TryGetValue(VisibilityCell, out var visibility)
            || visibility.Opcode is not (ConditionalJump or Jump) || visibility.Operands.Count != 1
            || visibility.Operands[0].Kind != SwShAmxOperandKind.CodeTarget
            || visibility.Operands[0].Target.OriginalCell != 7662)
            throw new InvalidDataException("The Poke Jobs visibility setting is unsupported.");
        if (!cells.TryGetValue(TableCell, out var table) || !table.IsSwitchTable || table.IsIndirectSwitchTable
            || table.DefaultDestination?.Target.OriginalCell != CleanupCell)
            throw new InvalidDataException("The Pokemon Center terminal dispatch table is unsupported.");
        var cases = table.SwitchCases.Select((entry, index) => (entry, index)).Where(item => item.entry.Value == 0).ToArray();
        if (cases.Length != 1) throw new InvalidDataException("The Poke Jobs menu choice must occur exactly once.");
        var target = cases[0].entry.Destination.Target.OriginalCell;
        var disabled = visibility.Opcode == Jump;
        if (target != (disabled ? CleanupCell : HandlerCell))
            throw new InvalidDataException("The Poke Jobs visibility and selection settings are inconsistent.");
        return (disabled, cases[0].index);
    }

    private static string Signature(SwShAmxInstruction instruction) =>
        $"{instruction.Opcode}:{string.Join(',', instruction.Operands.Select(operand =>
            operand.Kind == SwShAmxOperandKind.Literal ? $"v{operand.LiteralValue}" : $"t{operand.Target.OriginalCell}"))}";
}
