// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Workspace;

/// <summary>Resolves the private application data directory used by the managed editor.</summary>
public static class KmEditorAppData
{
    public static string Resolve()
    {
        // The Windows portable package sets this before launching the managed sidecar.
        // Its Data folder is the root; individual managed services use KM Editor beneath it.
        var portableRoot = Environment.GetEnvironmentVariable("KM_EDITOR_PORTABLE_DATA_DIR");
        if (portableRoot is not null)
        {
            if (string.IsNullOrWhiteSpace(portableRoot) || !Path.IsPathFullyQualified(portableRoot))
                throw new InvalidOperationException("KM_EDITOR_PORTABLE_DATA_DIR must be an absolute path.");
            return Path.Combine(Path.GetFullPath(portableRoot), "KM Editor");
        }

        var overridePath = Environment.GetEnvironmentVariable("KM_EDITOR_APP_DATA_ROOT");
        if (!string.IsNullOrEmpty(overridePath))
        {
            if (!Path.IsPathFullyQualified(overridePath))
                throw new InvalidOperationException("KM_EDITOR_APP_DATA_ROOT must be an absolute path.");
            return Path.GetFullPath(overridePath);
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (!Path.IsPathFullyQualified(localAppData))
            throw new InvalidOperationException("A private local application-data location is unavailable.");

        return Path.Combine(localAppData, "KM Editor");
    }
}
