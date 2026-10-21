// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Globalization;

namespace KM.Setup.UI.Burn;

internal sealed record InstallerProgressSnapshot(
    string? ActionKey = null,
    string FileName = "",
    long Bytes = 0,
    long TotalBytes = 0,
    int? PackagePercent = null,
    int? CachePercent = null,
    bool RollingBack = false,
    long LastUpdate = 0);

// Burn can report thousands of file callbacks per second. Keep only the latest
// snapshot so the UI can sample it without queuing one dispatcher call per file.
internal sealed class InstallerProgressDetails
{
    private readonly object gate = new();
    private InstallerProgressSnapshot snapshot = new();
    private string? packageId;
    private string? action;
    private bool caching;
    private bool executing;

    public InstallerProgressSnapshot Snapshot
    {
        get { lock (gate) { return snapshot; } }
    }

    public void Reset()
    {
        lock (gate)
        {
            packageId = action = null;
            caching = executing = false;
            snapshot = new(LastUpdate: Stopwatch.GetTimestamp());
        }
    }

    public void BeginCache()
    {
        lock (gate)
        {
            caching = true;
            executing = false;
            snapshot = new(LastUpdate: Stopwatch.GetTimestamp());
        }
    }

    public void BeginExecution()
    {
        lock (gate)
        {
            caching = false;
            executing = true;
            packageId = action = null;
            snapshot = new(LastUpdate: Stopwatch.GetTimestamp());
        }
    }

    public void BeginPackage(string id, bool rollingBack)
    {
        lock (gate)
        {
            caching = false;
            executing = true;
            packageId = id;
            action = null;
            snapshot = new(RollingBack: rollingBack, LastUpdate: Stopwatch.GetTimestamp());
        }
    }

    public void Finish()
    {
        lock (gate)
        {
            caching = executing = false;
            packageId = action = null;
            snapshot = new(LastUpdate: Stopwatch.GetTimestamp());
        }
    }

    public void CacheProgress(string actionKey, long bytes, long total, int percent)
    {
        lock (gate)
        {
            // Caching can continue in parallel with execution. It must not
            // replace the file operation currently shown to the user.
            if (!caching) return;
            total = Math.Max(0, total);
            snapshot = new(actionKey, Bytes: Math.Clamp(bytes, 0, total),
                TotalBytes: total, CachePercent: Math.Clamp(percent, 0, 100), LastUpdate: Stopwatch.GetTimestamp());
        }
    }

    public void ExecuteProgress(string id, int percent)
    {
        lock (gate)
        {
            if (!executing || packageId != id) return;
            snapshot = snapshot with
            {
                PackagePercent = Math.Clamp(percent, 0, 100),
                LastUpdate = Stopwatch.GetTimestamp(),
            };
        }
    }

    public void BeginMsiAction(string id, IList<string> data)
    {
        lock (gate)
        {
            if (!executing || packageId != id) return;
            action = data.Count > 0 ? data[0] : null;
            snapshot = snapshot with
            {
                ActionKey = ActionKey(action),
                FileName = string.Empty,
                RollingBack = snapshot.RollingBack || action == "Rollback",
                LastUpdate = Stopwatch.GetTimestamp(),
            };
        }
    }

    public void MsiActionData(string id, IList<string> data)
    {
        lock (gate)
        {
            if (!executing || packageId != id) return;
            // Only known file actions have a filename in field 1. Other
            // records may contain registry values or arbitrary local data.
            var fileName = action is "InstallFiles" or "RemoveFiles" or "MoveFiles" or "DuplicateFiles"
                && data.Count > 0 ? SafeFileName(data[0]) : string.Empty;
            snapshot = snapshot with { FileName = fileName, LastUpdate = Stopwatch.GetTimestamp() };
        }
    }

    private static string SafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var leaf = value.AsSpan(value.LastIndexOfAny(['\\', '/']) + 1);
        if (leaf.Length is 0 or > 255) return string.Empty;
        foreach (var character in leaf)
        {
            if (char.IsControl(character) || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format ||
                character is ':' or '"' or '<' or '>' or '|') return string.Empty;
        }
        return leaf.ToString();
    }

    private static string ActionKey(string? value) => value switch
    {
        "InstallFiles" or "MoveFiles" or "DuplicateFiles" => "ProgressInstallingFiles",
        "RemoveFiles" or "RemoveDuplicateFiles" => "ProgressRemovingFiles",
        "CreateFolders" => "ProgressCreatingFolders",
        "RemoveFolders" => "ProgressRemovingFolders",
        "CreateShortcuts" => "ProgressCreatingShortcuts",
        "RemoveShortcuts" => "ProgressRemovingShortcuts",
        "WriteRegistryValues" or "RegisterProduct" or "RegisterUser" or "PublishProduct" or
            "PublishFeatures" or "RegisterClassInfo" or "RegisterExtensionInfo" => "ProgressRegistering",
        "RemoveRegistryValues" or "UnpublishFeatures" or "UnregisterClassInfo" or
            "UnregisterExtensionInfo" => "ProgressUnregistering",
        "FileCost" or "CostInitialize" or "CostFinalize" or "InstallValidate" => "ProgressCheckingFiles",
        "ProcessComponents" => "ProgressUpdatingComponents",
        "RemoveExistingProducts" => "ProgressRemovingPreviousVersion",
        "InstallInitialize" or "InstallExecute" or "InstallExecuteAgain" => "ProgressPreparingChanges",
        "InstallFinalize" => "ProgressSavingChanges",
        "Rollback" => "ProgressRollingBack",
        "Wix4RemoveFoldersEx_X64" => "ProgressCheckingCache",
        _ => "ProgressInstallerStep",
    };
}
