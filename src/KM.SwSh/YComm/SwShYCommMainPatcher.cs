// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.YComm;

internal static class SwShYCommMainPatcher
{
    internal const string Owner = "Disable Y Comm";
    internal const string RelativePath = "exefs/main";
    private const uint Original = 0x37280508;
    private const uint Disabled = 0xD503201F;
    internal static int Offset(ProjectGame game) => game == ProjectGame.Sword ? 0xDC3A64 : 0xDC3A94;

    internal static (NsoFile Main, bool Disabled) Read(byte[] bytes, ProjectGame? game)
    {
        var main = NsoFile.Parse(bytes);
        var build = game == ProjectGame.Sword ? "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000"
            : game == ProjectGame.Shield ? "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000" : "";
        if (build.Length == 0 || Convert.ToHexString(main.BuildId) != build || main.Text.Header.MemoryOffset != 0
            || main.Ro.Header.MemoryOffset != 0x1901000 || main.Data.Header.MemoryOffset != 0x24DB000)
            throw new InvalidDataException("Y Comm requires the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        var start = game == ProjectGame.Sword ? 0xDC3510 : 0xDC3540;
        var text = main.Text.DecompressedData;
        if (text.Length < start + 0x638) throw new InvalidDataException("The field input handler is truncated.");
        var gate = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(Offset(game!.Value)));
        if (gate is not (Original or Disabled)) throw new InvalidDataException("The Y Comm input gate contains conflicting changes.");
        var handler = text.AsSpan(start, 0x638).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(handler.AsSpan(Offset(game.Value) - start), Original);
        var expected = game == ProjectGame.Sword ? "D9103D10592917E83A5CF3225E7D7CDA57C327100E63DBF1E2B3E046796BF317"
            : "1AF810CF274112C68C76CDBA28E9E1CE3EC2751EF0356E9F2E99C220B7AC0545";
        if (Convert.ToHexString(SHA256.HashData(handler)) != expected)
            throw new InvalidDataException("The Y Comm entry handler contains incompatible changes.");
        return (main, gate == Disabled);
    }

    internal static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, bool disabled)
    {
        var baseline = Read(vanilla, game);
        var current = Read(source, game);
        if (baseline.Disabled) throw new InvalidDataException("Y Comm requires the original Base input gate.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline.Main, current.Main, Owner);
        if (current.Disabled == disabled) return source.ToArray();
        var text = current.Main.Text.DecompressedData.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(Offset(game!.Value)), disabled ? Disabled : Original);
        var output = current.Main.Write(textDecompressedData: text);
        if (Read(output, game).Disabled != disabled) throw new InvalidDataException("Y Comm output verification failed.");
        return output;
    }

    internal static bool HasInstalledHook(byte[] bytes)
    {
        try { return Read(bytes, SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Inspect(bytes).BuildId))).Disabled; }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    internal static IEnumerable<SwShExeFsReservedRegion> Reservations()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
            yield return new(Owner, $"y-comm-{(game == ProjectGame.Sword ? "sword" : "shield")}-input", RelativePath,
                "main.text", Offset(game), 4, "Y Comm field input gate", "do-not-overwrite");
    }
}
