// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using I = KM.Formats.SwSh.SwShAmxInstruction;

namespace KM.SwSh.TrainerWhiteout;

internal static partial class SwShTrainerWhiteoutAmxPatcher
{
    private const int RecoveryVersion = 2;
    private static readonly HashSet<uint> RecoveryNatives = new[]
    {
        "CallTrainerBattleSeamless_", "CallTornamentTrainerBattleSeamless_", "CallKumiteBattleSeamless_"
    }.Select(name => SwShAmxNativeNameHash.Compute(name)).ToHashSet();

    private static IEnumerable<int> RecoveryCells(SwShAmxDocument document) => document.Instructions
        .Where(instruction => instruction.Mnemonic == "sysreq.n"
            && RecoveryNatives.Contains(document.NativeHashes[(int)instruction.Operands[0].LiteralValue]))
        .Select(instruction => instruction.OriginalCell!.Value);

    private static byte[] AddRecovery(SwShAmxDocument legacy, int gapCells = 0, int? legacyHelperEnd = null)
    {
        var originalCode = checked((int)legacy.DataCells[^4]);
        var originalData = checked((int)legacy.DataCells[^3]);
        var helperEnd = legacyHelperEnd ?? CodeCells(legacy);
        var recoveryStart = checked(CodeCells(legacy) + gapCells);
        var a = legacy.CreateAssembler();
        if (gapCells > 0)
            a.InsertAfter(a.Instructions.Last(), Enumerable.Range(0, gapCells).Select(_ => I.CreateLiteral(Op.Nop)).ToArray());

        bool IsHelper(I instruction) => instruction.OriginalCell >= originalCode && instruction.OriginalCell < helperEnd;
        bool Calls(I instruction, string name) => instruction.Mnemonic == "sysreq.n"
            && legacy.NativeHashes[(int)instruction.Operands[0].LiteralValue] == SwShAmxNativeNameHash.Compute(name);
        var resultCall = a.Instructions.First(instruction => IsHelper(instruction) && Calls(instruction, "GetTrainerBattleResult_"));
        var resultHelper = a.Instructions.TakeWhile(instruction => instruction != resultCall).Last(instruction => instruction.Mnemonic == "proc");

        // Keep all existing code addresses while giving forced loss a real scheduler yield.
        foreach (var suspend in a.Instructions.Where(instruction => IsHelper(instruction) && Calls(instruction, "_Suspend")).ToArray())
        {
            var push = a.GetInstructionAtOriginalCell(suspend.OriginalCell!.Value - 2);
            if (push.Opcode != Op.PushC || push.Operands[0].LiteralValue != 1 || suspend.Operands[1].LiteralValue != 8)
                throw new InvalidDataException("Trainer Whiteout recovery yield is incompatible.");
            a.ReplaceWith(push, I.CreateLiteral(Op.PushCP, 1)); // Packed push leaves one cell for the yield.
            a.InsertAfter(suspend, I.CreateLiteral(Op.HaltP, 12));
        }

        // Scene cameras can resume before the script reads the battle result.
        // Resolve an optional forced defeat at the battle return boundary instead.
        var sites = a.Instructions.Where(instruction => IsHelper(instruction) && Calls(instruction, "CallTrainerBattleCore")
            || instruction.OriginalCell < originalCode && instruction.Mnemonic == "sysreq.n"
                && RecoveryNatives.Contains(legacy.NativeHashes[(int)instruction.Operands[0].LiteralValue])).ToArray();
        foreach (var site in sites)
        {
            var count = site.Operands[1].LiteralValue;
            if (count != (Calls(site, "CallTrainerBattleCore") ? 56 : 0))
                throw new InvalidDataException("Trainer Whiteout recovery launch is incompatible.");
            var entry = I.CreateLiteral(Op.Proc);
            var done = I.CreateLiteral(Op.LoadSPri, -8);
            var code = new List<I> { entry, I.CreateLiteral(Op.Stack, -32) };
            for (var argument = (int)count / 8 - 1; argument >= 0; argument--)
                code.Add(I.CreateLiteral(Op.PushS, 16 + argument * 8));
            code.AddRange([
                I.CreateLiteral(Op.SysreqN, site.Operands[0].LiteralValue, count),
                I.CreateLiteral(Op.StorSPri, -8),
                I.CreateLiteral(Op.LoadPri, originalData * 8),
                I.CreateLiteral(Op.EqCPri, 2),
                I.CreateBranch(Op.Jzer, done),
                I.CreateLiteral(Op.LoadPri, originalData * 8 + 8),
                I.CreateLiteral(Op.ConstAlt, 4),
                I.CreateLiteral(Op.And),
                I.CreateBranch(Op.Jzer, done),
                I.CreateLiteral(Op.AddrPri, -32),
                I.CreateLiteral(Op.PushPri),
                I.CreateBranch(Op.Call, resultHelper),
                I.CreateLiteral(Op.Stack, 8)
            ]);
            // Restricted battles retain their existing cleanup before the deferred loss.
            // Their result is read immediately after the battle without a scene transition.
            code.AddRange([done, I.CreateLiteral(Op.Stack, 32), I.CreateLiteral(Op.Ret)]);
            a.InsertAfter(a.Instructions.Last(), code.ToArray());
            a.ReplaceWith(site, I.CreateBranch(Op.Call, entry), I.CreateLiteral(Op.StackP, count));
        }

        // One extra footer cell records the appended span without moving existing data or code.
        a.ReplaceDataCell((legacy.DataCells.Count - 5) * 8, FormatVersion, RecoveryVersion);
        a.ReplaceDataCell((legacy.DataCells.Count - 1) * 8, unchecked((ulong)EndMarker), (ulong)recoveryStart);
        a.AppendDataCells(EndMarker);
        return a.Assemble();
    }
}
