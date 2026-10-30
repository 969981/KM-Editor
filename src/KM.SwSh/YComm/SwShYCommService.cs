// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;

namespace KM.SwSh.YComm;

public sealed record SwShYCommWorkflow(bool CanEdit, bool? Disabled, bool NeedsRepair, string SourceLayer,
    ProjectGame? DetectedGame, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShYCommEditResult(SwShYCommWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class SwShYCommService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.yComm";
    public const string RecordId = "y-comm";
    public const string Field = "disabled";
    public const string SourceCode = "KM-SWSH-Y-COMM-SOURCE-INVALID";
    public const string SessionCode = "KM-SWSH-Y-COMM-SESSION-INVALID";
    public const string StaleCode = "KM-SWSH-Y-COMM-PLAN-STALE";
    public const string ApplyCode = "KM-SWSH-Y-COMM-APPLY-FAILED";
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();

    public SwShYCommWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>(); var state = Read(paths, diagnostics);
        return new(state?.CanEdit == true && !HasErrors(diagnostics), state?.Disabled, state?.NeedsRepair == true,
            state is null ? "missing" : state.Main.Layer == ProjectFileLayer.Layered || state.Hud.Layer == ProjectFileLayer.Layered ? "layered" : "base",
            paths.SelectedGame, diagnostics);
    }

    public SwShYCommEditResult Stage(ProjectPaths paths, bool disabled, EditSession? session)
    {
        var current = session ?? EditSession.Start(); var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics); RequireEditable(state, diagnostics);
        if (state is not null && !HasErrors(diagnostics))
        {
            var retained = current.PendingEdits.Where(edit => edit.Domain != Domain);
            current = current with { PendingEdits = (state.Disabled == disabled && !state.NeedsRepair
                ? retained : retained.Append(CreateEdit(disabled))).ToArray() };
        }
        return new(Load(paths), current, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>(); RequireEditable(Read(paths, diagnostics), diagnostics);
        _ = Decode(session, diagnostics); return new(session, !HasErrors(diagnostics), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>(); var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics); var disabled = Decode(session, diagnostics);
        if (state is null || HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        try
        {
            var sources = new ProjectFileReference[]
            {
                new(ProjectFileLayer.Base, SwShYCommMainPatcher.RelativePath), new(ProjectFileLayer.Base, SwShYCommHudPatcher.RelativePath),
                new(ProjectFileLayer.Generated, SwShYCommMainPatcher.RelativePath), new(ProjectFileLayer.Generated, SwShYCommHudPatcher.RelativePath),
                new(ProjectFileLayer.Generated, state.IpsPath),
            }.Concat(CreateEdit(disabled).Sources).ToArray();
            var writes = Outputs(state, paths.SelectedGame!.Value, disabled).Where(output => !Same(output.Before, output.After))
                .Select(output => new PlannedFileWrite(output.Path, sources, File.Exists(output.Target),
                    disabled ? "Disable Y Comm while keeping Mystery Gift available." : "Restore Y Comm while preserving other edits.")).ToArray();
            return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, diagnostics));
        }
        catch (Exception exception) when (SourceError(exception))
        { return new(session.Id, [], [Error("The Y Comm output could not be prepared. Check the sources, then stage again.")]); }
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session); var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan)) diagnostics.Add(Error("The Y Comm plan changed. Review it again.", StaleCode));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error("Y Comm sources changed. Review again.", StaleCode)]);
            var state = Read(verified.ApplyPaths, diagnostics); var disabled = Decode(session, diagnostics);
            if (state is null || HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            var written = new List<ProjectFileReference>();
            foreach (var output in Outputs(state, paths.SelectedGame!.Value, disabled).Where(output => !Same(output.Before, output.After)))
            {
                if (output.After is null) File.Delete(output.Target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output.Target)!); File.WriteAllBytes(output.Target, output.After);
                    if (!File.ReadAllBytes(output.Target).AsSpan().SequenceEqual(output.After)) throw new InvalidDataException();
                }
                written.Add(new(ProjectFileLayer.Generated, output.Path));
            }
            var after = Read(verified.ApplyPaths, diagnostics);
            if (after is null || after.Disabled != disabled || after.NeedsRepair || HasErrors(diagnostics)) throw new InvalidDataException();
            return verified.Commit(Result(prepared, written, diagnostics));
        }
        catch (Exception exception) when (SourceError(exception))
        { return Result(reviewedPlan, [], [Error("Y Comm output could not be applied. Check sources and output, then review again.", ApplyCode)]); }
        finally { workspace.ClearMemoryCache(); }
    }

    private State? Read(ProjectPaths paths, List<ValidationDiagnostic> diagnostics)
    {
        try
        {
            if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)) throw new InvalidDataException();
            var project = workspace.Open(paths);
            if (!project.Health.CanOpenReadOnlyWorkflows) throw new InvalidDataException();
            var main = ResourceAt(paths, SwShYCommMainPatcher.RelativePath); var hud = ResourceAt(paths, SwShYCommHudPatcher.RelativePath);
            var baseline = SwShYCommMainPatcher.Read(main.Vanilla, paths.SelectedGame);
            var native = SwShYCommMainPatcher.Read(main.Source, paths.SelectedGame);
            if (baseline.Disabled) throw new InvalidDataException();
            SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline.Main, native.Main, "Y Comm");
            var appearance = SwShYCommHudPatcher.Read(hud.Source);
            _ = SwShYCommHudPatcher.Apply(hud.Vanilla, hud.Source, appearance.Disabled);
            var ipsPath = SwShYCommIpsPatcher.RelativePath(paths.SelectedGame.Value);
            var ipsTarget = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, ipsPath) ?? "";
            if (Directory.Exists(ipsTarget)) throw new InvalidDataException();
            var ips = File.Exists(ipsTarget) ? File.ReadAllBytes(ipsTarget) : null;
            var patch = SwShYCommIpsPatcher.Read(ips, paths.SelectedGame.Value);
            var disabled = native.Disabled || patch.Disabled;
            return new(main, hud, ipsPath, ipsTarget, ips, patch.Remaining, patch.Disabled, disabled,
                disabled != appearance.Disabled || patch.Disabled,
                project.Health.CanOpenEditableWorkflows && main.Target.Length > 0 && hud.Target.Length > 0);
        }
        catch (Exception exception) when (SourceError(exception))
        { diagnostics.Add(Error("Y Comm requires readable original 1.3.2 sources and compatible executable, HUD, and patch output.")); return null; }
    }

    private static Resource ResourceAt(ProjectPaths paths, string path)
    {
        var original = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, path) ?? throw new InvalidDataException();
        var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, path) ?? "";
        if (Directory.Exists(target)) throw new InvalidDataException();
        var vanilla = File.ReadAllBytes(original); var exists = File.Exists(target);
        return new(path, vanilla, exists ? File.ReadAllBytes(target) : vanilla, target, exists ? ProjectFileLayer.Layered : ProjectFileLayer.Base);
    }

    private static IEnumerable<Output> Outputs(State state, ProjectGame game, bool disabled)
    {
        foreach (var resource in new[] { state.Main, state.Hud })
        {
            var bytes = resource == state.Main ? SwShYCommMainPatcher.Apply(resource.Vanilla, resource.Source, game, disabled)
                : SwShYCommHudPatcher.Apply(resource.Vanilla, resource.Source, disabled);
            // Missing vanilla output is already effective and need not be materialized.
            var before = File.Exists(resource.Target) ? resource.Source : resource.Vanilla;
            yield return new(resource.Path, resource.Target, before, bytes);
        }
        if (state.IpsDisabled) yield return new(state.IpsPath, state.IpsTarget, state.Ips, state.RemainingIps);
    }

    private static PendingEdit CreateEdit(bool disabled) => new(Domain, disabled ? "Disable Y Comm." : "Enable Y Comm.",
        [new(ProjectFileLayer.Pending, $"pending/y-comm/disabled/{(disabled ? "true" : "false")}")], RecordId, Field, disabled ? "true" : "false");
    private static bool Decode(EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Count != 1) { diagnostics.Add(Error("Stage exactly one Y Comm setting before review.", SessionCode)); return false; }
        var edit = session.PendingEdits[0]; var disabled = edit.NewValue == "true"; var expected = CreateEdit(disabled);
        if (edit.Domain != Domain || edit.RecordId != RecordId || edit.Field != Field || edit.NewValue != expected.NewValue
            || edit.Summary != expected.Summary || !edit.Sources.SequenceEqual(expected.Sources))
            diagnostics.Add(Error("The staged Y Comm setting is invalid. Stage it again.", SessionCode));
        return disabled;
    }
    private static void RequireEditable(State? state, List<ValidationDiagnostic> diagnostics)
    { if (state is not null && !state.CanEdit) diagnostics.Add(Error("Configure an editable Output Root before staging Y Comm.")); }
    private static bool SourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException
        or ArgumentException or OverflowException or KM.Core.Output.OutputCoordinatorException;
    private static bool Same(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private static ValidationDiagnostic Error(string message, string code = SourceCode) =>
        new(DiagnosticSeverity.Error, message, File: SwShYCommMainPatcher.RelativePath, Domain: Domain, Field: Field) { Code = code };
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    { var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow; return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics); }
    private sealed record Resource(string Path, byte[] Vanilla, byte[] Source, string Target, ProjectFileLayer Layer);
    private sealed record Output(string Path, string Target, byte[]? Before, byte[]? After);
    private sealed record State(Resource Main, Resource Hud, string IpsPath, string IpsTarget, byte[]? Ips, byte[]? RemainingIps,
        bool IpsDisabled, bool Disabled, bool NeedsRepair, bool CanEdit);
}
