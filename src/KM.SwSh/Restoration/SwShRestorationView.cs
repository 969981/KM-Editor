// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.SwSh.Restoration;

/// <summary>A disposable prospective output used by existing readers and validators.</summary>
public sealed class SwShRestorationView : IDisposable
{
    private static readonly ConcurrentDictionary<string, SwShRestorationView> Views = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string root;
    private bool disposed;
    public ProjectPaths SourcePaths { get; }
    public ProjectPaths Paths { get; }
    public SwShRestorationStateSnapshot? State { get; set; }
    public static SwShRestorationView? Find(ProjectPaths paths) => paths.OutputRootPath is { Length: > 0 } output
        && Views.TryGetValue(Path.GetFullPath(output), out var view) && view.Paths.SelectedGame == paths.SelectedGame ? view : null;

    public SwShRestorationView(ProjectPaths paths)
    {
        SourcePaths = paths;
        root = Path.Combine(Path.GetTempPath(), "KM", "restoration-preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Paths = paths with { OutputRootPath = root };
        try
        {
            long bytes = 0; var count = 0;
            if (paths.OutputRootPath is { Length: > 0 } source && Directory.Exists(source))
            {
                // Only game output participates. Private history and prepared artifacts
                // remain attached to the real workspace.
                foreach (var folder in new[] { "romfs", "exefs" })
                {
                    var directory = Path.Combine(source, folder);
                    if (!Directory.Exists(directory)) continue;
                    var pending = new Stack<DirectoryInfo>(); pending.Push(new(directory));
                    while (pending.TryPop(out var current))
                    {
                        if (current.LinkTarget is not null) throw new InvalidDataException("Linked restoration output folders are unsupported.");
                        foreach (var child in current.EnumerateDirectories()) pending.Push(child);
                        foreach (var file in current.EnumerateFiles())
                        {
                            if (file.LinkTarget is not null || ++count > 50000 || (bytes += file.Length) > 2L * 1024 * 1024 * 1024)
                                throw new InvalidDataException("Prospective restoration output exceeds its safe copy budget or contains linked files.");
                            var relative = Path.GetRelativePath(source, file.FullName).Replace('\\', '/');
                            var destination = Output(relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                            using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            input.CopyTo(output);
                        }
                    }
                }
            }
            if (!Views.TryAdd(root, this)) throw new InvalidOperationException("Prospective output identity was reused.");
        }
        catch { Dispose(); throw; }
    }

    public string Output(string relative)
    {
        _ = new RelativeOutputPath(relative);
        if (!relative.StartsWith("romfs/", StringComparison.Ordinal) && !relative.StartsWith("exefs/", StringComparison.Ordinal))
            throw new InvalidDataException("Prospective output must address game data.");
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true; Views.TryRemove(root, out _);
        // The exact directory was created by this instance and is never a user path.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
