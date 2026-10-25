// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.ShakingTrees;

public sealed class SwShShakingTreesService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.shakingTrees";
    public const string SourceCode = "KM-SWSH-TREES-SOURCE-INVALID";
    public const string ValuesCode = "KM-SWSH-TREES-VALUES-INVALID";
    public const string ConflictCode = "KM-SWSH-TREES-CONFLICT";
    public const string SessionCode = "KM-SWSH-TREES-SESSION-INVALID";
    public const string StaleCode = "KM-SWSH-TREES-PLAN-STALE";
    public const string ApplyCode = "KM-SWSH-TREES-APPLY-FAILED";
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();
    private readonly bool itemsOnly;

    internal SwShShakingTreesService(bool itemsOnly) : this() => this.itemsOnly = itemsOnly;

    public static SwShWorkflowSummary CreateSummary(OpenedProject project) => new(SwShWorkflowIds.ShakingTrees,
        "Shaking Trees", "Edit tree items, quantities, encounter tables, and shake risk.",
        !ProjectGameMetadata.IsSwordShield(project.Paths.SelectedGame) || !project.Health.CanOpenReadOnlyWorkflows
            ? SwShWorkflowAvailability.Disabled : project.Health.CanOpenEditableWorkflows
                ? SwShWorkflowAvailability.Available : SwShWorkflowAvailability.ReadOnly, []);

    public SwShShakingTreesWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        return Read(paths, diagnostics)?.Workflow(diagnostics) ?? Empty(diagnostics);
    }

    public SwShShakingTreesResult Stage(ProjectPaths paths, IReadOnlyList<SwShTreeUpdate>? updates, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics, current);
        if (state is null) return new(Empty(diagnostics), current, diagnostics);
        if (!state.CanEdit) diagnostics.Add(Error(SourceCode, "Configure an editable output before staging tree changes."));
        if (updates is null || updates.Count is < 1 or > 20_000 || updates.Any(row => row is null)
            || updates.Select(row => (row.RecordId, row.Field)).Distinct().Count() != updates.Count)
            diagnostics.Add(Error(SessionCode, "Stage distinct, bounded tree fields."));
        if (HasErrors(diagnostics)) return new(state.Workflow(diagnostics), current, diagnostics);
        var pending = current.PendingEdits.ToList();
        foreach (var update in updates!)
        {
            var key = SwShTreeSources.Key(update.RecordId, update.Field);
            if (!state.Cells.TryGetValue(key, out var cell) || update.Value < cell.Min || update.Value > cell.Max)
            { diagnostics.Add(Error(ValuesCode, "A tree field is missing or outside its supported range.", update.Field)); continue; }
            pending.RemoveAll(edit => edit.Domain == Domain && edit.RecordId == update.RecordId && edit.Field == update.Field);
            if (update.Value != cell.Value) pending.Add(Edit(update));
        }
        var candidate = current with { PendingEdits = pending };
        _ = Desired(state, candidate, diagnostics);
        return new(state.Workflow(diagnostics), HasErrors(diagnostics) ? current : candidate, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics, session);
        if (state is not null) _ = Desired(state, session, diagnostics);
        return new(session, !HasErrors(diagnostics), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics, session);
        if (state is null) return new(session.Id, [], diagnostics);
        var desired = Desired(state, session, diagnostics);
        if (HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        try
        {
            var outputs = Build(state, desired);
            var sources = state.Sources.Concat(session.PendingEdits.Where(edit => edit.Domain == Domain).SelectMany(edit => edit.Sources)).Distinct().ToArray();
            var writes = outputs.Select(pair => new PlannedFileWrite(pair.Key, sources,
                File.Exists(SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, pair.Key)),
                "Apply tree changes while preserving unrelated records and executable settings.")).ToArray();
            return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, diagnostics));
        }
        catch (Exception exception) when (IsSourceError(exception))
        { diagnostics.Add(Error(SourceCode, exception.Message)); return new(session.Id, [], diagnostics); }
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan)) diagnostics.Add(Error(StaleCode, "The tree plan changed. Review it again."));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error(StaleCode, "Tree sources changed while preparing output. Review again.")]);
            var state = Read(verified.ApplyPaths, diagnostics, session);
            if (state is null) return Result(plan, [], diagnostics);
            var desired = Desired(state, session, diagnostics);
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            var outputs = Build(state, desired);
            var written = new List<ProjectFileReference>();
            foreach (var (path, bytes) in outputs)
            {
                var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(verified.ApplyPaths, path) ?? throw new IOException("The tree output target is unavailable.");
                if (path == SwShTreeSources.MainPath && SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(bytes, state.Read(path, true))) File.Delete(target);
                else
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, bytes);
                    if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new IOException("Tree output failed readback.");
                }
                written.Add(new(ProjectFileLayer.Generated, path));
            }
            return verified.Commit(Result(prepared, written, diagnostics));
        }
        catch (Exception exception) when (IsSourceError(exception))
        { return Result(reviewedPlan, [], [Error(ApplyCode, $"Tree output could not be applied: {exception.Message}")]); }
        finally { workspace.ClearMemoryCache(); }
    }

    private SwShTreeSources? Read(ProjectPaths paths, List<ValidationDiagnostic> diagnostics, EditSession? session = null)
    {
        try { return new(workspace.Open(paths), session, itemsOnly); }
        catch (Exception exception) when (IsSourceError(exception))
        { diagnostics.Add(Error(SourceCode, exception.Message)); return null; }
    }

    private static Dictionary<string, int> Desired(SwShTreeSources state, EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        var values = state.Values();
        if (!state.CanEdit) diagnostics.Add(Error(SourceCode, "Configure an editable output before reviewing tree changes."));
        var edits = session.PendingEdits.Where(edit => edit.Domain == Domain).ToArray();
        if (edits.Length > 20_000 || edits.Select(edit => (edit.RecordId, edit.Field)).Distinct().Count() != edits.Length)
            diagnostics.Add(Error(SessionCode, "The tree draft contains duplicate or excessive fields."));
        foreach (var edit in edits)
        {
            var key = SwShTreeSources.Key(edit.RecordId ?? "", edit.Field ?? "");
            if (!state.Cells.TryGetValue(key, out var cell) || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || value < cell.Min || value > cell.Max)
            { diagnostics.Add(Error(SessionCode, "A staged tree field is invalid or its source record is missing.")); continue; }
            var expected = Edit(new(edit.RecordId!, edit.Field!, value));
            if (edit.Summary != expected.Summary || edit.NewValue != expected.NewValue || !edit.Sources.SequenceEqual(expected.Sources))
            { diagnostics.Add(Error(SessionCode, "A staged tree field has inconsistent review metadata.")); continue; }
            values[key] = value;
        }
        var ownValues = values;
        values = SwShTreeComposition.Overlay(state, session, ownValues, diagnostics);
        int Get(string id, string field) => SwShTreeSources.Get(values, id, field);
        if (values.Any(pair => pair.Value < state.Cells[pair.Key].Min || pair.Value > state.Cells[pair.Key].Max))
            diagnostics.Add(Error(ValuesCode, "The current tree data has fields outside the supported ranges."));
        foreach (var tree in state.Trees)
            if (Enumerable.Range(0, tree.Slots).Sum(slot => Get(tree.Id, $"percent.{slot}")) != 100
                || Enumerable.Range(0, tree.Slots).Any(slot => !state.Items.Any(item => item.Value == Get(tree.Id, $"item.{slot}"))))
                diagnostics.Add(Error(ValuesCode, "Every tree needs valid items and item percentages totaling 100%.", tree.Id));
        foreach (var area in state.Areas)
        {
            if (Get(area.Id, "levelMin") > Get(area.Id, "levelMax")
                || Enumerable.Range(0, area.Slots).Sum(slot => Get(area.Id, $"percent.{slot}")) != 100)
                diagnostics.Add(Error(ValuesCode, "Encounter levels must be ordered and species percentages must total 100%.", area.Id));
            foreach (var slot in Enumerable.Range(0, area.Slots))
                if (Get(area.Id, $"percent.{slot}") > 0 && !state.Pokemon.Any(pokemon => pokemon.Species == Get(area.Id, $"species.{slot}") && pokemon.Form == Get(area.Id, $"form.{slot}")))
                    diagnostics.Add(Error(ValuesCode, "A tree encounter uses a Pokemon form absent from the current game.", area.Id));
        }
        if (state.ItemsOnly) return ownValues;
        var settings = SwShTreeSources.Settings(values);
        if (settings.InitialRiskMin > settings.InitialRiskMax || settings.RiskStepMin > settings.RiskStepMax || settings.DropMin > settings.DropMax
            || (99 - settings.InitialRiskMin) / Math.Max(1, settings.RiskStepMin) * settings.DropMax > 255)
            diagnostics.Add(Error(ValuesCode, "Risk and quantity ranges must be ordered, with at most 255 possible items before an encounter.", "settings"));
        return ownValues;
    }

    private static Dictionary<string, byte[]> Build(SwShTreeSources state, IReadOnlyDictionary<string, int> values)
    {
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var changed = values.Where(pair => state.Cells[pair.Key].Value != pair.Value).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        int Get(string id, string field) => SwShTreeSources.Get(values, id, field);
        foreach (var group in state.Trees.Where(tree => changed.Any(key => key.StartsWith(tree.Id + "|", StringComparison.Ordinal))).GroupBy(tree => tree.Member))
        {
            var raw = new List<SwShTreeItemEdit>();
            foreach (var tree in group)
            for (var slot = 0; slot < tree.Slots; slot++)
            {
                if (!changed.Contains(SwShTreeSources.Key(tree.Id, $"item.{slot}")) && !changed.Contains(SwShTreeSources.Key(tree.Id, $"percent.{slot}"))) continue;
                raw.Add(new(tree.Zone, tree.Object, slot, state.ItemHashes[Get(tree.Id, $"item.{slot}")], (uint)Get(tree.Id, $"percent.{slot}")));
            }
            state.Placement.SetFileByName(group.Key, SwShTreeItemWriter.Write(group.First().Archive.SourceData, raw));
            outputs[SwShTreeSources.PlacementPath] = [];
        }
        if (outputs.ContainsKey(SwShTreeSources.PlacementPath)) outputs[SwShTreeSources.PlacementPath] = state.Placement.Write();
        if (state.ItemsOnly) return outputs;
        var encounterBytes = state.Encounters!.GetFileByName(state.EncounterMember);
        foreach (var area in state.Areas.Where(area => changed.Any(key => key.StartsWith(area.Id + "|", StringComparison.Ordinal))))
        {
            var desired = new SwShWildEncounterSubTable((byte)Get(area.Id, "levelMin"), (byte)Get(area.Id, "levelMax"),
                Enumerable.Range(0, area.Slots).Select(slot => new SwShWildEncounterSlot((byte)Get(area.Id, $"percent.{slot}"), Get(area.Id, $"species.{slot}"), (byte)Get(area.Id, $"form.{slot}"))).ToArray());
            encounterBytes = SwShTreeEncounterWriter.Write(encounterBytes, area.Table, desired);
            outputs[SwShTreeSources.EncounterPath] = [];
        }
        if (outputs.ContainsKey(SwShTreeSources.EncounterPath))
        { state.Encounters.SetFileByName(state.EncounterMember, encounterBytes); outputs[SwShTreeSources.EncounterPath] = state.Encounters.Write(); }
        var settings = SwShTreeSources.Settings(values);
        if (SwShTreeScript.Fields.Any(field => changed.Contains(SwShTreeSources.Key("settings", field))))
            outputs[SwShTreeScript.Path] = SwShTreeScript.Apply(state.Read(SwShTreeScript.Path, true), state.Read(SwShTreeScript.Path), settings);
        if (changed.Contains("settings|dropMin") || changed.Contains("settings|dropMax"))
            outputs[SwShTreeSources.MainPath] = SwShTreeQuantityPatcher.Apply(state.Read(SwShTreeSources.MainPath, true), state.Read(SwShTreeSources.MainPath), state.Paths.SelectedGame!.Value, settings.DropMin, settings.DropMax);
        return outputs;
    }

    private static PendingEdit Edit(SwShTreeUpdate update)
    {
        var value = update.Value.ToString(CultureInfo.InvariantCulture);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{update.RecordId}|{update.Field}|{value}")));
        return new(Domain, "Stage tree field.", [new(ProjectFileLayer.Pending, $"pending/shaking-trees/{hash}")], update.RecordId, update.Field, value);
    }
    private static SwShShakingTreesWorkflow Empty(IReadOnlyList<ValidationDiagnostic> diagnostics) => new(false, [], [], null, [], [], diagnostics);
    private static bool IsSourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException or InvalidOperationException or KeyNotFoundException;
    private static ValidationDiagnostic Error(string code, string message, string? field = null) => new(DiagnosticSeverity.Error, message, Domain: Domain, Field: field) { Code = code };
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(value => value.Severity == DiagnosticSeverity.Error);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics);
    }
}
