// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SV.TypeChart;
using KM.SV.Workflows;

namespace KM.SV.Moves;

internal static class SvTeraBlastWorkflow
{
    internal const string MainPath = "exefs/main";
    internal const string UnavailableCode = "KM-SV-MOVES-TERA-BLAST-UNAVAILABLE";

    internal static SvMoveRecord[] Attach(OpenedProject project, SvMoveRecord[] moves)
    {
        var index = Array.FindIndex(moves, move => move.MoveId == 851);
        if (index < 0) return moves;
        try
        {
            var source = SvTypeChartWorkflowService.ResolveWorkflowFile(project, MainPath)
                ?? throw new InvalidDataException("Tera Blast settings require a readable executable.");
            var settings = SvTeraBlastMainPatcher.Read(File.ReadAllBytes(source.AbsolutePath), project.Paths.SelectedGame);
            moves[index] = moves[index] with
            {
                TeraBlast = settings,
                TeraBlastProvenance = new(MainPath,
                    source.Entry.LayeredFile is null ? ProjectFileLayer.Base : ProjectFileLayer.Generated, source.Entry.State),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            moves[index] = moves[index] with { TeraBlastBlockedReason = exception.Message };
        }
        return moves;
    }

    internal static bool CanEdit(SvMoveRecord move, string field, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SvTeraBlastSettings.IsField(field)) return true;
        if (move.MoveId == 851 && move.TeraBlast is not null) return true;
        diagnostics.Add(Diagnostic(UnavailableCode, move.MoveId != 851
            ? "Tera Blast settings can only be staged for move 851."
            : move.TeraBlastBlockedReason ?? "Tera Blast settings are unavailable.", field));
        return false;
    }

    internal static ValidationDiagnostic Diagnostic(string code, string message, string? field = null) =>
        new(DiagnosticSeverity.Error, message, MainPath, SvEditSessionSupport.MovesDomain, field,
            "Supported Scarlet/Violet Tera Blast settings") { Code = code };

    internal static PlannedFileWrite Plan(ProjectPaths paths, EditSession session)
    {
        var target = SvTypeChartWorkflowService.ResolveOutputPath(paths, MainPath)
            ?? throw new InvalidDataException("Tera Blast output requires a valid output root.");
        return new(MainPath,
            session.PendingEdits.Where(edit => SvTeraBlastSettings.IsField(edit.Field)).SelectMany(edit => edit.Sources).Distinct().ToArray(),
            File.Exists(target), "Update Tera Blast battle behavior and matching previews.");
    }

    internal static void Apply(ProjectPaths paths, OpenedProject project, EditSession session, ChangePlan plan,
        SvOutputMode outputMode)
    {
        var source = SvTypeChartWorkflowService.ResolveWorkflowFile(project, MainPath)
            ?? throw new InvalidDataException("Tera Blast executable is missing. Review the changes again.");
        var bytes = File.ReadAllBytes(source.AbsolutePath);
        var settings = SvTeraBlastMainPatcher.Read(bytes, paths.SelectedGame);
        foreach (var edit in session.PendingEdits.Where(edit => SvTeraBlastSettings.IsField(edit.Field)))
            settings = settings.With(edit.Field!, int.Parse(edit.NewValue!, System.Globalization.CultureInfo.InvariantCulture));
        var output = SvTeraBlastMainPatcher.Apply(bytes, settings, paths.SelectedGame);
        var context = new SvOutputApplyContext(OutputReviewFingerprint.FromChangePlan(plan),
            new OwnershipOwnerId(SvEditSessionSupport.MovesDomain),
            [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, SvEditSessionSupport.MovesDomain)])
        { HistoryDetails = OutputHistoryDetails.Capture(session.PendingEdits) };
        SvWorkflowFileSource.ApplyExecutableOutputBatch(paths, output, outputMode, context);
    }
}
