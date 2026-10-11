// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.HyperTraining;

public sealed class SwShHyperTrainingEditSessionService
{
    public const string HyperTrainingEditDomain = "workflow.hyperTraining";

    private const string RecordId = "hyper-training-minimum-level";
    private const string MinimumLevelField = "minimumLevel";
    private const string StageSummaryPrefix = "Stage Hyper Training minimum level Lv.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShHyperTrainingWorkflowService hyperTrainingWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShHyperTrainingEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.hyperTrainingWorkflowService = hyperTrainingWorkflowService ?? new SwShHyperTrainingWorkflowService();
    }

    internal SwShHyperTrainingEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, hyperTrainingWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    internal SwShHyperTrainingEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, hyperTrainingWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    public SwShHyperTrainingEditResult StageMinimumLevel(
        ProjectPaths paths,
        int minimumLevel,
        EditSession? session,
        int? bottleCapItemId = null, int? goldBottleCapItemId = null, bool? pokemonCenterEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperTrainingWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var selection = new SwShHyperTrainingSelection(minimumLevel, new(
            bottleCapItemId ?? workflow.Items.BottleCapItemId, goldBottleCapItemId ?? workflow.Items.GoldBottleCapItemId),
            pokemonCenterEnabled ?? workflow.PokemonCenterEnabled);
        try { SwShHyperTrainingItemWorkflow.ValidateSelection(selection.Items, workflow); }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(SwShHyperTrainingItemWorkflow.Diagnostic(exception.Message));
            return new(workflow, currentSession, diagnostics);
        }


        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training needs its own edit session before staging.",
                expected: "A Hyper Training-only edit session"));
            return new SwShHyperTrainingEditResult(workflow, currentSession, diagnostics);
        }

        if (!ValidateMinimumLevel(minimumLevel, diagnostics) || !CanStage(project, workflow, diagnostics))
        {
            return new SwShHyperTrainingEditResult(workflow, currentSession, diagnostics);
        }

        var sources = CreatePendingSources(project, selection, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShHyperTrainingEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(selection, sources))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Hyper Training minimum level Lv.{minimumLevel} is staged for change-plan review.")));

        return new SwShHyperTrainingEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperTrainingWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanStage(project, workflow, diagnostics))
        {
            return new SwShEditSessionValidation(session, false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage a Hyper Training minimum level before validating."
                    : "Hyper Training expects exactly one canonical staged minimum-level edit.",
                expected: "Exactly one pending Hyper Training minimum-level edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (!IsCanonicalIdentity(edit))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the canonical Hyper Training minimum-level field.",
                    field: edit.Field,
                    expected: $"{HyperTrainingEditDomain}/{RecordId}/{MinimumLevelField}"));
            }
            else
            {
                ValidateCanonicalEdit(project, edit, diagnostics);
            }
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Hyper Training change is valid for change-plan review."));
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var pendingEdit = session.PendingEdits.Single();
        var selection = ParsePendingSelection(pendingEdit.NewValue, diagnostics);
        var minimumLevel = selection.MinimumLevel;
        var scriptSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ScriptPath);
        var scriptTargetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ScriptPath, diagnostics);
        var mainSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ExeFsMainPath);
        var mainTargetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ExeFsMainPath, diagnostics);
        if (scriptSource is null || scriptTargetPath is null || mainSource is null || mainTargetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training source or output target could not be resolved.",
                file: scriptSource is null ? SwShHyperTrainingWorkflowService.ScriptPath : SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Readable script/main sources and writable LayeredFS targets"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>
        {
            new(
                SwShHyperTrainingWorkflowService.ScriptPath,
                CreatePlanSources(scriptSource.Entry, selection, project),
                File.Exists(scriptTargetPath),
                string.Create(CultureInfo.InvariantCulture, $"Set Hyper Training to Lv.{minimumLevel}, with payment items {selection.Items.BottleCapItemId} and {selection.Items.GoldBottleCapItemId}.")),
            new(
                SwShHyperTrainingWorkflowService.ExeFsMainPath,
                CreatePlanSources(mainSource.Entry, selection, project),
                File.Exists(mainTargetPath),
                string.Create(CultureInfo.InvariantCulture, $"Update the Hyper Training picker cutoff to Lv.{minimumLevel} and its payment items in exefs/main.")),
        };

        var dialogueSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        if (dialogueSource is not null)
        {
            var dialogueTargetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.EnglishDialoguePath, diagnostics);
            if (dialogueTargetPath is not null)
            {
                writes.Add(new PlannedFileWrite(
                    SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                    CreatePlanSources(dialogueSource.Entry, selection, project),
                    File.Exists(dialogueTargetPath),
                    string.Create(CultureInfo.InvariantCulture, $"Update English Hyper Training NPC dialogue to mention Lv.{minimumLevel}.")));
            }
        }

        if (NeedsItemWrites(project, selection.Items))
        {
            foreach (var path in SwShHyperTrainingItemDialogue.Targets(project))
            {
                var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path);
                if (source is null) { diagnostics.Add(SwShHyperTrainingItemWorkflow.Diagnostic("Hyper Training payment dialogue is unavailable.")); continue; }
                try
                {
                    var baseline = File.ReadAllBytes(SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, path)!);
                    var names = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingItemDialogue.NamesPath(path));
                    _ = SwShHyperTrainingItemDialogue.Apply(File.ReadAllBytes(source.AbsolutePath), baseline,
                        names is null ? null : File.ReadAllBytes(names.AbsolutePath), path, selection.Items);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    diagnostics.Add(SwShHyperTrainingItemWorkflow.Diagnostic(exception.Message));
                    continue;
                }
                if (writes.Any(write => write.TargetRelativePath == path)) continue;
                var target = ResolveOutputPath(paths, path, diagnostics);
                if (target is null) continue;
                writes.Add(new(path, CreatePlanSources(source.Entry, selection, project),
                    File.Exists(target), "Update Hyper Training payment item names."));
            }
        }

        try
        {
            foreach (var output in SwShHyperTrainingCenterWorkflow.Prepare(project, selection))
            {
                var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, output.Path)!;
                var target = ResolveOutputPath(paths, output.Path, diagnostics);
                if (target is not null) writes.Add(new(output.Path, CreatePlanSources(source.Entry, selection, project),
                    File.Exists(target), selection.PokemonCenterEnabled
                        ? "Add Pokemon Center Hyper Training using the selected level and payment items."
                        : "Remove the added Pokemon Center Hyper Training service."));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, exception.Message, field: "pokemonCenterEnabled")); }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Hyper Training change plan preview contains {writes.Count:N0} target file(s).")));

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        projectWorkspaceService.ClearMemoryCache();
        try
        {
            return ApplyChangePlanCore(paths, session, reviewedPlan);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Hyper Training change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Hyper Training change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        beforeAcquireApplyScope?.Invoke();
        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var applyScope,
                out var acquireDiagnostics))
        {
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                acquireDiagnostics);
        }

        using var verifiedScope = applyScope!;
        var snapshotPlan = CreateChangePlan(verifiedScope.ApplyPaths, session);
        if (!verifiedScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan))
        {
            var staleDiagnostics = preparedPlan.Diagnostics.ToList();
            staleDiagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Hyper Training change plan"));
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                staleDiagnostics);
        }

        var snapshotResult = ApplyPreparedPlan(
            verifiedScope.ApplyPaths,
            session,
            preparedPlan,
            applyId,
            appliedAt);
        return verifiedScope.Commit(snapshotResult, beforeVerifiedPromotion);
    }

    private ApplyResult ApplyPreparedPlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan preparedPlan,
        string applyId,
        DateTimeOffset appliedAt)
    {
        projectWorkspaceService.ClearMemoryCache();
        var diagnostics = preparedPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        if (session.PendingEdits.Count != 1 || !IsCanonicalIdentity(session.PendingEdits[0]))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training prepared apply requires exactly one canonical pending edit.",
                expected: $"{HyperTrainingEditDomain}/{RecordId}/{MinimumLevelField}"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var selection = ParsePendingSelection(session.PendingEdits[0].NewValue, diagnostics);
        var minimumLevel = selection.MinimumLevel;
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var itemProject = projectWorkspaceService.Open(paths);
        IReadOnlyList<SwShHyperTrainingCenterWorkflow.Output> centerOutputs;
        try { centerOutputs = SwShHyperTrainingCenterWorkflow.Prepare(itemProject, selection); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, exception.Message, field: "pokemonCenterEnabled"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }
        var writeItems = NeedsItemWrites(itemProject, selection.Items);
        ApplyScript(paths, minimumLevel, selection.Items, writtenFiles, diagnostics);
        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            ApplyMain(paths, minimumLevel, selection.Items, writtenFiles, diagnostics);
        }

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            ApplyDialogue(paths, minimumLevel, preparedPlan, writtenFiles, diagnostics);
        }
        if (writeItems && !diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            ApplyItemDialogue(paths, selection.Items, writtenFiles, diagnostics);

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            try
            {
                foreach (var output in centerOutputs)
                {
                    var target = ResolveOutputPath(paths, output.Path, diagnostics)
                        ?? throw new InvalidDataException("Pokemon Center Hyper Training output target could not be resolved.");
                    if (output.Bytes.AsSpan().SequenceEqual(output.Baseline))
                    {
                        if (File.Exists(target)) File.Delete(target);
                    }
                    else WriteOutputAtomically(target, output.Bytes, _ => { });
                    writtenFiles.Add(new(ProjectFileLayer.Generated, output.Path));
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            { diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, exception.Message, field: "pokemonCenterEnabled")); }
        }


        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private void ApplyMain(
        ProjectPaths paths,
        int minimumLevel,
        SwShHyperTrainingItems items,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ExeFsMainPath, diagnostics);
        var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShHyperTrainingWorkflowService.ExeFsMainPath);
        if (source is null || targetPath is null || basePath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training picker source, vanilla base, or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var baseBytes = File.ReadAllBytes(basePath);
            SwShHyperTrainingMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, sourceBytes);
            var output = minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                ? SwShHyperTrainingMainPatcher.RestoreFromBase(
                    sourceBytes,
                    baseBytes,
                    paths.SelectedGame)
                : SwShHyperTrainingMainPatcher.ApplyMinimumLevel(
                    sourceBytes,
                    minimumLevel,
                    paths.SelectedGame);
            output = SwShHyperTrainingItemsPatcher.ApplyMain(output, paths.SelectedGame, items);
            if (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                && SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(output, baseBytes))
            {
                if (File.Exists(targetPath)) File.Delete(targetPath);
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => { VerifyMainOutput(roundTrip, minimumLevel, paths.SelectedGame); if (SwShHyperTrainingItemsPatcher.ReadMain(roundTrip, paths.SelectedGame) != items) throw new InvalidDataException("Hyper Training payment items did not round trip."); });
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training picker runtime changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training picker runtime could not be prepared safely: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Verified vanilla base, supported effective main, and writable output"));
        }
    }

    private void ApplyScript(
        ProjectPaths paths,
        int minimumLevel,
        SwShHyperTrainingItems items,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ScriptPath);
        var targetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ScriptPath, diagnostics);
        var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShHyperTrainingWorkflowService.ScriptPath);
        if (source is null || targetPath is null || basePath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training script source, vanilla base, or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Readable source and writable LayeredFS target"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            if (SwShHyperTrainingAmxPatcher.Analyze(baseBytes).Kind != SwShHyperTrainingScriptKind.NotInstalled)
            {
                throw new InvalidDataException("Hyper Training AMX base is not the verified vanilla Lv.100 script.");
            }

            var output = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(
                File.ReadAllBytes(source.AbsolutePath),
                minimumLevel);
            output = SwShHyperTrainingItemsPatcher.ApplyScript(output, items);
            if (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                && output.AsSpan().SequenceEqual(baseBytes))
            {
                if (File.Exists(targetPath)) File.Delete(targetPath);
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => { VerifyScriptOutput(roundTrip, minimumLevel); if (SwShHyperTrainingItemsPatcher.ReadScript(roundTrip) != items) throw new InvalidDataException("Hyper Training payment items did not round trip."); });
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.ScriptPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training script changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training script could not be prepared safely: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Verified vanilla base, known Hyper Training AMX shape, and writable output"));
        }
    }

    private void ApplyDialogue(
        ProjectPaths paths,
        int minimumLevel,
        ChangePlan currentPlan,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!currentPlan.Writes.Any(write => string.Equals(
                write.TargetRelativePath,
                SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        var targetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.EnglishDialoguePath, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training dialogue source or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                expected: "Readable source and writable LayeredFS target"));
            return;
        }

        try
        {
            var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(
                paths,
                SwShHyperTrainingWorkflowService.EnglishDialoguePath);
            byte[]? baseBytes = null;
            if (basePath is not null)
            {
                baseBytes = File.ReadAllBytes(basePath);
                if (SwShHyperTrainingDialoguePatcher.Analyze(baseBytes).Kind
                    != SwShHyperTrainingDialogueKind.NotInstalled)
                {
                    throw new InvalidDataException(
                        "English Hyper Training dialogue base is not the verified vanilla Lv.100 table.");
                }
            }

            var output = SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(
                File.ReadAllBytes(source.AbsolutePath),
                minimumLevel);
            if (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                && baseBytes is not null
                && output.AsSpan().SequenceEqual(baseBytes))
            {
                if (File.Exists(targetPath)) File.Delete(targetPath);
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => VerifyDialogueOutput(roundTrip, minimumLevel));
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.EnglishDialoguePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training dialogue changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training dialogue could not be prepared safely: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                expected: "Verified English Hyper Training dialogue and writable output"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShHyperTrainingWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (workflow.InstallStatus == "blocked")
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyper Training cannot stage while the script or picker runtime has an unsupported level-check shape.",
                    expected: "Known Hyper Training AMX level check and selected-game exefs/main picker checks"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool ValidateMinimumLevel(int minimumLevel, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (minimumLevel is >= SwShHyperTrainingAmxPatcher.MinimumAllowedLevel and <= SwShHyperTrainingAmxPatcher.MaximumAllowedLevel)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Hyper Training minimum level must be between {SwShHyperTrainingAmxPatcher.MinimumAllowedLevel} and {SwShHyperTrainingAmxPatcher.MaximumAllowedLevel}."),
            field: MinimumLevelField,
            expected: "Integer level 1-100"));
        return false;
    }

    private static SwShHyperTrainingSelection ParsePendingSelection(string? value, ICollection<ValidationDiagnostic> diagnostics)
    {
        try { return SwShHyperTrainingSelection.Parse(value); }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(SwShHyperTrainingItemWorkflow.Diagnostic(exception.Message));
            return new(100, SwShHyperTrainingItemsPatcher.Defaults);
        }
    }

    private static PendingEdit CreatePendingEdit(
        SwShHyperTrainingSelection selection,
        IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            HyperTrainingEditDomain,
            CreateStageSummary(selection),
            sources,
            RecordId,
            MinimumLevelField,
            selection.Payload);
    }

    private static bool IsCanonicalIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, MinimumLevelField, StringComparison.Ordinal);
    }

    private static void ValidateCanonicalEdit(
        OpenedProject project,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selection = ParsePendingSelection(edit.NewValue, diagnostics);
        try { SwShHyperTrainingItemWorkflow.ValidateSelection(selection.Items, new SwShHyperTrainingWorkflowService().Load(project)); }
        catch (InvalidDataException exception) { diagnostics.Add(SwShHyperTrainingItemWorkflow.Diagnostic(exception.Message)); }
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        var canonicalPayload = selection.Payload;
        if (!string.Equals(edit.NewValue, canonicalPayload, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Hyper Training minimum level is not in canonical integer format.",
                field: MinimumLevelField,
                expected: canonicalPayload));
        }

        var expectedSummary = CreateStageSummary(selection);
        if (!string.Equals(edit.Summary, expectedSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Hyper Training edit does not have the canonical staged summary.",
                field: MinimumLevelField,
                expected: expectedSummary));
        }

        var sourceDiagnostics = new List<ValidationDiagnostic>();
        var expectedSources = CreatePendingSources(project, selection, sourceDiagnostics);
        foreach (var diagnostic in sourceDiagnostics)
        {
            diagnostics.Add(diagnostic);
        }
        if (!SwShPendingSourceComparison.Matches(edit.Sources, expectedSources))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Hyper Training sources do not match the canonical base, effective, and payload references.",
                field: MinimumLevelField,
                expected: "Canonical ordered unique Hyper Training source references"));
        }
    }

    private static string CreateStageSummary(SwShHyperTrainingSelection selection)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{StageSummaryPrefix}{selection.MinimumLevel}.{(selection.Items == SwShHyperTrainingItemsPatcher.Defaults ? "" : $" Payment items {selection.Items.BottleCapItemId}/{selection.Items.GoldBottleCapItemId}.")}{(selection.PokemonCenterEnabled ? " Pokemon Center Hyper Training enabled." : "")}");
    }

    private static IReadOnlyList<ProjectFileReference> CreatePendingSources(
        OpenedProject project,
        SwShHyperTrainingSelection selection,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = new List<ProjectFileReference>();
        foreach (var relativePath in new[]
        {
            SwShHyperTrainingWorkflowService.ScriptPath,
            SwShHyperTrainingWorkflowService.ExeFsMainPath,
        })
        {
            var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, relativePath);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyper Training source could not be resolved.",
                    file: relativePath,
                    expected: "Readable current Hyper Training source"));
                continue;
            }

            AddBaseAndEffectiveSourceReferences(source.Entry, sources);
        }

        var dialogueSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(
            project,
            SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        if (dialogueSource is not null)
        {
            AddBaseAndEffectiveSourceReferences(dialogueSource.Entry, sources);
        }

        AddItemSources(project, selection.Items, sources);
        try { AddCenterSources(project, selection, sources); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        { diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, exception.Message, field: "pokemonCenterEnabled")); }
        sources.Add(CreatePendingPayloadSource(selection));
        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ProjectFileReference> CreatePlanSources(
        ProjectFileGraphEntry entry,
        SwShHyperTrainingSelection selection, OpenedProject project)
    {
        var sources = new List<ProjectFileReference>();
        AddBaseAndEffectiveSourceReferences(entry, sources);
        AddItemSources(project, selection.Items, sources);
        AddCenterSources(project, selection, sources);
        sources.Add(CreatePendingPayloadSource(selection));
        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddBaseAndEffectiveSourceReferences(
        ProjectFileGraphEntry entry,
        ICollection<ProjectFileReference> sources)
    {
        if (entry.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, entry.RelativePath));
        }

        if (entry.LayeredFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, entry.RelativePath));
        }
    }

    private static ProjectFileReference CreatePendingPayloadSource(SwShHyperTrainingSelection selection)
    {
        var payload = selection.Payload;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/hyper-training/minimum-level/{hash}");
    }

    private static bool NeedsItemWrites(OpenedProject project, SwShHyperTrainingItems items)
    {
        if (items != SwShHyperTrainingItemsPatcher.Defaults) return true;
        var script = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ScriptPath);
        var main = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ExeFsMainPath);
        return script is not null && SwShHyperTrainingItemsPatcher.ReadScript(File.ReadAllBytes(script.AbsolutePath)) != items
            || main is not null && SwShHyperTrainingItemsPatcher.ReadMain(File.ReadAllBytes(main.AbsolutePath), project.Paths.SelectedGame) != items
            || SwShHyperTrainingItemDialogue.HasGeneratedDialogue(project);
    }

    private static void AddCenterSources(OpenedProject project, SwShHyperTrainingSelection selection, ICollection<ProjectFileReference> sources)
    {
        if (!selection.PokemonCenterEnabled && !SwShHyperTrainingCenterWorkflow.IsEnabled(project)) return;
        foreach (var path in SwShHyperTrainingCenterWorkflow.Dependencies(project))
        {
            var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path);
            if (source is not null) AddBaseAndEffectiveSourceReferences(source.Entry, sources);
        }
    }

    private static void AddItemSources(OpenedProject project, SwShHyperTrainingItems items, ICollection<ProjectFileReference> sources)
    {
        if (!NeedsItemWrites(project, items)) return;
        var paths = SwShHyperTrainingItemDialogue.Targets(project).ToArray();
        foreach (var path in paths.Concat(paths.Select(SwShHyperTrainingItemDialogue.NamesPath)).Append(SwShItemsWorkflowService.ItemDataPath))
        {
            var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path);
            if (source is not null) AddBaseAndEffectiveSourceReferences(source.Entry, sources);
        }
    }

    private void ApplyItemDialogue(ProjectPaths paths, SwShHyperTrainingItems items,
        ICollection<ProjectFileReference> writtenFiles, ICollection<ValidationDiagnostic> diagnostics)
    {
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        foreach (var path in SwShHyperTrainingItemDialogue.Targets(project))
        {
            try
            {
                var baseline = File.ReadAllBytes(SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, path)!);
                var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, path)!;
                var source = File.Exists(target) ? File.ReadAllBytes(target) : baseline;
                var names = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingItemDialogue.NamesPath(path));
                var output = SwShHyperTrainingItemDialogue.Apply(source, baseline,
                    names is null ? null : File.ReadAllBytes(names.AbsolutePath), path, items);
                if (output.AsSpan().SequenceEqual(baseline))
                {
                    if (File.Exists(target)) File.Delete(target);
                }
                else WriteOutputAtomically(target, output, bytes => _ = KM.Formats.SwSh.SwShGameTextFile.Parse(bytes));
                var reference = new ProjectFileReference(ProjectFileLayer.Generated, path);
                if (!writtenFiles.Contains(reference)) writtenFiles.Add(reference);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
            { diagnostics.Add(SwShHyperTrainingItemWorkflow.Diagnostic(exception.Message)); return; }
        }
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static void VerifyMainOutput(byte[] output, int minimumLevel, ProjectGame? selectedGame)
    {
        var analysis = SwShHyperTrainingMainPatcher.Analyze(output, selectedGame);
        var expectedKind = minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
            ? SwShHyperTrainingMainKind.NotInstalled
            : SwShHyperTrainingMainKind.CustomMinimumLevel;
        if (analysis.Kind != expectedKind || analysis.MinimumLevel != minimumLevel)
        {
            throw new InvalidDataException(
                "Hyper Training main output did not round-trip with the reviewed cutoff.");
        }
    }

    private static void VerifyScriptOutput(byte[] output, int minimumLevel)
    {
        if (SwShHyperTrainingAmxPatcher.ReadMinimumLevel(output) != minimumLevel)
        {
            throw new InvalidDataException(
                "Hyper Training AMX output did not round-trip with the reviewed cutoff.");
        }
    }

    private static void VerifyDialogueOutput(byte[] output, int minimumLevel)
    {
        var analysis = SwShHyperTrainingDialoguePatcher.Analyze(output);
        if (analysis.Kind == SwShHyperTrainingDialogueKind.Conflict
            || analysis.MinimumLevel != minimumLevel)
        {
            throw new InvalidDataException(
                "Hyper Training dialogue output did not round-trip with the reviewed cutoff.");
        }
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Hyper Training output directory could not be resolved.");
        Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, output);
            var roundTrip = File.ReadAllBytes(temporaryPath);
            if (!roundTrip.AsSpan().SequenceEqual(output))
            {
                throw new IOException("Hyper Training temporary output did not round-trip byte-for-byte.");
            }

            verifyRoundTrip(roundTrip);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static ApplyResult CreateApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: HyperTrainingEditDomain,
            Field: field,
            Expected: expected);
    }
}
