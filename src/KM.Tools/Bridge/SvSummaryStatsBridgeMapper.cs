// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.SummaryStats;
using KM.Api.Workflows;
using KM.SV.SummaryStats;
using KM.SV.Workflows;

namespace KM.Tools.Bridge;

internal static class SvSummaryStatsBridgeMapper
{
    internal static SummaryStatsWorkflowDto ToDto(SvSummaryStatsWorkflow workflow) => new(
        new WorkflowSummaryDto(workflow.Summary.Id, workflow.Summary.Label, workflow.Summary.Description,
            workflow.Summary.Availability switch { SvWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
                SvWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly, _ => WorkflowAvailabilityDto.Disabled },
            workflow.Summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray()), workflow.SourceRevision,
        workflow.Enabled, workflow.AppliedEnabled, workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
}
