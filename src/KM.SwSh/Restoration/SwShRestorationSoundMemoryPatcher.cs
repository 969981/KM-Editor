// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Reserves sound memory and its upstream budgets as one executable change.</summary>
public static class SwShRestorationSoundMemoryPatcher
{
    internal const string Owner = "Restoration Sound Memory";
    internal const string MainPath = "exefs/main";
    private const int SplitOffset = 0xDA8;
    private const int ParentOffset = 0x28;
    private const int SoundOffset = 0x58;
    private const uint OriginalSplit = 0x52B85209;
    private const uint ExpandedSplit = 0x52B95209;
    private const uint OriginalParent = 0x5454CCCC;
    private const uint ExpandedParent = 0x6044CCCC;
    private const uint OriginalSound = 65 * 1024 * 1024;
    private const uint ExpandedSound = 256 * 1024 * 1024;
    internal enum Kind { Original, Legacy81, Legacy256, Installed }

    internal static (NsoFile Main, Kind State) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0 || main.Text.DecompressedData.Length < 0x14A0
            || main.Data.Header.MemoryOffset != 0x24DB000 || main.Data.DecompressedData.Length < 0x68)
            throw new InvalidDataException("Restored sounds require the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The sound memory executable has an invalid segment checksum.");
        var text = main.Text.DecompressedData; var data = main.Data.DecompressedData;
        var values = (Word(text, SplitOffset), Word(data, ParentOffset), Word(data, SoundOffset));
        var state = values switch
        {
            (OriginalSplit, OriginalParent, OriginalSound) => Kind.Original,
            (OriginalSplit, OriginalParent, 81 * 1024 * 1024) => Kind.Legacy81,
            (OriginalSplit, OriginalParent, ExpandedSound) => Kind.Legacy256,
            (ExpandedSplit, ExpandedParent, ExpandedSound) => Kind.Installed,
            _ => throw new InvalidDataException("The sound memory budgets contain incompatible changes. Restore a supported executable before adding restored sounds."),
        };
        var startup = text.AsSpan(0xD50, 0x750).ToArray();
        Put(startup, SplitOffset - 0xD50, OriginalSplit);
        var startupHash = game == ProjectGame.Sword
            ? "E21A424BF793F324358548995FA9BA2C3695A785B0D23553DD47CB47C1079FFE"
            : "5E831A3E28FEB2EA7B15F5882A53FB0FF4ADE550BCBF4C75B96633CE672D5048";
        var constants = data.AsSpan(ParentOffset, 0x40).ToArray();
        Put(constants, 0, OriginalParent); Put(constants, SoundOffset - ParentOffset, OriginalSound);
        if (Convert.ToHexString(SHA256.HashData(startup)) != startupHash
            || Convert.ToHexString(SHA256.HashData(constants)) != "50B0ACFD0F51FA4490D4C942E095EFA74C91B96625A38AA19732653659317D20")
            throw new InvalidDataException("The sound memory initialization contains incompatible changes.");
        return (main, state);
    }

    public static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, true);
    internal static byte[] RestoreFromBase(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, false);

    private static byte[] Rewrite(byte[] baseline, byte[] source, ProjectGame game, bool expanded)
    {
        var original = Read(baseline, game); var current = Read(source, game);
        if (original.State != Kind.Original) throw new InvalidDataException("Base ExeFS must contain the original sound memory budgets.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main, current.Main, Owner);
        if (current.State == (expanded ? Kind.Installed : Kind.Original)) return source;
        var text = current.Main.Text.DecompressedData.ToArray(); var data = current.Main.Data.DecompressedData.ToArray();
        Put(text, SplitOffset, expanded ? ExpandedSplit : OriginalSplit);
        Put(data, ParentOffset, expanded ? ExpandedParent : OriginalParent);
        Put(data, SoundOffset, expanded ? ExpandedSound : OriginalSound);
        var output = current.Main.Write(textDecompressedData: text, dataDecompressedData: data);
        var after = Read(output, game);
        if (after.State != (expanded ? Kind.Installed : Kind.Original)
            || !after.Main.Text.DecompressedData.SequenceEqual(text) || !after.Main.Data.DecompressedData.SequenceEqual(data)
            || !after.Main.Ro.DecompressedData.SequenceEqual(current.Main.Ro.DecompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Sound memory output failed preservation verification.");
        return output;
    }

    public static bool HasInstalledPatch(byte[] bytes)
    {
        try
        {
            var game = SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId));
            return game is not null && Read(bytes, game.Value).State == Kind.Installed;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() =>
    [
        new(Owner, "restoration-sound-global-budget", MainPath, "main.text", SplitOffset, 4, "Shared sound memory global budget", "payload-only"),
        new(Owner, "restoration-sound-parent-budget", MainPath, "main.data", ParentOffset, 4, "Shared sound memory parent budget", "payload-only"),
        new(Owner, "restoration-sound-pool-budget", MainPath, "main.data", SoundOffset, 4, "Shared sound pool budget", "payload-only"),
    ];
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
