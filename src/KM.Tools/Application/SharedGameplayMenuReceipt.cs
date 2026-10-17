// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text.Json;
using KM.Api.RuntimeSettings;
using KM.Formats.Executable;
using KM.Formats.SwSh;
using KM.SwSh.RuntimeSettings;

namespace KM.Tools.Application;

internal sealed record SharedGameplayMenuFile(string Path, bool Existed, byte[]? Before, byte[] After);

internal sealed record SharedGameplayMenuReceipt(
    int Schema, InGameSettingsPackageDescriptorDto Package, byte[] Journal, SharedGameplayMenuFile[] Files,
    bool ConsoleSettings = true, SwShNativeMenuFeatures? Features = null)
{
    private const int MaximumReceiptBytes = 192 * 1024 * 1024;

    public byte[] Write()
    {
        using var output = new MemoryStream();
        using (var compression = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(compression, this);
        if (output.Length > MaximumReceiptBytes) throw new InvalidDataException("The shared output receipt is too large.");
        return output.ToArray();
    }

    public static SharedGameplayMenuReceipt Read(byte[] bytes, string titleId)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var compression = new GZipStream(input, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        var buffer = new byte[128 * 1024];
        int count;
        while ((count = compression.Read(buffer)) != 0)
        {
            if (decoded.Length + count > MaximumReceiptBytes)
                throw new InvalidDataException("The shared output receipt exceeds its decoded size limit.");
            decoded.Write(buffer, 0, count);
        }
        var receipt = JsonSerializer.Deserialize<SharedGameplayMenuReceipt>(decoded.ToArray());
        if (receipt is null || receipt.Schema != 1 || receipt.Package?.TitleId != titleId
            || receipt.Features is { } features && (uint)features is < 1 or > 7
            || receipt.Journal is null || receipt.Files is null || receipt.Files.Length != 23
            || receipt.Files.Any(file => file is null || !IsComponentPath(file.Path) || file.After is null
                || file.After.Length > 128 * 1024 * 1024 || file.Before?.Length > 128 * 1024 * 1024)
            || receipt.Files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != 23)
            throw new InvalidDataException("The shared output receipt is invalid for this game.");
        return receipt;
    }

    internal static bool IsComponentPath(string? path)
    {
        if (path is null) return false;
        if (path is "exefs/main" or "exefs/main.npdm" or "exefs/subsdk9") return true;
        var parts = path.Split('/');
        return parts.Length == 6 && parts[0] == "romfs" && parts[1] == "bin" && parts[2] == "message"
            && Languages.Contains(parts[3], StringComparer.Ordinal) && parts[4] == "common"
            && parts[5] is "option.dat" or "option.tbl";
    }

    internal static readonly string[] Languages =
        ["English", "French", "German", "Italian", "JPN", "JPN_KANJI", "Korean", "Simp_Chinese", "Spanish", "Trad_Chinese"];

    public Dictionary<string, byte[]?> RemoveFrom(IReadOnlyDictionary<string, byte[]?> current)
    {
        var restored = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var file in Files.Where(file => file.Path.StartsWith("exefs/", StringComparison.Ordinal)))
        {
            var bytes = current[file.Path];
            restored[file.Path] = bytes is null ? null : file.Before is null
                ? bytes.AsSpan().SequenceEqual(file.After) ? null : throw Conflict()
                : file.Path == "exefs/main" ? ReverseMain(file.Before, file.After, bytes)
                : ReverseBytes(file.Before, file.After, bytes);
        }
        foreach (var language in Languages)
        {
            var prefix = $"romfs/bin/message/{language}/common/option";
            var dataFile = Files.Single(file => file.Path == prefix + ".dat");
            var keysFile = Files.Single(file => file.Path == prefix + ".tbl");
            var data = current[dataFile.Path];
            var keys = current[keysFile.Path];
            if (data is null && keys is null)
            {
                restored[dataFile.Path] = null;
                restored[keysFile.Path] = null;
                continue;
            }
            // Reconstruct a deleted partner with its reviewed installed form so
            // the surviving table can still be restored by stable message key.
            data ??= dataFile.After;
            keys ??= keysFile.After;
            var beforeKeys = SwShAhtbFile.Parse(keysFile.Before ?? throw Conflict()).Entries;
            var afterKeys = SwShAhtbFile.Parse(keysFile.After).Entries;
            var afterLines = SwShGameTextFile.Parse(dataFile.After).Lines;
            var added = afterKeys.Take(afterLines.Count)
                .Select((key, index) => (key, index)).Where(pair => !beforeKeys.Contains(pair.key)).ToArray();
            foreach (var (key, index) in added.Reverse())
            {
                var observedKeys = SwShAhtbFile.Parse(keys).Entries;
                var observedLines = SwShGameTextFile.Parse(data).Lines;
                if (observedKeys.Count < observedLines.Count) throw Conflict();
                var positions = observedKeys.Select((entry, position) => (entry, position))
                    .Where(pair => pair.entry.Name == key.Name).ToArray();
                if (positions.Length == 0) continue;
                if (positions.Length != 1 || positions[0].entry != key
                    || positions[0].position >= observedLines.Count
                    || observedLines[positions[0].position] != afterLines[index]) throw Conflict();
                var result = CoordinatedGameTextAuthoring.Delete(
                    [new(language, data, keys)], [language], key.Name, GameTextNullLineEncoding.LegacyCountOne).Single();
                data = result.Data;
                keys = result.Keys;
            }
            restored[dataFile.Path] = data;
            restored[keysFile.Path] = keys;
        }
        return restored;
    }

    private static byte[] ReverseMain(byte[] before, byte[] after, byte[] current)
    {
        if (NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(after, current)
            || NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(current, after)) return before.ToArray();
        if (current.AsSpan().SequenceEqual(before)) return current;
        var original = NsoFile.Parse(before);
        var installed = NsoFile.Parse(after);
        var observed = NsoFile.Parse(current);
        if (!observed.BuildId.AsSpan().SequenceEqual(original.BuildId)
            || observed.Version != original.Version || observed.Flags != original.Flags
            || observed.Segments.Where((segment, index) => segment.Header.MemoryOffset
                != original.Segments[index].Header.MemoryOffset).Any()) throw Conflict();
        foreach (var segment in observed.Segments)
            if (!NsoFile.ComputeHash(segment.DecompressedData).AsSpan().SequenceEqual(segment.Hash)) throw Conflict();
        return observed.Write(
            ReverseBytes(original.Text.DecompressedData, installed.Text.DecompressedData, observed.Text.DecompressedData),
            ReverseBytes(original.Ro.DecompressedData, installed.Ro.DecompressedData, observed.Ro.DecompressedData),
            ReverseBytes(original.Data.DecompressedData, installed.Data.DecompressedData, observed.Data.DecompressedData));
    }

    private static byte[] ReverseBytes(byte[] before, byte[] after, byte[] current)
    {
        if (current.AsSpan().SequenceEqual(after)) return before.ToArray();
        if (current.AsSpan().SequenceEqual(before)) return current;
        if (before.Length != after.Length) throw Conflict();
        var result = current.ToArray();
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i]) continue;
            if (i >= current.Length || current[i] != before[i] && current[i] != after[i]) throw Conflict();
            result[i] = before[i];
        }
        return result;
    }

    private static InvalidDataException Conflict() => new(
        "The installed native features cannot restore a region changed by another edit. The existing files were preserved.");
}
