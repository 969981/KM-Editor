// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core;

/// <summary>
/// Resolves durable state for the managed sidecar. Portable desktop builds pass
/// KM_EDITOR_PORTABLE_DATA_DIR before starting workers; normal installs retain
/// their existing per-user Windows data root.
/// </summary>
public static class PortableApplicationData
{
    private const string PortableDataVariable = "KM_EDITOR_PORTABLE_DATA_DIR";

    public static string GetLocalApplicationDataDirectory()
    {
        var portableData = Environment.GetEnvironmentVariable(PortableDataVariable);
        if (portableData is not null)
        {
            if (string.IsNullOrWhiteSpace(portableData) ||
                !Path.IsPathFullyQualified(portableData))
            {
                throw new InvalidOperationException(
                    "The KM Editor portable data directory must be an absolute path.");
            }

            return Path.GetFullPath(portableData);
        }

        return Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
    }
}
