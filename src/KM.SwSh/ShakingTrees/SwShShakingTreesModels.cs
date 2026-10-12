// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.ShakingTrees;

public sealed record SwShTreeDrop(int ItemId, int Percent);
public sealed record SwShTreeSlot(int Species, int Form, int Percent);
public sealed record SwShTreeRecord(string Id, string Label, string AreaId, string Area,
    IReadOnlyList<SwShTreeDrop> Drops);
public sealed record SwShTreeArea(string Id, string Name, int LevelMin, int LevelMax,
    IReadOnlyList<SwShTreeSlot> Slots);
public sealed record SwShTreeOption(int Value, string Label);
public sealed record SwShTreePokemonOption(int Species, int Form, string Label);
public sealed record SwShTreeSettings(int InitialRiskMin, int InitialRiskMax, int RiskStepMin,
    int RiskStepMax, int LoseAllRolls, int DropMin, int DropMax);
public sealed record SwShShakingTreesWorkflow(bool CanEdit, IReadOnlyList<SwShTreeRecord> Trees,
    IReadOnlyList<SwShTreeArea> Areas, SwShTreeSettings? Settings, IReadOnlyList<SwShTreeOption> Items,
    IReadOnlyList<SwShTreePokemonOption> Pokemon, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShTreeUpdate(string RecordId, string Field, int Value);
public sealed record SwShShakingTreesResult(SwShShakingTreesWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
