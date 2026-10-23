// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.Pokemon;

public sealed partial class SwShPokemonEditSessionService
{
    public SwShPokemonEditResult StageHeldItemRepairs(
        ProjectPaths paths, EditSession? previousSession, SwShPokemonEditResult staged)
    {
        if (staged.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            || staged.Session.PendingEdits.SequenceEqual(previousSession?.PendingEdits ?? [])) return staged;

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = SwShPokemonWorkflowService.ResolvePersonalDataSource(project)
                ?? throw new InvalidDataException("Pokemon personal data is unavailable.");
            var records = SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
            var pending = staged.Session.PendingEdits.Where(IsPokemonDomainEdit)
                .Select(edit => (edit.RecordId, edit.Field)).ToHashSet();
            var updates = new List<SwShPokemonFieldUpdate>();
            foreach (var record in records)
            {
                Add(record.PersonalId, SwShPokemonWorkflowService.HeldItem1Field, record.HeldItem1);
                Add(record.PersonalId, SwShPokemonWorkflowService.HeldItem2Field, record.HeldItem2);
                Add(record.PersonalId, SwShPokemonWorkflowService.HeldItem3Field, record.HeldItem3);
            }
            if (updates.Count == 0) return staged;

            var repaired = UpdateFields(paths, staged.Session, updates);
            if (repaired.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return RollBack(repaired.Diagnostics);
            return repaired with { Diagnostics = staged.Diagnostics.Concat(repaired.Diagnostics)
                .Append(new ValidationDiagnostic(DiagnosticSeverity.Info,
                    "Corrections to invalid held item values have been staged as None. Review and apply them with your Pokemon edits.",
                    File: SwShPokemonWorkflowService.PersonalDataPath, Domain: PokemonEditDomain)
                    { Code = "KM-SWSH-POKEMON-HELD-ITEMS-REPAIR-STAGED" }).ToArray() };

            void Add(int personalId, string field, int value)
            {
                if (value == -1 && !pending.Contains((personalId.ToString(CultureInfo.InvariantCulture), field)))
                    updates.Add(new(personalId, field, "0"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return RollBack([new ValidationDiagnostic(DiagnosticSeverity.Error,
                "Held item corrections could not be staged. Refresh Pokemon data and try again.",
                File: SwShPokemonWorkflowService.PersonalDataPath, Domain: PokemonEditDomain)
                { Code = "KM-SWSH-POKEMON-HELD-ITEMS-REPAIR-FAILED" }]);
        }

        SwShPokemonEditResult RollBack(IReadOnlyList<ValidationDiagnostic> diagnostics)
        {
            var session = previousSession ?? staged.Session with { PendingEdits = [] };
            return staged with { Session = session, Diagnostics = staged.Diagnostics.Concat(diagnostics).ToArray() };
        }
    }
}
