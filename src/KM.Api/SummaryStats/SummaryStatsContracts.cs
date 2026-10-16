// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;
using System.Text.Json.Serialization;

namespace KM.Api.SummaryStats;

public sealed record SummaryStatsWorkflowDto(WorkflowSummaryDto Summary, string SourceRevision,
    bool Enabled, bool AppliedEnabled, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadSummaryStatsRequest(ProjectPathsDto Paths, EditSessionDto? Session);
public sealed record LoadSummaryStatsResponse(SummaryStatsWorkflowDto Workflow);
public sealed record StageSummaryStatsRequest(ProjectPathsDto Paths, EditSessionDto? Session, string SourceRevision, [property: JsonRequired] bool Enabled);
public sealed record StageSummaryStatsResponse(SummaryStatsWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
