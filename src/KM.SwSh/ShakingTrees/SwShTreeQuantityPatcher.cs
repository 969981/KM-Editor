// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.ShakingTrees;

internal static class SwShTreeQuantityPatcher
{
    internal const string Owner = "Shaking Trees";
    private const string RoutineHash = "B17BB380891EC2C3651A8A629AE6FE590EECC68CF3DF2D95F46C7063D6D60969";
    private static readonly byte[] Original = Convert.FromHexString("6B0500127F0D007120FFFF54");
    private static int Routine(ProjectGame game) => game == ProjectGame.Sword ? 0xD555C0 : 0xD555F0;
    private static int Offset(ProjectGame game) => Routine(game) + 0xD8;

    internal static (NsoFile Main, int Min, int Max) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0 || main.Text.DecompressedData.Length < Routine(game) + 0x3D0)
            throw new InvalidDataException("Shaking Trees requires the selected game's 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        var values = (from min in Enumerable.Range(1, 3) from max in Enumerable.Range(min, 4 - min)
            where main.Text.DecompressedData.AsSpan(Offset(game), 12).SequenceEqual(Pattern(min, max))
            select (Min: min, Max: max)).ToArray();
        if (values.Length != 1) throw new InvalidDataException("The tree quantity instructions are incompatible.");
        var normalized = main.Text.DecompressedData.AsSpan(Routine(game), 0x3D0).ToArray();
        Original.CopyTo(normalized, 0xD8);
        if (Convert.ToHexString(SHA256.HashData(normalized)) != RoutineHash)
            throw new InvalidDataException("The tree drop routine contains incompatible changes.");
        return (main, values[0].Min, values[0].Max);
    }

    internal static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame game, int min, int max)
    {
        var baseline = Read(vanilla, game);
        var current = Read(source, game);
        if (baseline.Min != 1 || baseline.Max != 3) throw new InvalidDataException("Base ExeFS must contain the original tree quantity routine.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline.Main, current.Main, Owner);
        if (SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game, Owner)
            .Any(region => SwShExeFsReservedRegionLedger.Overlaps(region, Offset(game), 12)))
            throw new InvalidDataException("The tree quantity patch overlaps another executable feature.");
        var text = current.Main.Text.DecompressedData.ToArray();
        Pattern(min, max).CopyTo(text, Offset(game));
        var output = current.Main.Write(textDecompressedData: text);
        var after = Read(output, game);
        if (after.Min != min || after.Max != max || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(current.Main.Ro.DecompressedData)
            || !after.Main.Data.DecompressedData.SequenceEqual(current.Main.Data.DecompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Tree quantity output failed preservation verification.");
        return output;
    }

    private static byte[] Pattern(int min, int max)
    {
        if (min < 1 || max > 3 || min > max) throw new InvalidDataException("Choose an item count range from 1 to 3.");
        if (min == 1 && max == 3) return Original;
        // Keep the game's RNG transition. For two outcomes use its unbiased low bit;
        // fixed counts replace only the result. No fourth temporary item can be written.
        uint first = min == max ? 0x5280000BU | (uint)(min - 1) << 5 : 0x1200016BU;
        uint second = min == max || min == 1 ? 0xD503201FU : 0x1100056BU;
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, first);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), second);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0xD503201F);
        return bytes;
    }

    internal static bool HasInstalledHook(byte[] bytes)
    {
        try
        {
            var game = SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId));
            if (game is null) return false;
            var state = Read(bytes, game.Value);
            return state.Min != 1 || state.Max != 3;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() => new[] { ProjectGame.Sword, ProjectGame.Shield }
        .Select(game => new SwShExeFsReservedRegion(Owner, $"shaking-trees-{game.ToString().ToLowerInvariant()}-quantity",
            "exefs/main", "main.text", Offset(game), 12, "Items per successful tree shake", "payload-only")).ToArray();
}
