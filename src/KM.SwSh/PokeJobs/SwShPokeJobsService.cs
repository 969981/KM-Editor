// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;

namespace KM.SwSh.PokeJobs;

public sealed record SwShPokeJobsWorkflow(bool CanEdit, bool? Disabled, string SourceLayer,
    ProjectGame? DetectedGame, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShPokeJobsEditResult(SwShPokeJobsWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class SwShPokeJobsService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.pokeJobs";
    public const string RecordId = "poke-jobs";
    public const string Field = "disabled";
    public const string SourceCode = "KM-SWSH-POKE-JOBS-SOURCE-INVALID";
    public const string SessionCode = "KM-SWSH-POKE-JOBS-SESSION-INVALID";
    public const string StaleCode = "KM-SWSH-POKE-JOBS-PLAN-STALE";
    public const string ApplyCode = "KM-SWSH-POKE-JOBS-APPLY-FAILED";
    private const string ScriptPath = SwShPokeJobsPatcher.RelativePath;
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();

    public SwShPokeJobsWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        return new(state is not null && state.CanEdit && !HasErrors(diagnostics), state?.Disabled,
            state?.Layer.ToString().ToLowerInvariant() ?? "missing", paths.SelectedGame, diagnostics);
    }

    public SwShPokeJobsEditResult Stage(ProjectPaths paths, bool disabled, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        if (state is not null && !state.CanEdit)
            diagnostics.Add(Error("Poke Jobs requires an editable project with a configured Output Root."));
        if (state is not null && !HasErrors(diagnostics))
        {
            // Keep other domains. A current live source is sufficient even if generated files were deleted.
            var retained = current.PendingEdits.Where(edit => edit.Domain != Domain);
            current = current with { PendingEdits = (state.Disabled == disabled
                ? retained : retained.Append(CreateEdit(disabled))).ToArray() };
        }
        return new(Load(paths), current, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        ValidateEdit(session, diagnostics);
        if (state is not null && !state.CanEdit)
            diagnostics.Add(Error("Poke Jobs requires an editable project with a configured Output Root."));
        return new(session, !HasErrors(diagnostics), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        var disabled = ValidateEdit(session, diagnostics);
        if (state is not null && !state.CanEdit)
            diagnostics.Add(Error("Poke Jobs requires an editable project with a configured Output Root."));
        if (state is null || HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        var sources = new List<ProjectFileReference> { new(ProjectFileLayer.Base, ScriptPath) };
        if (state.Layer == ProjectFileLayer.Layered) sources.Add(new(ProjectFileLayer.Layered, ScriptPath));
        sources.AddRange(CreateEdit(disabled).Sources);
        var write = new PlannedFileWrite(ScriptPath, sources.Distinct().ToArray(), File.Exists(state.Target),
            disabled ? "Disable all Poke Jobs." : "Restore Poke Jobs while preserving other script edits.");
        return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, [write], diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan))
                diagnostics.Add(Error("The Poke Jobs output plan changed. Review it again before applying.", StaleCode));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures))
                return Result(plan, [], failures);
            using var verified = scope!;
            var snapshotPlan = CreateChangePlan(verified.ApplyPaths, session);
            if (!verified.TryPrepareSnapshotPlan(snapshotPlan, out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error("Poke Jobs sources changed while preparing output. Review again.", StaleCode)]);
            var snapshotDiagnostics = prepared.Diagnostics.ToList();
            var state = Read(verified.ApplyPaths, snapshotDiagnostics);
            if (state is null || HasErrors(snapshotDiagnostics)) return Result(plan, [], snapshotDiagnostics);
            var disabled = ValidateEdit(session, snapshotDiagnostics);
            if (HasErrors(snapshotDiagnostics)) return Result(plan, [], snapshotDiagnostics);
            var bytes = SwShPokeJobsPatcher.Apply(state.Vanilla, state.Source, disabled);
            Directory.CreateDirectory(Path.GetDirectoryName(state.Target)!);
            File.WriteAllBytes(state.Target, bytes);
            var roundTrip = File.ReadAllBytes(state.Target);
            if (!roundTrip.AsSpan().SequenceEqual(bytes)
                || SwShPokeJobsPatcher.ReadDisabled(state.Vanilla, roundTrip) != disabled)
                throw new InvalidDataException("Poke Jobs output failed verification.");
            return verified.Commit(Result(prepared, [new(ProjectFileLayer.Generated, ScriptPath)], snapshotDiagnostics));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException or KM.Core.Output.OutputCoordinatorException)
        {
            return Result(reviewedPlan, [], [Error("Poke Jobs output could not be applied. Check sources and output, then review again.", ApplyCode)]);
        }
        finally { workspace.ClearMemoryCache(); }
    }

    private State? Read(ProjectPaths paths, List<ValidationDiagnostic> diagnostics)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            diagnostics.Add(Error("Poke Jobs supports Pokemon Sword and Pokemon Shield."));
            return null;
        }
        try
        {
            // Reuse the shared graph. Open expires it when the project filesystem stamp changes;
            // the owned script is read directly below, including after output deletion.
            var project = workspace.Open(paths);
            if (!project.Health.CanOpenReadOnlyWorkflows)
            {
                diagnostics.Add(Error("Poke Jobs requires valid Base RomFS and Base ExeFS paths."));
                return null;
            }
            var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, ScriptPath);
            var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, ScriptPath);
            if (basePath is null) throw new InvalidDataException("The vanilla Pokemon Center terminal script is missing from Base RomFS.");
            var vanilla = File.ReadAllBytes(basePath);
            // A directory or unreadable output must not silently fall back to vanilla.
            if (target is not null && Directory.Exists(target))
                throw new InvalidDataException("The Pokemon Center terminal script output path is a directory. It must be a readable file or absent.");
            var hasOutput = target is not null && File.Exists(target);
            var source = hasOutput ? File.ReadAllBytes(target!) : vanilla;
            return new(vanilla, source, target ?? string.Empty,
                hasOutput ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
                SwShPokeJobsPatcher.ReadDisabled(vanilla, source),
                project.Health.CanOpenEditableWorkflows && target is not null);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException or KM.Core.Output.OutputCoordinatorException)
        {
            diagnostics.Add(Error("Poke Jobs requires a readable supported original Pokemon Center script and compatible output."));
            return null;
        }
    }

    private static PendingEdit CreateEdit(bool disabled) => new(Domain,
        disabled ? "Disable Poke Jobs." : "Enable Poke Jobs.",
        [new(ProjectFileLayer.Pending, $"pending/poke-jobs/disabled/{(disabled ? "true" : "false")}")],
        RecordId, Field, disabled ? "true" : "false");

    private static bool ValidateEdit(EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(Error("Poke Jobs requires exactly one staged setting.", SessionCode));
            return false;
        }
        var edit = session.PendingEdits[0];
        var disabled = edit.NewValue == "true";
        var expected = CreateEdit(disabled);
        if (edit.Domain != Domain || edit.RecordId != RecordId || edit.Field != Field
            || edit.NewValue != expected.NewValue || edit.Summary != expected.Summary
            || !edit.Sources.SequenceEqual(expected.Sources))
            diagnostics.Add(Error("The staged Poke Jobs setting is invalid. Stage the setting again.", SessionCode));
        return disabled;
    }

    private static ValidationDiagnostic Error(string message, string code = SourceCode) =>
        new(DiagnosticSeverity.Error, message, File: ScriptPath, Domain: Domain, Field: Field) { Code = code };
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics);
    }
    private sealed record State(byte[] Vanilla, byte[] Source, string Target, ProjectFileLayer Layer,
        bool Disabled, bool CanEdit);
}
