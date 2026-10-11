// SPDX-License-Identifier: GPL-3.0-only
namespace KM.SwSh.IvScreen;

internal static partial class SwShIvScreenMainPatcher
{
    private const int TrainingRefreshCall = 0x139EF4C;
    private const int GraphScaleInstruction = 0x138AA10;
    private static readonly int[] TrainingSlots = [0x138F324, 0x138F704, 0x138F764, 0x138F984];

    private static IEnumerable<(int Offset, uint[] Instructions)> TrainingPanelInstructions(PatchLayout layout)
    {
        var slots = TrainingSlots.Select(layout.ShiftOffset).ToArray();
        var call = layout.ShiftOffset(TrainingRefreshCall);
        var scale = layout.ShiftOffset(GraphScaleInstruction);
        yield return (call, [EncodeBranchLink(call, slots[0])]);
        yield return (scale, [EncodeBranchLink(scale, slots[1])]);
        yield return (slots[0], [0x3919241F, EncodeBranch(slots[0] + 4, layout.SummaryMultiChartRefreshOffset), NopInstruction]);
        yield return (slots[1], [XModePaneVisibilityInstruction, EncodeCompareBranchNonZero32(slots[1] + 4, slots[2], 8), EncodeBranch(slots[1] + 8, slots[3])]);
        yield return (slots[2], [0x52803208, 0xD65F03C0, NopInstruction]);
        yield return (slots[3], [0x528003E8, 0xD65F03C0, NopInstruction]);
    }

    // Earlier exact installations have neither added call. Partial updates are conflicts.
    private static int TrainingPanelState(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        var call = layout.ShiftOffset(TrainingRefreshCall);
        var scale = layout.ShiftOffset(GraphScaleInstruction);
        if (ReadInstruction(text, call) == EncodeBranchLink(call, layout.SummaryMultiChartRefreshOffset)
            && ReadInstruction(text, scale) == 0x52803208) return 0;
        foreach (var slot in TrainingPanelInstructions(layout))
            if (!InstructionsEqual(text, slot.Offset, slot.Instructions)) return -1;
        return 1;
    }

    private static void WriteTrainingPanel(byte[] text, PatchLayout layout)
    {
        foreach (var slot in TrainingPanelInstructions(layout)) WriteInstructions(text, slot.Offset, slot.Instructions);
    }

    private static bool TrainingRendererCallsAreSupported(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        foreach (var site in layout.RendererWrapperCallSites)
        {
            if (site.Offset == layout.ShiftOffset(TrainingRefreshCall) && TrainingPanelState(text, layout) == 1) continue;
            if (ReadInstruction(text, site.Offset) != site.VanillaInstruction) return false;
        }
        return true;
    }
}
