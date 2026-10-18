// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.PlaCache;
using KM.PLA.Caching;

namespace KM.Tools.Bridge;

public static class PlaCacheBridgeMapper
{
    public static PlaCacheMode ToCore(PlaCacheModeDto mode)
    {
        return mode switch
        {
            PlaCacheModeDto.Minimal => PlaCacheMode.Minimal,
            PlaCacheModeDto.Balanced => PlaCacheMode.Balanced,
            PlaCacheModeDto.Performance => PlaCacheMode.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static PlaCacheStatusResponse ToDto(PlaCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new PlaCacheStatusResponse(new PlaCacheStatusDto(
            new PlaCacheSettingsDto(ToDto(status.Settings.Mode), status.Settings.MaxCacheSizeBytes),
            status.CacheSizeBytes,
            status.WarmupCompleted,
            status.WarmupTotal,
            status.ProgressPercent,
            status.Phase,
            status.Message,
            status.IsActiveProjectPreserved));
    }

    private static PlaCacheModeDto ToDto(PlaCacheMode mode)
    {
        return mode switch
        {
            PlaCacheMode.Minimal => PlaCacheModeDto.Minimal,
            PlaCacheMode.Balanced => PlaCacheModeDto.Balanced,
            PlaCacheMode.Performance => PlaCacheModeDto.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
