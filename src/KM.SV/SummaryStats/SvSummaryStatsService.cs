// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.SummaryStats;

public sealed record SvSummaryStatsWorkflow(SvWorkflowSummary Summary, string SourceRevision,
    bool Enabled, bool AppliedEnabled, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SvSummaryStatsEditResult(SvSummaryStatsWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed class SvSummaryStatsService(ProjectWorkspaceService projects)
{
    internal const string Domain = "workflow.summaryStats";
    private static readonly string[] Paths = [SvSummaryStatsScript.VirtualPath, .. SvSummaryStatsMessages.VirtualPaths];
    private readonly SvWorkflowFileSource files = new(bypassReusableBaseCache: true, maximumReadBytes: 32 * 1024 * 1024);
    private sealed record Intent(string Revision, [property: JsonRequired] bool Previous,
        [property: JsonRequired] bool Enabled, IReadOnlyList<string> Hashes);
    private sealed record Source(IReadOnlyDictionary<string, SvWorkflowFile> Files, string Revision, SvSharedScriptState Script,
        IReadOnlyList<ProjectFileReference> References, IReadOnlyList<string> Hashes, IReadOnlyDictionary<string, byte[]> BaseKeys)
    {
        public bool Enabled => Script.SummaryStats;
    }
    private sealed record Output(string Path, byte[] Bytes);

    internal SvWorkflowSummary CreateSummary(OpenedProject project) => SvWorkflowSupport.CreateSummary(project,
        "summaryStats", "IV and EV Screen", "Show raw IVs and numeric EVs on the Pokemon Summary screen.");

    private Source Read(ProjectPaths paths)
    {
        var project = projects.Open(paths);
        var inputs = Paths.Select(path => files.Read(project, path)).ToArray();
        var references = inputs.Select(file => new ProjectFileReference(file.SourceLayer, file.RelativePath)).ToList();
        var hashes = inputs.Select(file => Convert.ToHexString(SHA256.HashData(file.Bytes))).ToList();
        var baseKeys = new Dictionary<string, byte[]>();
        foreach (var path in SvSummaryStatsMessages.VirtualPaths.Where(path => path.EndsWith(".tbl", StringComparison.Ordinal)))
        {
            var original = files.ReadBaseBytesFresh(paths, path);
            SvSummaryStatsMessages.ValidateBaseKeys(original);
            baseKeys.Add(path, original);
            hashes.Add(Convert.ToHexString(SHA256.HashData(original)));
            references.Add(new(ProjectFileLayer.Base, "romfs/" + path));
        }
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(';', hashes))));
        return new(Paths.Zip(inputs).ToDictionary(pair => pair.First, pair => pair.Second), revision,
            SvSharedScript.Read(inputs[0].Bytes), references, hashes, baseKeys);
    }

    private static SvSummaryStatsWorkflow Project(Source source, SvWorkflowSummary summary, EditSession? session = null)
    {
        var diagnostics = summary.Diagnostics.ToList();
        var enabled = source.Enabled;
        foreach (var edit in (session?.PendingEdits ?? []).Where(edit => edit.Domain == Domain))
            if (Resolve(source, RebindMissing(edit, source), diagnostics, out var desired)) enabled = desired;
        return new(summary, source.Revision, enabled, source.Enabled, diagnostics);
    }

    public SvSummaryStatsWorkflow Load(ProjectPaths paths, EditSession? session = null)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var summary = CreateSummary(projects.Open(paths));
        var diagnostics = summary.Diagnostics.ToList();
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame) || summary.Availability == SvWorkflowAvailability.Disabled)
            return new(summary, "", false, false, diagnostics);
        try
        {
            return Project(Read(paths), summary, session);
        }
        catch (Exception exception) when (SourceFailure(exception))
        {
            diagnostics.Add(Error("The Summary script or localized messages are unavailable or unsupported. Check Base RomFS and reload.", "source"));
            return new(summary with { Availability = SvWorkflowAvailability.Disabled }, "", false, false, diagnostics);
        }
    }

    public SvSummaryStatsEditResult Stage(ProjectPaths paths, EditSession? session, string revision, bool enabled)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var project = projects.Open(paths);
        var summary = CreateSummary(project);
        var workflow = new SvSummaryStatsWorkflow(summary, "", false, false, summary.Diagnostics);
        try
        {
            if (!SvEditSessionSupport.CanEdit(project, summary, summary.Diagnostics, Domain, diagnostics))
                return new(workflow, current, diagnostics);
            var source = Read(paths);
            workflow = Project(source, summary, current);
            if (!SvEditSessionSupport.CanEdit(project, summary, workflow.Diagnostics, Domain, diagnostics))
                return new(workflow, current, diagnostics);
            if (revision != source.Revision) { diagnostics.Add(Error("Summary sources changed. Reload before staging.", "sourceRevision")); return new(workflow, current, diagnostics); }
            var edit = new PendingEdit(Domain, enabled ? "Enable numeric Summary IV and EV views." : "Restore the original Summary views.",
                source.References, "summary", "enabled", JsonSerializer.Serialize(new Intent(source.Revision, source.Enabled, enabled, source.Hashes)));
            // Reapplying the same choice also repairs deleted companion output.
            var staged = current with { PendingEdits = [.. current.PendingEdits.Where(edit => edit.Domain != Domain), edit] };
            return new(Project(source, summary, staged), staged, diagnostics);
        }
        catch (Exception exception) when (SourceFailure(exception))
        { diagnostics.Add(Error("Summary changes could not be staged. Reload and try again.", "source")); return new(workflow, current, diagnostics); }
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            return Validate(paths, session, Read(paths));
        }
        catch (Exception exception) when (SourceFailure(exception)) { diagnostics.Add(Error("Summary sources could not be verified. Reload and stage again.", "source")); }
        return new(session, diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), diagnostics);
    }

    private SvEditSessionValidation Validate(ProjectPaths paths, EditSession session, Source source)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var project = projects.Open(paths);
        var summary = CreateSummary(project);
        SvEditSessionSupport.CanEdit(project, summary, summary.Diagnostics, Domain, diagnostics);
        if (session.PendingEdits.Count != 1) diagnostics.Add(Error("Stage one Summary screen setting before review.", "session"));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return new(session, false, diagnostics);
        session = session with { PendingEdits = session.PendingEdits.Select(edit => RebindMissing(edit, source)).ToArray() };
        foreach (var edit in session.PendingEdits) Resolve(source, edit, diagnostics, out _);
        return new(session, diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), diagnostics);
    }

    private IReadOnlyList<Output> Prepare(Source source, EditSession session, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Count != 1 || !Resolve(source, session.PendingEdits[0], diagnostics, out var enabled)) return [];
        var script = source.Enabled == enabled && !source.Script.SummaryStatsNeedsUpdate ? source.Files[SvSummaryStatsScript.VirtualPath].Bytes
            : SvSharedScript.Compose(source.Script with { SummaryStats = enabled });
        var outputs = new List<Output> { new(SvSummaryStatsScript.VirtualPath, script) };
        foreach (var language in SvGameTextLanguage.SupportedMessageLanguages)
        {
            var stem = $"message/dat/{language}/common/status";
            var data = source.Files[stem + ".dat"];
            var keys = source.Files[stem + ".tbl"];
            var paired = SvSummaryStatsMessages.RepairMissingCompanion(language, data.Bytes, keys.Bytes, source.BaseKeys[stem + ".tbl"],
                data.SourceLayer == ProjectFileLayer.Base, keys.SourceLayer == ProjectFileLayer.Base);
            var transformed = SvSummaryStatsMessages.Transform(language, paired.Data, paired.Keys, enabled);
            outputs.Add(new(stem + ".dat", transformed.Data)); outputs.Add(new(stem + ".tbl", transformed.Keys));
            if (transformed.PreservedCustomText) diagnostics.Add(new ValidationDiagnostic(DiagnosticSeverity.Warning,
                "Custom Summary captions were preserved. Review their wording for the selected display modes.", "romfs/" + stem + ".dat", Domain, "captions")
                { Code = "KM-SV-SUMMARY-STATS-TEXT-PRESERVED" });
        }
        return outputs;
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, SvOutputMode mode)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        try { return CreateChangePlan(paths, session, mode, Read(paths)); }
        catch (Exception exception) when (SourceFailure(exception))
        { return new(session.Id, [], [Error("Summary sources could not be verified. Reload and stage again.", "source")]); }
    }

    private ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, SvOutputMode mode, Source source)
    {
        var validation = Validate(paths, session, source); var diagnostics = validation.Diagnostics.ToList(); var writes = new List<PlannedFileWrite>();
        if (validation.IsValid) try
        {
            foreach (var output in Prepare(source, validation.Session, diagnostics))
            {
                var info = SvWorkflowFileSource.CreatePlannedWrite(paths, output.Path, source.References, mode);
                writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Apply Summary display modes and captions."));
            }
            if (mode == SvOutputMode.Standalone)
            { var info = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths); writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Register Summary screen output.")); }
        }
        catch (Exception exception) when (SourceFailure(exception)) { diagnostics.Add(Error("Summary output could not be prepared.", "output")); }
        return SvChangePlanSourceGuard.Capture(paths, validation.Session, new ChangePlan(session.Id, writes, diagnostics) { EffectivePendingEdits = validation.Session.PendingEdits }, mode);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, SvOutputMode mode)
    {
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths); using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        Source source;
        try { source = Read(paths); }
        catch (Exception exception) when (SourceFailure(exception))
        { return SvEditSessionSupport.CreateApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, reviewed, [],
            [Error("Summary sources could not be verified. Reload and stage again.", "source")]); }
        var current = CreateChangePlan(paths, session, mode, source); var diagnostics = current.Diagnostics.ToList(); var written = new List<ProjectFileReference>();
        if (!ChangePlanReview.Matches(reviewed, current)) diagnostics.Add(Error("The Summary review is stale. Review Changes again.", "review"));
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error)) try
        {
            var outputs = Prepare(source, session with { PendingEdits = current.EffectivePendingEdits ?? session.PendingEdits }, diagnostics);
            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                foreach (var output in outputs) { SvWorkflowFileSource.Write(paths, output.Path, output.Bytes, mode); written.Add(SvEditSessionSupport.GeneratedReference(output.Path, mode)); }
                if (mode == SvOutputMode.Standalone) written.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }
        }
        catch (Exception exception) when (SourceFailure(exception)) { diagnostics.Add(Error("Summary output could not be written. Review the output status before retrying.", "output")); }
        return SvEditSessionSupport.CreateApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, current, written, diagnostics);
    }

    // The mixed batch has already authenticated every original source before this rebind.
    internal EditSession RebindVerifiedBatch(ProjectPaths paths, EditSession session)
    {
        var source = Read(paths);
        return session with { PendingEdits = session.PendingEdits.Select(edit => Rebind(edit, source)).ToArray() };
    }
    private static PendingEdit Rebind(PendingEdit edit, Source source)
    {
        var intent = Parse(edit) ?? throw new InvalidDataException("Invalid Summary setting.");
        return edit with { Sources = source.References, NewValue = JsonSerializer.Serialize(intent with { Revision = source.Revision, Previous = source.Enabled, Hashes = source.Hashes }) };
    }
    private static PendingEdit RebindMissing(PendingEdit edit, Source source)
    {
        var intent = Parse(edit);
        if (intent is null || edit.Sources.Count != source.References.Count || intent.Hashes.Count != source.Hashes.Count) return edit;
        var removed = edit.Sources.Zip(source.References).Select(pair => pair.First.Layer == ProjectFileLayer.Layered
            && pair.Second.Layer == ProjectFileLayer.Base && pair.First.RelativePath == pair.Second.RelativePath).ToArray();
        return removed.Any(value => value) && Enumerable.Range(0, removed.Length).All(i => removed[i]
            || edit.Sources[i] == source.References[i] && intent.Hashes[i] == source.Hashes[i]) ? Rebind(edit, source) : edit;
    }
    private static bool Resolve(Source source, PendingEdit edit, ICollection<ValidationDiagnostic> diagnostics, out bool enabled)
    {
        enabled = false; var intent = Parse(edit);
        if (edit.Domain != Domain || edit.RecordId != "summary" || edit.Field != "enabled" || intent is null
            || intent.Revision != source.Revision || intent.Previous != source.Enabled || !intent.Hashes.SequenceEqual(source.Hashes)
            || !edit.Sources.SequenceEqual(source.References))
        { diagnostics.Add(Error("The staged Summary setting is invalid or its source changed. Reload and stage again.", "sourceRevision")); return false; }
        enabled = intent.Enabled; return true;
    }
    private static Intent? Parse(PendingEdit edit)
    {
        if (edit.NewValue is not { Length: <= 6000 }) return null;
        try { return JsonSerializer.Deserialize<Intent>(edit.NewValue) is { Hashes.Count: 31 } intent ? intent : null; }
        catch (JsonException) { return null; }
    }
    private static bool SourceFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or OverflowException or KeyNotFoundException;
    private static ValidationDiagnostic Error(string message, string field) => new(DiagnosticSeverity.Error, message,
        "romfs/" + SvSummaryStatsScript.VirtualPath, Domain, field)
    { Code = field switch { "sourceRevision" or "review" => "KM-SV-SUMMARY-STATS-SOURCE-STALE", "source" => "KM-SV-SUMMARY-STATS-SOURCE-UNAVAILABLE",
        "output" => "KM-SV-SUMMARY-STATS-OUTPUT-FAILED", _ => "KM-SV-SUMMARY-STATS-EDIT-INVALID" } };
}
