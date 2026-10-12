// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.SwSh;
using KM.SwSh.Scripts;

namespace KM.SwSh.ShakingTrees;

internal static class SwShTreeScript
{
    internal const string Path = "romfs/bin/script/amx/kinomi_tree.amx";
    private const string Hash = "B87B45A0B3CD50461973368CFB00E432D700D7FDB25C1A35D699DC91D6D3DD86";
    internal static readonly string[] Fields = ["initialRiskMin", "initialRiskMax", "riskStepMin", "riskStepMax", "loseAllRolls"];
    private static readonly int[] Cells = [944, 943, 1319, 1318, 1232];

    internal static int[] Read(byte[] vanilla, byte[] source)
    {
        if (Convert.ToHexString(SHA256.HashData(vanilla)) != Hash)
            throw new InvalidDataException("The base tree script is not a supported Sword/Shield script.");
        var baseline = SwShAmxDocument.Parse(vanilla);
        var current = SwShAmxDocument.Parse(source);
        if (!baseline.NativeHashes.SequenceEqual(current.NativeHashes))
            throw new InvalidDataException("Tree script native bindings are incompatible.");
        // Guard the RNG helper and the call sites we own; independent timing and dialogue edits survive.
        int[] anchors = [39, 55, 875, 876, 881, 945, 946, 948, 949, 952, 953, 1227, 1228, 1229, 1230, 1233, 1320, 1321, 1323, 1325, 1326, 1329, 1330, 1341];
        foreach (var cell in anchors.Concat(Cells).Concat(baseline.Instructions
            .Where(instruction => instruction.OriginalCell is >= 39 and <= 66).Select(instruction => instruction.OriginalCell!.Value)).Distinct())
        {
            var expected = baseline.Instructions.SingleOrDefault(instruction => instruction.OriginalCell == cell);
            if (expected is null) continue;
            var actual = current.Instructions.SingleOrDefault(instruction => instruction.OriginalCell == cell);
            if (actual is null || actual.Opcode != expected.Opcode || actual.Operands.Count != expected.Operands.Count
                || !Cells.Contains(cell) && Signature(actual) != Signature(expected))
                throw new InvalidDataException("The tree risk or item loss instructions are incompatible.");
        }
        return Cells.Select(cell => checked((int)current.Instructions.Single(i => i.OriginalCell == cell).Operands[0].LiteralValue)).ToArray();
    }

    internal static byte[] Apply(byte[] vanilla, byte[] source, SwShTreeSettings settings)
    {
        _ = Read(vanilla, source);
        int[] values = [settings.InitialRiskMin, settings.InitialRiskMax, settings.RiskStepMin, settings.RiskStepMax, settings.LoseAllRolls];
        var output = source;
        var instructions = SwShAmxDocument.Parse(source).Instructions;
        for (var i = 0; i < Cells.Length; i++)
            output = SwShAmxCellPatcher.ApplyPackedInstructionOperand(output, Cells[i],
                (int)instructions.Single(instruction => instruction.OriginalCell == Cells[i]).Opcode, values[i]);
        if (!Read(vanilla, output).SequenceEqual(values)) throw new InvalidDataException("Tree settings failed script verification.");
        return output;
    }

    private static string Signature(SwShAmxInstruction instruction) => string.Join(',', instruction.Operands.Select(operand =>
        operand.Kind == SwShAmxOperandKind.Literal ? $"v{operand.LiteralValue}" : $"t{operand.Target.OriginalCell}"));
}
