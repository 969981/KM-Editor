// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using System.Text.Json.Serialization;

namespace KM.Api.YComm;

public sealed record LoadYCommRequest(ProjectPathsDto Paths);
public sealed record StageYCommRequest(ProjectPathsDto Paths, [property: JsonRequired] bool Disabled, EditSessionDto? Session);
public sealed record YCommWorkflowDto(bool CanEdit, bool? Disabled, bool NeedsRepair, string SourceLayer,
    ProjectGameDto? DetectedGame, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadYCommResponse(YCommWorkflowDto Workflow);
public sealed record StageYCommResponse(YCommWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
