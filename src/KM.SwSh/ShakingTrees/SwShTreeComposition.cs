// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Formats.SwSh;
using KM.SwSh.Encounters;
using KM.SwSh.Placement;

namespace KM.SwSh.ShakingTrees;

internal static class SwShTreeComposition
{
    internal static IReadOnlyList<SwShPersonalRecord> Pokemon(IReadOnlyList<SwShPersonalRecord> source, EditSession? session)
    {
        var records = source.ToArray();
        foreach (var edit in session?.PendingEdits ?? [])
        {
            if (edit.Domain != "workflow.pokemon" || !int.TryParse(edit.RecordId, out var id) || id < 0 || id >= records.Length
                || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) continue;
            records[id] = edit.Field switch
            {
                "isPresentInGame" when value is 0 or 1 => records[id] with { IsPresentInGame = value == 1 },
                "formCount" when value is >= 0 and <= 255 => records[id] with { FormCount = value },
                "formStatsIndex" when value is >= 0 and <= ushort.MaxValue => records[id] with { FormStatsIndex = value },
                _ => records[id],
            };
        }
        return records;
    }

    internal static Dictionary<string, int> Overlay(SwShTreeSources state, EditSession session,
        Dictionary<string, int> ownValues, List<ValidationDiagnostic> diagnostics)
    {
        var values = new Dictionary<string, int>(ownValues, StringComparer.Ordinal);
        var owned = session.PendingEdits.Where(edit => edit.Domain == SwShShakingTreesService.Domain)
            .Select(edit => SwShTreeSources.Key(edit.RecordId ?? "", edit.Field ?? "")).ToHashSet(StringComparer.Ordinal);
        foreach (var edit in session.PendingEdits)
        {
            var key = Key(state, edit);
            if (key is null || !state.Cells.ContainsKey(key)) continue;
            if (!int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || owned.Contains(key) && ownValues[key] != value)
            {
                diagnostics.Add(new(DiagnosticSeverity.Error,
                    "The same tree field has conflicting staged values. Keep one value before reviewing.",
                    Domain: SwShShakingTreesService.Domain, Field: key) { Code = SwShShakingTreesService.ConflictCode });
                continue;
            }
            values[key] = value;
        }
        return values;
    }

    private static string? Key(SwShTreeSources state, PendingEdit edit)
    {
        if (edit.Domain == "workflow.encounters"
            && SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            && SwShEncountersWorkflowService.TryParseTableId(tableId, out var member, out var tableIndex, out var zoneId, out var subTable)
            && member.FileName == state.EncounterMember && subTable == 9)
        {
            var area = state.Areas.SingleOrDefault(row => row.Table == tableIndex && row.Id == $"area:{zoneId:X16}");
            if (area is null || slot > area.Slots) return null;
            var field = edit.Field switch
            {
                "speciesId" => $"species.{slot - 1}", "form" => $"form.{slot - 1}", "probability" => $"percent.{slot - 1}",
                "levelMin" => "levelMin", "levelMax" => "levelMax", _ => null,
            };
            return field is null ? null : SwShTreeSources.Key(area.Id, field);
        }
        if (edit.Domain == "workflow.placement" && edit.RecordId is not null
            && SwShPlacementWorkflowService.TryParseObjectRecordId(edit.RecordId, out var archive, out var zone, out var type, out var index, out _)
            && type == "BerryTree")
        {
            var tree = state.Trees.SingleOrDefault(row => row.Member == archive && row.Zone == zone && row.Object == index);
            if (tree is null) return null;
            for (var itemSlot = 0; itemSlot < tree.Slots; itemSlot++)
                if (edit.Field == $"raw.BerryTree.Field_01[{itemSlot}].Rate") return SwShTreeSources.Key(tree.Id, $"percent.{itemSlot}");
        }
        return null;
    }
}
