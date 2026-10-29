// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Formats.Lua;
using KM.SV.Workflows;

namespace KM.SV.TmMachine;

internal enum SvTmMaterialVisibilityKind
{
    DiscoveryGated,
    TrackerOnly,
    AlwaysVisible,
    Unsupported,
}

internal sealed record SvTmMaterialVisibilityAnalysis(
    SvTmMaterialVisibilityKind Kind,
    string Message,
    string Sha256);

internal static class SvTmMaterialVisibilityPatcher
{
    private const int TrackerPrototype = 24349;
    private const int RecipePrototype = 28215;
    private const int TrackerGate = 168;
    private const int RecipeGate = 50;
    private const uint TrackerDiscovery = 0x000085C2;
    private const uint TrackerVisible = 0x000B0580;
    private const uint RecipeDiscovery = 0x000083C2;
    private const uint RecipeVisible = 0x00070380;

    public static SvTmMaterialVisibilityAnalysis Analyze(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        try
        {
            var chunk = ReadSupported(bytes);
            var trackerVisible = chunk.Root.Children[TrackerPrototype].Code[TrackerGate] == TrackerVisible;
            var recipeVisible = chunk.Root.Children[RecipePrototype].Code[RecipeGate] == RecipeVisible;
            var kind = recipeVisible ? SvTmMaterialVisibilityKind.AlwaysVisible
                : trackerVisible ? SvTmMaterialVisibilityKind.TrackerOnly
                : SvTmMaterialVisibilityKind.DiscoveryGated;
            var message = kind switch
            {
                SvTmMaterialVisibilityKind.AlwaysVisible => "TM material names are always shown in the recipe panel and tracking window.",
                SvTmMaterialVisibilityKind.TrackerOnly => "Only tracking-window material names are visible. Reapply Always visible to include the TM Machine recipe panel.",
                _ => "TM material names use the standard discovery checks in the recipe panel and tracking window.",
            };
            return new(kind, message, sha256);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or OverflowException or ArgumentOutOfRangeException)
        {
            return new(SvTmMaterialVisibilityKind.Unsupported,
                $"The TM material script failed structural validation: {exception.Message}", sha256);
        }
    }

    public static byte[] Apply(byte[] currentBytes, bool alwaysVisible)
    {
        ArgumentNullException.ThrowIfNull(currentBytes);
        var chunk = ReadSupported(currentBytes);
        var children = chunk.Root.Children.ToArray();
        ReplaceGate(TrackerPrototype, TrackerGate, alwaysVisible ? TrackerVisible : TrackerDiscovery);
        ReplaceGate(RecipePrototype, RecipeGate, alwaysVisible ? RecipeVisible : RecipeDiscovery);
        var output = chunk.WithRoot(chunk.Root.WithChildren(children)).Serialize();
        var expected = alwaysVisible ? SvTmMaterialVisibilityKind.AlwaysVisible : SvTmMaterialVisibilityKind.DiscoveryGated;
        if (Analyze(output).Kind != expected)
            throw new InvalidDataException("The TM material visibility transform did not survive structural reparse validation.");
        return output;

        void ReplaceGate(int prototypeIndex, int instructionIndex, uint instruction)
        {
            var prototype = children[prototypeIndex];
            var code = prototype.Code.ToArray();
            // Replace only the display discovery test; retain recipe availability and all quantity checks.
            code[instructionIndex] = instruction;
            children[prototypeIndex] = prototype.WithCodeAndConstants(code, prototype.Constants);
        }
    }

    private static Lua54BinaryChunk ReadSupported(byte[] bytes)
    {
        // Full normalized-source admission also recognizes exact shared-script compositions.
        SvSharedScript.Read(bytes);
        var chunk = Lua54BinaryChunk.Parse(bytes);
        ValidateWindow(chunk.Root.Children[TrackerPrototype], TrackerGate,
            TrackerDiscovery, TrackerVisible, 0x800011B8, 206, 412, 46, 1, 20);
        ValidateWindow(chunk.Root.Children[RecipePrototype], RecipeGate,
            RecipeDiscovery, RecipeVisible, 0x800006B8, 66, 236, 47, 0, 22);
        return chunk;
    }

    private static void ValidateWindow(Lua54Prototype prototype, int gate, uint discovery, uint visible,
        uint jump, int visibleTarget, int codeCount, int constantCount, int childCount, byte stackSize)
    {
        if (prototype.Code.Count != codeCount || prototype.Constants.Count != constantCount
            || prototype.Children.Count != childCount || prototype.MaxStackSize != stackSize
            || (prototype.Code[gate] != discovery && prototype.Code[gate] != visible)
            || prototype.Code[gate + 1] != jump
            || gate + 2 + SignedJump(jump) != visibleTarget)
            throw new InvalidDataException("The TM material display patch window does not match the supported build.");
        for (var index = 0; index < prototype.Code.Count; index++)
        {
            var instruction = prototype.Code[index];
            if ((instruction & 0x7F) == 56 && index + 1 + SignedJump(instruction) is var target
                && (target == gate || target == gate + 1))
                throw new InvalidDataException("The TM material display patch window has an unsupported incoming control-flow edge.");
        }
    }

    private static int SignedJump(uint instruction) => checked((int)(instruction >> 7) - 0xFFFFFF);
}
