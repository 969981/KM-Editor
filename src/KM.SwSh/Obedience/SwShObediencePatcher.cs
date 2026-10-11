// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Obedience;

internal static class SwShObediencePatcher
{
    internal static readonly int[] Defaults = [20, 30, 40, 50, 60, 70, 80, 90, 100];
    private const int RuntimeTable = 0x008A1FC4;
    private const int RuntimeFormula = 0x007E65E0;
    private static ReadOnlySpan<byte> Marker => "OB1"u8;

    internal static bool HasInstalledHook(byte[] bytes)
    {
        try
        {
            var game = SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId));
            return !Read(bytes, game).Selections.SequenceEqual(Defaults);
        }
        catch (InvalidDataException) { return false; }
    }

    internal static int[] Normalize(IReadOnlyList<int>? selections)
    {
        if (selections is null || selections.Count != 9 || selections.Any(value => value is < 1 or > 100))
            throw new InvalidDataException("Obedience requires nine level limits between 1 and 100.");
        return selections.ToArray();
    }

    internal static (NsoFile Main, int[] Selections) Read(byte[] bytes, ProjectGame? game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0)
            throw new InvalidDataException("Obedience requires the selected game's 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        var text = main.Text.DecompressedData;
        var layout = Layout(game.Value);
        var spans = Spans(game.Value);
        if (spans.Any(span => span.Offset > text.Length - span.Before.Length) || layout.Display + 0x24 > text.Length)
            throw new InvalidDataException("Obedience code is truncated.");
        var installed = !text.AsSpan(RuntimeTable, 12).SequenceEqual(new byte[12]);
        var selections = installed ? text.AsSpan(RuntimeTable, 9).ToArray().Select(value => (int)value).ToArray() : Defaults.ToArray();
        _ = Normalize(selections);
        foreach (var span in spans)
        {
            var expected = installed ? span.After : span.Before;
            if (span.IsTable && installed) expected = Table(selections);
            if (!text.AsSpan(span.Offset, expected.Length).SequenceEqual(expected))
                throw new InvalidDataException("Obedience found conflicting or incomplete executable changes.");
        }
        // Only the two level formulas and their tables are writable. Preserve the
        // badge count, original trainer checks, and battle disobedience behavior.
        Require(text, RuntimeFormula - 4, BranchLink(RuntimeFormula - 4, layout.Badges));
        Require(text, RuntimeFormula + 8, 0xAA1803E0);
        Require(text, RuntimeFormula + 16, 0x3929AA88);
        Require(text, RuntimeTable - 4, 0xD65F03C0);
        Require(text, RuntimeTable + 12, 0xF81D0FF5);
        Require(text, layout.Display - 4, BranchLink(layout.Display - 4, layout.Badges));
        Require(text, layout.Display + 8, 0x121F1909);
        Require(text, layout.Table - 4, 0xD65F03C0);
        return (main, selections);
    }

    internal static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, IReadOnlyList<int> selections)
    {
        var values = Normalize(selections);
        var baseline = Read(vanilla, game);
        var current = Read(source, game);
        if (!baseline.Selections.SequenceEqual(Defaults)
            || !baseline.Main.Text.DecompressedData.AsSpan(RuntimeTable, 12).SequenceEqual(new byte[12]))
            throw new InvalidDataException("Obedience requires original Base level limits.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline.Main, current.Main, "Obedience");
        var restore = values.SequenceEqual(Defaults);
        var text = current.Main.Text.DecompressedData.ToArray();
        foreach (var span in Spans(game!.Value))
            (restore ? span.Before : span.IsTable ? Table(values) : span.After).CopyTo(text, span.Offset);
        var output = current.Main.Write(textDecompressedData: text);
        if (!Read(output, game).Selections.SequenceEqual(values))
            throw new InvalidDataException("Obedience output verification failed.");
        return output;
    }

    internal static IEnumerable<SwShExeFsReservedRegion> CreateReservations()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            var name = game == ProjectGame.Sword ? "sword" : "shield";
            foreach (var span in Spans(game))
                yield return new("Obedience", $"obedience-{name}-{span.Offset:X}", "exefs/main", "main.text",
                    span.Offset, span.Before.Length, span.IsTable ? "Obedience badge level table" : "Obedience level formula", "do-not-overwrite");
        }
    }

    private static (int Display, int Table, int Badges) Layout(ProjectGame game) => game == ProjectGame.Sword
        ? (0x0153CD14, 0x0153C364, 0x01438F80) : (0x0153CDA4, 0x0153C3F4, 0x01438FB0);

    private static Span[] Spans(ProjectGame game)
    {
        var layout = Layout(game);
        return [
            new(RuntimeFormula, Words(0x52800148, 0x1B087C08), Words(Address(RuntimeFormula, RuntimeTable), 0x38606908)),
            new(RuntimeFormula + 12, Words(0x11005108), Words(0xD503201F)),
            new(layout.Display, Words(0x1B1A7C08, 0x11005108), Words(Address(layout.Display, layout.Table), 0x38606908)),
            new(RuntimeTable, new byte[12], [], true),
            new(layout.Table, new byte[12], [], true),
        ];
    }

    private static byte[] Table(IReadOnlyList<int> values) => [.. values.Select(value => checked((byte)value)), .. Marker];
    private static uint Address(int offset, int target)
    {
        var delta = target - offset;
        if (delta is < -0x100000 or >= 0x100000) throw new InvalidDataException("Obedience table is out of range.");
        return 0x10000008U | (((uint)delta & 3) << 29) | ((((uint)delta >> 2) & 0x7FFFF) << 5);
    }
    private static uint BranchLink(int offset, int target) => 0x94000000U | ((uint)((target - offset) / 4) & 0x3FFFFFF);
    private static byte[] Words(params uint[] values)
    {
        var result = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4), values[i]);
        return result;
    }
    private static void Require(byte[] text, int offset, uint value)
    {
        if (offset > text.Length - 4 || BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset)) != value)
            throw new InvalidDataException("Obedience native code is incompatible.");
    }
    private sealed record Span(int Offset, byte[] Before, byte[] After, bool IsTable = false);
}
