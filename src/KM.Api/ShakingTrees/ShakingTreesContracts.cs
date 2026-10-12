// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.ShakingTrees;

public sealed record TreeDropDto(int ItemId, int Percent);
public sealed record TreeSlotDto(int Species, int Form, int Percent);
public sealed record TreeRecordDto(string Id, string Label, string AreaId, string Area,
    IReadOnlyList<TreeDropDto> Drops);
public sealed record TreeAreaDto(string Id, string Name, int LevelMin, int LevelMax,
    IReadOnlyList<TreeSlotDto> Slots);
public sealed record TreeOptionDto(int Value, string Label);
public sealed record TreePokemonOptionDto(int Species, int Form, string Label);
public sealed record TreeSettingsDto(int InitialRiskMin, int InitialRiskMax, int RiskStepMin,
    int RiskStepMax, int LoseAllRolls, int DropMin, int DropMax);
public sealed record ShakingTreesWorkflowDto(bool CanEdit, IReadOnlyList<TreeRecordDto> Trees,
    IReadOnlyList<TreeAreaDto> Areas, TreeSettingsDto? Settings, IReadOnlyList<TreeOptionDto> Items,
    IReadOnlyList<TreePokemonOptionDto> Pokemon, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record TreeUpdateDto(string RecordId, string Field, int Value);
public sealed record StageShakingTreesResponse(ShakingTreesWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadShakingTreesRequest(ProjectPathsDto Paths);
public sealed record StageShakingTreesRequest(ProjectPathsDto Paths, IReadOnlyList<TreeUpdateDto>? Updates, EditSessionDto? Session);
public sealed record LoadShakingTreesResponse(ShakingTreesWorkflowDto Workflow);
