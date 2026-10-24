// SPDX-License-Identifier: GPL-3.0-only

using System.Security;

namespace KM.Core.Files;

internal static class FileSystemPathBoundary
{
    public static bool HasSafeExistingChain(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            FileSystemInfo? entry = isDirectory
                ? new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                : new FileInfo(Path.GetFullPath(path));
            while (entry is not null)
            {
                entry.Refresh();
                if (!entry.Exists)
                {
                    return false;
                }

                var attributes = entry.Attributes;
                if (attributes.HasFlag(FileAttributes.ReparsePoint)
                    && !string.IsNullOrEmpty(entry.LinkTarget))
                {
                    return false;
                }

                entry = entry switch
                {
                    FileInfo file => file.Directory,
                    DirectoryInfo directory => directory.Parent,
                    _ => null,
                };
            }

            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    public static bool HasSafeExistingAncestorChain(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var directoryEntry = new DirectoryInfo(fullPath);
            directoryEntry.Refresh();
            FileSystemInfo? entry = directoryEntry.Exists ? directoryEntry : new FileInfo(fullPath);
            while (entry is not null)
            {
                if (HasLinkTarget(entry))
                {
                    return false;
                }

                entry = entry switch
                {
                    FileInfo file => file.Directory,
                    DirectoryInfo directory => directory.Parent,
                    _ => null,
                };
            }

            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    internal static bool HasLinkTarget(FileSystemInfo entry)
    {
        entry.Refresh();
        var attributes = entry.Attributes;
        // Missing entries report all attribute bits set, so dangling links still
        // receive a target lookup. Ordinary entries need no second filesystem read.
        return attributes.HasFlag(FileAttributes.ReparsePoint)
            && !string.IsNullOrEmpty(entry.LinkTarget);
    }
}
