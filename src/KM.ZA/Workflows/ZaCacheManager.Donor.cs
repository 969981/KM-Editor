// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.ZA;

namespace KM.ZA.Workflows;

public sealed partial class ZaCacheManager
{
    /// <summary>Reads a matching existing cache without warming, pruning, or modifying it.</summary>
    public bool TryReadExistingDonorIndex(ProjectPaths paths, out ZaTrinityArchiveIndex index)
    {
        lock (syncRoot)
        {
            try
            {
                return TryReadCachedIndex(CreateProjectContext(paths), out index)
                    && index.Files is not null && index.Packs is not null
                    && index.Files.All(file => file is not null && !string.IsNullOrWhiteSpace(file.PackName) && file.PackSize >= 0)
                    && index.Packs.All(pack => pack is not null);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NullReferenceException)
            {
                // Malformed cached indexes are disposable. The donor archive remains the source.
                index = default!;
                return false;
            }
        }
    }

    public bool TryReadExistingDonorPayload(ProjectPaths paths, string virtualPath, out byte[] bytes)
    {
        lock (syncRoot)
        {
            return TryReadPayload(CreateProjectContext(paths), NormalizeVirtualPath(virtualPath), out bytes, touch: false);
        }
    }
}
