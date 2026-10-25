// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Pokemon;

public sealed partial class SwShPokemonEditSessionService
{
    private static SwShPokemonEditResult SyncMegaLearnset(
        SwShPokemonWorkflow workflow,
        EditSession session,
        SwShPokemonRecord target,
        SwShPokemonWorkflowService.WorkflowFileSource source,
        List<ValidationDiagnostic> diagnostics)
    {
        var baseForm = SwShMegaLearnsetSync.BaseForm(target);
        var bases = workflow.Pokemon.Where(pokemon => baseForm is not null
            && pokemon.SpeciesId == target.SpeciesId && pokemon.CanonicalForm == baseForm
            && !pokemon.Personal.IsRegionalForm && pokemon.PersonalId != target.PersonalId).ToArray();
        if (bases.Length != 1)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error,
                baseForm is null
                    ? "Sync Learnset is only available for restored Mega forms, excluding regional variants."
                    : "The corresponding non-Mega form could not be uniquely resolved for Sync Learnset.",
                field: "learnset", expected: "A restored Mega with one corresponding non-Mega form"));
            return new(workflow, session, diagnostics);
        }

        // Snapshot the effective base, including staged edits. Ordinary row operations
        // keep preview, Apply, history and file validation on the existing path.
        var moves = bases[0].Learnset.ToArray();
        var workingWorkflow = workflow;
        var workingSession = session;
        var workingTarget = target;
        bool Stage(string action, int slot, int? moveId = null, int? level = null)
        {
            var edit = CreateLearnsetPendingEdit(workingWorkflow, workingTarget, source,
                action, slot, moveId, level, diagnostics);
            if (edit is null) return false;
            workingSession = ReplacePendingPokemonEdit(workingSession, edit);
            workingWorkflow = OverlayPendingEdits(workingWorkflow, [edit]);
            workingTarget = workingWorkflow.Pokemon.Single(pokemon => pokemon.PersonalId == target.PersonalId);
            return true;
        }

        for (var slot = workingTarget.Learnset.Count - 1; slot >= moves.Length; slot--)
            if (!Stage(LearnsetRemoveAction, slot)) return new(workflow, session, diagnostics);

        for (var slot = 0; slot < moves.Length; slot++)
        {
            var move = moves[slot];
            if (slot < workingTarget.Learnset.Count
                && workingTarget.Learnset[slot].MoveId == move.MoveId
                && workingTarget.Learnset[slot].Level == move.Level) continue;
            if (!Stage(LearnsetUpsertAction, slot, move.MoveId, move.Level))
                return new(workflow, session, diagnostics);
        }

        return new(workingWorkflow, workingSession, diagnostics);
    }
}
