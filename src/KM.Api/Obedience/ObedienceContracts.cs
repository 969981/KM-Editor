// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.Obedience;

public sealed record LoadObedienceRequest(ProjectPathsDto Paths);
public sealed record StageObedienceRequest(ProjectPathsDto Paths, IReadOnlyList<int> Selections, EditSessionDto? Session);
public sealed record ObedienceWorkflowDto(bool CanEdit, ProjectGameDto? DetectedGame,
    IReadOnlyList<int> Selections, string SourceLayer, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadObedienceResponse(ObedienceWorkflowDto Workflow);
public sealed record StageObedienceResponse(ObedienceWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
