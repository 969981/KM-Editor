// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;

namespace KM.Api.PlaCache;

public enum PlaCacheModeDto
{
    Minimal,
    Balanced,
    Performance,
}

public sealed record PlaCacheSettingsDto(
    PlaCacheModeDto Mode,
    long MaxCacheSizeBytes);

public sealed record PlaCacheStatusDto(
    PlaCacheSettingsDto Settings,
    long CacheSizeBytes,
    int WarmupCompleted,
    int WarmupTotal,
    int ProgressPercent,
    string Phase,
    string Message,
    bool IsActiveProjectPreserved);

public sealed record GetPlaCacheStatusRequest(ProjectPathsDto? Paths);

public sealed record UpdatePlaCacheSettingsRequest(
    PlaCacheModeDto Mode,
    long MaxCacheSizeBytes,
    ProjectPathsDto? Paths);

public sealed record ClearPlaCacheRequest(ProjectPathsDto? ActivePaths);

public sealed record WarmupPlaCacheStepRequest(
    ProjectPathsDto Paths,
    int StepIndex);

public sealed record PlaCacheStatusResponse(PlaCacheStatusDto Status);
