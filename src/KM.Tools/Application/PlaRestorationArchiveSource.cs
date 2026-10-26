// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.PLA;

namespace KM.Tools.Application;

/// <summary>Reads declared PLA model members from bounded, unchanged source archives.</summary>
internal sealed class PlaRestorationArchiveSource(string root, string? support) : IDisposable
{
    private readonly PlaCompressionRuntime runtime = new(support);
    private readonly Dictionary<string, (PlaFieldArchive Archive, long Length, long Ticks)> archives = new(StringComparer.Ordinal);
    private long retained;
    private long decoded;
    private readonly HashSet<string> members = new(StringComparer.Ordinal);

    internal void Verify()
    {
        foreach (var (path, entry) in archives)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != entry.Length || file.LastWriteTimeUtc.Ticks != entry.Ticks)
                throw new InvalidDataException("The PLA donor archive changed during preparation.");
        }
    }

    internal byte[] Read(string path, int maximum)
    {
        _ = PlaFieldArchive.HashPath(path);
        var parts = path.Split('/');
        if (parts.Length < 5 || parts[0] != "bin" || parts[1] != "pokemon"
            || parts[2].Length != 6 || !parts[2].StartsWith("pm", StringComparison.Ordinal)
            || parts[3].Length != 12 || !parts[3].StartsWith(parts[2] + "_", StringComparison.Ordinal)
            || parts[3][9] != '_' || !parts[2][2..].All(char.IsAsciiDigit)
            || !parts[3][7..9].All(char.IsAsciiDigit) || !parts[3][10..12].All(char.IsAsciiDigit))
            throw new InvalidDataException("The PLA restoration model path is unsupported.");
        var physical = Path.Combine(root, "bin", "archive", "pokemon", parts[3] + ".gfpak");
        Verify();
        if (!archives.TryGetValue(physical, out var entry))
        {
            using var stream = new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (archives.Count >= 8 || stream.Length is < 1 or > 64L * 1024 * 1024
                || retained + stream.Length > 128L * 1024 * 1024)
                throw new InvalidDataException("PLA restoration archives exceed the source budget.");
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            entry = (new PlaFieldArchive(bytes), bytes.Length, File.GetLastWriteTimeUtc(physical).Ticks);
            retained += bytes.Length; archives.Add(physical, entry);
        }
        var size = entry.Archive.DecodedSize(path);
        if (size > maximum || !members.Contains(path) && decoded + size > 256L * 1024 * 1024)
            throw new InvalidDataException("The PLA donor members exceed the decoded source budget.");
        var result = entry.Archive.Read(path, runtime);
        if (members.Add(path)) decoded += result.Length;
        if (result.Length > maximum) throw new InvalidDataException("The PLA donor member exceeds its size limit.");
        Verify();
        return result;
    }

    public void Dispose() { archives.Clear(); members.Clear(); runtime.Dispose(); }
}
