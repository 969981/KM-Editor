// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using System.Text.Json.Serialization;

namespace KM.Api.PokeJobs;

public sealed record LoadPokeJobsRequest(ProjectPathsDto Paths);
public sealed record StagePokeJobsRequest(ProjectPathsDto Paths, [property: JsonRequired] bool Disabled, EditSessionDto? Session);
public sealed record PokeJobsWorkflowDto(bool CanEdit, bool? Disabled, string SourceLayer,
    ProjectGameDto? DetectedGame, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadPokeJobsResponse(PokeJobsWorkflowDto Workflow);
public sealed record StagePokeJobsResponse(PokeJobsWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
