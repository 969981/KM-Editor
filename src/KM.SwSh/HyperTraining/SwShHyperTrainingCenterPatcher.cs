// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.SwSh;

namespace KM.SwSh.HyperTraining;

/// <summary>Imports the verified training procedure into the shared Pokemon Center service script.</summary>
internal static class SwShHyperTrainingCenterPatcher
{
    internal const string ScriptPath = "romfs/bin/script/amx/battle_man.amx";
    internal static readonly SwShAhtbEntry[] Keys = Enumerable.Range(0, 17).Select(index =>
    {
        var name = $"km_center_hyper_training_{index:00}";
        return new SwShAhtbEntry(SwShGfPackFile.HashFnv1a64(name), name);
    }).ToArray();

    internal sealed record BuildResult(byte[] Bytes, IReadOnlyDictionary<int, int> ImportedCells);

    internal static BuildResult Build(byte[] centerBase, byte[] hyperBase, SwShAhtbFile hyperKeys,
        int level, SwShHyperTrainingItems items)
    {
        RequireHash(centerBase, "0012C09E307E828ACB122772F5E70D57A07668EA71DC873E3BAF216F52CD83BD");
        RequireHash(hyperBase, "6CCFF1A44DFC1F116F2F8ED45C69ADEA6D9F0709F218F0185771B63257E6BE76");
        RequireHash(hyperKeys.Write(), "99D189BE4C0A9691C821C32E101D81A39EECED1925118E3E6BC87B08CCEBD35E");
        if (hyperKeys.Entries.Count != 18 || hyperKeys.Entries.Any(key => SwShGfPackFile.HashFnv1a64(key.Name) != key.Hash))
            throw new InvalidDataException("Pokemon Center Hyper Training message keys are unsupported.");
        var configured = SwShHyperTrainingItemsPatcher.ApplyScript(
            SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(hyperBase, level), items);
        var center = SwShAmxDocument.Parse(centerBase);
        var hyper = SwShAmxDocument.Parse(configured);
        var assembler = center.CreateAssembler();
        var opcodes = center.Instructions.Concat(hyper.Instructions).GroupBy(instruction => instruction.Mnemonic)
            .ToDictionary(group => group.Key, group => group.First().Opcode);
        SwShAmxInstruction Literal(string name, params long[] values) => SwShAmxInstruction.CreateLiteral(opcodes[name], values);
        SwShAmxInstruction Branch(string name, SwShAmxInstruction target) => SwShAmxInstruction.CreateBranch(opcodes[name], target);
        var messageMap = hyperKeys.Entries.Take(17).Select((key, index) =>
            (From: unchecked((long)key.Hash), To: unchecked((long)Keys[index].Hash))).ToDictionary(pair => pair.From, pair => pair.To);
        var dataDelta = assembler.AppendDataCells(hyper.DataCells.Skip(269).ToArray()) - 269 * 8;
        var dataReferences = new HashSet<int> { 2657, 2665, 2677, 2733 };
        var copies = assembler.AppendInstructions(hyper.Instructions).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var original in hyper.Instructions.Where(instruction => !instruction.IsSwitchTable))
        {
            for (var index = 0; index < original.Operands.Count; index++)
            {
                var operand = original.Operands[index];
                if (operand.Kind != SwShAmxOperandKind.Literal) continue;
                var value = messageMap.GetValueOrDefault(operand.LiteralValue, operand.LiteralValue);
                if (index == 0 && original.Mnemonic == "sysreq.n")
                    value = assembler.GetOrAddNative(hyper.NativeHashes[checked((int)operand.LiteralValue)]);
                if (index == 0 && dataReferences.Contains(original.OriginalCell!.Value)) value += dataDelta;
                if (value != operand.LiteralValue)
                    copies[original] = assembler.ReplaceLiteralOperand(copies[original], index, operand.LiteralValue, value);
            }
        }
        var originals = hyper.Instructions.ToDictionary(instruction => instruction.OriginalCell!.Value);
        SwShAmxInstruction Imported(int cell) => copies[originals[cell]];
        // Skip the Tower introduction flag and Champion-only introduction. The Center has no story gate.
        copies[originals[2140]] = assembler.ReplaceWith(Imported(2140), Branch("jump", Imported(2170)));
        var handler = new[]
        {
            Literal("push.p.c", 0), Branch("call", assembler.GetInstructionAtOriginalCell(609)),
            Literal("sysreq.n", assembler.GetOrAddNative("GetTargetFieldObjectID"), 0),
            Literal("push.pri"), Literal("push.p.c", 8), Branch("call", Imported(2139)),
            Literal("stack.p", 8), Branch("jump", assembler.GetInstructionAtOriginalCell(2343)),
        };
        assembler.InsertAfter(assembler.Instructions.Last(), handler);
        assembler.AddSwitchCase(assembler.GetInstructionAtOriginalCell(2303), 5, handler[0]);
        assembler.ReplaceWith(assembler.GetInstructionAtOriginalCell(2253),
            Literal("push.p.c", 0), Literal("push.c", unchecked((long)0x8E6E0270F9AB864EUL)),
            Literal("push.p.c", 5), Literal("push.p.c", 24),
            Branch("call", assembler.GetInstructionAtOriginalCell(615)), Literal("push.p.c", 0));
        var finalCells = new Dictionary<SwShAmxInstruction, int>();
        var cell = 0;
        foreach (var instruction in assembler.Instructions)
        {
            finalCells.Add(instruction, cell);
            cell += instruction.EncodedCellCount;
        }
        var bytes = assembler.Assemble();
        _ = SwShAmxDocument.Parse(bytes);
        return new(bytes, copies.ToDictionary(pair => pair.Key.OriginalCell!.Value, pair => finalCells[pair.Value]));
    }

    internal static void VerifyCurrent(byte[] current, byte[] centerBase, byte[] hyperBase, SwShAhtbFile hyperKeys)
    {
        if (current.AsSpan().SequenceEqual(centerBase)) return;
        var template = Build(centerBase, hyperBase, hyperKeys, 100, SwShHyperTrainingItemsPatcher.Defaults);
        var cells = SwShAmxDocument.Parse(current).Instructions.ToDictionary(instruction => instruction.OriginalCell!.Value);
        int Value(int originalCell)
        {
            if (!cells.TryGetValue(template.ImportedCells[originalCell], out var instruction)
                || instruction.Operands.Count != 1 || instruction.Operands[0].Kind != SwShAmxOperandKind.Literal)
                throw new InvalidDataException("Pokemon Center Hyper Training settings are not recognized.");
            return checked((int)instruction.Operands[0].LiteralValue);
        }
        var recognized = Build(centerBase, hyperBase, hyperKeys, Value(2294), new(Value(2185), Value(2175)));
        if (!current.AsSpan().SequenceEqual(recognized.Bytes))
            throw new InvalidDataException("The Pokemon Center service script has other changes. Restore a supported battle_man.amx before changing this option.");
    }

    private static void RequireHash(byte[] bytes, string expected)
    {
        if (Convert.ToHexString(SHA256.HashData(bytes)) != expected)
            throw new InvalidDataException("Pokemon Center Hyper Training requires the original supported Sword/Shield service scripts.");
    }
}
