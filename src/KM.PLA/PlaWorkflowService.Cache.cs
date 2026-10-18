// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Concurrency;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.PLA.Caching;

namespace KM.PLA;

public sealed partial class PlaWorkflowService
{
    private readonly PlaCacheManager cacheManager;
    private readonly PlaWorkflowCache cache;

    public PlaWorkflowService(PlaCacheManager? cacheManager = null)
    {
        this.cacheManager = cacheManager ?? new();
        cache = new(this.cacheManager);
    }

    public void ClearMemoryCaches() => cacheManager.ClearMemory();

    public PlaCacheStatus GetCacheStatus(ProjectPaths? paths = null)
    {
        if (paths is null) return cacheManager.GetStatus();
        // Preparation is independent of live output and pending edits.
        using var scope = new PlaSourceScope(paths with { OutputRootPath = null });
        var status = cacheManager.GetStatus(scope.CacheSource);
        var kinds = PreparationKinds(paths, status.Settings.Mode);
        var completed = kinds.Count(kind => cache.IsPrepared(scope, kind));
        return status with
        {
            WarmupCompleted = completed, WarmupTotal = kinds.Length,
            ProgressPercent = kinds.Length == 0 ? 100 : completed * 100 / kinds.Length,
            Phase = completed == kinds.Length ? "Cache ready" : "Preparing cache",
            Message = completed == kinds.Length ? "PLA cache is ready." : "Preparing reusable PLA editor catalogs.",
        };
    }

    public PlaCacheStatus UpdateCacheSettings(PlaCacheMode mode, long maxCacheSizeBytes, ProjectPaths? paths = null)
    {
        using var scope = paths is null ? null : new PlaSourceScope(paths with { OutputRootPath = null });
        cacheManager.UpdateSettings(mode, maxCacheSizeBytes, scope?.CacheSource);
        return GetCacheStatus(paths);
    }

    public PlaCacheStatus ClearCache(ProjectPaths? paths = null)
    {
        using var scope = paths is null ? null : new PlaSourceScope(paths with { OutputRootPath = null });
        cacheManager.Clear(scope?.CacheSource);
        return GetCacheStatus(paths);
    }

    public PlaCacheStatus WarmupCacheStep(ProjectPaths paths, int stepIndex)
    {
        if (BoundedConcurrencyHostBudget.IsReadWorker)
            throw new InvalidOperationException("An isolated managed read worker cannot prepare the PLA cache.");
        if (stepIndex < 0) throw new ArgumentOutOfRangeException(nameof(stepIndex));
        var basePaths = paths with { OutputRootPath = null };
        var kinds = PreparationKinds(basePaths, cacheManager.GetSettings().Mode);
        using (var scope = new PlaSourceScope(basePaths))
        {
            // A restart or deleted artifact can leave a hole before the caller's
            // cursor. Prepare one missing unit, then report actual completed work.
            var missing = kinds.FirstOrDefault(kind => !cache.IsPrepared(scope, kind));
            if (missing is not null)
            {
                scope.ResetCacheObservations();
                var listing = ReadListing(scope, EditSession.Start(), missing, null);
                cache.StoreListing(scope, missing, null, listing);
            }
        }
        return GetCacheStatus(basePaths);
    }

    private static string[] PreparationKinds(ProjectPaths paths, PlaCacheMode mode) => mode == PlaCacheMode.Minimal ? []
        : mode == PlaCacheMode.Performance && !string.IsNullOrWhiteSpace(paths.PokemonLegendsArceusSupportFolderPath)
            ? [.. PlaWorkflowCache.CoreKinds, .. PlaWorkflowCache.ArchiveKinds] : PlaWorkflowCache.CoreKinds;
}
