// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Output;

namespace KM.Core.Projects;

internal static class ProjectFileSystemStamp
{
    private const int MaximumOutputEntries = 20_000;

    // This stamp expires read caches. Apply still validates exact source and output bytes.
    public static string? Capture(ProjectPaths paths) => CaptureCore(paths, ignoreTransientLocks: false);

    public static string? CaptureFileGraph(ProjectPaths paths) => CaptureCore(paths, ignoreTransientLocks: true);

    private static string? CaptureCore(ProjectPaths paths, bool ignoreTransientLocks)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AddRoot(paths.BaseRomFsPath);
            AddRoot(paths.BaseExeFsPath);
            AddRoot(paths.ScarletVioletSupportFolderPath);
            AddRoot(paths.PokemonLegendsZASupportFolderPath);
            if (paths.SelectedGame == ProjectGame.PLA) AddRoot(paths.PokemonLegendsArceusSupportFolderPath);
            if (!string.IsNullOrWhiteSpace(paths.SaveFilePath)) Add(new FileInfo(paths.SaveFilePath));
            if (!string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            {
                var identity = Path.Combine(paths.BaseExeFsPath, "main.npdm");
                if (File.Exists(identity))
                {
                    using var stream = File.OpenRead(identity);
                    if (stream.Length > 1024 * 1024) return null;
                    hash.AppendData(SHA256.HashData(stream));
                }
            }

            // Payload entries below capture additions, removals and replacements.
            // The root timestamp also changes for transient output coordination locks.
            AddRoot(paths.OutputRootPath, includeLastWriteTime: !ignoreTransientLocks);
            if (!string.IsNullOrWhiteSpace(paths.OutputRootPath) && Directory.Exists(paths.OutputRootPath))
            {
                var pending = new Stack<string>();
                pending.Push(paths.OutputRootPath);
                var count = 0;
                while (pending.TryPop(out var directory))
                {
                    foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos()
                        .Take(MaximumOutputEntries - count + 1)
                        .OrderBy(entry => entry.Name, StringComparer.Ordinal))
                    {
                        if (++count > MaximumOutputEntries) return null;
                        if (ignoreTransientLocks && directory == paths.OutputRootPath
                            && OutputMetadataNamespace.ContainsReservedSegment(entry.Name))
                        {
                            // Legacy state or pending working data must still force a fresh
                            // read. Only the ordinary empty lock scaffolding is disposable.
                            if (!IsTransientLockDirectory(entry)) return null;
                            continue;
                        }
                        Add(entry);
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                        if (entry is DirectoryInfo) pending.Push(entry.FullName);
                    }
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset());

            void AddRoot(string? path, bool includeLastWriteTime = true)
            {
                if (string.IsNullOrWhiteSpace(path)) { Append("unset"); return; }
                if (File.Exists(path)) { Add(new FileInfo(path)); return; }
                var entry = new DirectoryInfo(path);
                Add(entry, includeLastWriteTime);
                if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A cache source is not a physical directory.");
            }
            void Add(FileSystemInfo entry, bool includeLastWriteTime = true)
            {
                // Every entry is newly constructed or freshly enumerated for this capture.
                // Enumeration already supplies metadata; Refresh would stat every file again.
                Append(entry.FullName);
                Append(entry.Exists.ToString());
                if (!entry.Exists) return;
                Append(((int)entry.Attributes).ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (includeLastWriteTime)
                    Append(entry.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (entry is FileInfo file) Append(file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            void Append(string value)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(value));
                hash.AppendData([0]);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsTransientLockDirectory(FileSystemInfo entry)
    {
        if (entry is not DirectoryInfo directory
            || !string.Equals(entry.Name, OutputMetadataNamespace.DirectoryName, StringComparison.OrdinalIgnoreCase)
            || (entry.Attributes & FileAttributes.ReparsePoint) != 0) return false;

        var count = 0;
        foreach (var child in directory.EnumerateFileSystemInfos())
        {
            if (++count > 4 || (child.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (child is DirectoryInfo folder)
            {
                if (child.Name is not ("transactions" or "checkpoints") || folder.EnumerateFileSystemInfos().Any()) return false;
            }
            else if (child.Name is not ("output.lock" or "output-store.marker")) return false;
        }
        return true;
    }
}
