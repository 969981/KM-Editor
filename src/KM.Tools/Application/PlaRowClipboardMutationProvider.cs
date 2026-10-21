// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.PLA;

namespace KM.Tools.Application;

internal static class PlaRowClipboardMutationProvider
{
    public static string CaptureSourceFingerprint(ProjectPaths paths) => new PlaWorkflowService().CaptureRowClipboardSourceFingerprint(paths);

    public static RowClipboardMutationResult Mutate(ProjectPaths paths, EditSession session, RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode, RowClipboardPasteTarget target)
    {
        var kind = envelope.Editor == RowClipboardAdapterCatalog.PlaLearnset.Editor ? "learnsets"
            : envelope.Editor == RowClipboardAdapterCatalog.PlaTrainerParty.Editor ? "trainers"
            : envelope.Editor == RowClipboardAdapterCatalog.PlaEncounterSlot.Editor ? "encounters"
            : throw new InvalidDataException("The PLA clipboard schema is unavailable.");
        if (paths.SelectedGame != ProjectGame.PLA || string.IsNullOrEmpty(target.TableId)
            || target.Kind != envelope.Editor.RowKind || target.PersonalId is not null || target.TrainerId is not null)
            throw new InvalidDataException("The PLA clipboard target is invalid.");
        var service = new PlaWorkflowService();
        var page = service.Load(paths, session, kind, query: target.TableId);
        var record = page.Records.SingleOrDefault(record => record.Id == target.TableId)
            ?? throw new InvalidDataException("The PLA clipboard record is unavailable.");
        var previews = new List<RowClipboardMutationPreviewRow>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (kind == "learnsets")
        {
            var rows = JsonSerializer.Deserialize<ushort[][]>(record.Values["entries"])!.ToList();
            var start = mode == RowClipboardPasteMode.Append && target.Slot is null ? rows.Count
                : mode == RowClipboardPasteMode.Replace && target.Slot is { } slot ? slot
                : throw new InvalidDataException("The PLA learnset paste mode is invalid.");
            if (start < 0 || start > rows.Count || mode == RowClipboardPasteMode.Replace && start + envelope.Rows.Length > rows.Count)
                throw new InvalidDataException("The PLA learnset paste exceeds the target rows.");
            foreach (var (row, offset) in envelope.Rows.Select((row, index) => (row, index)))
            {
                var fields = row.Values.ToDictionary(value => value.FieldKey, StringComparer.Ordinal);
                if (fields.Count != 3 || new[] { "level", "mastery", "move" }.Any(key => !fields.ContainsKey(key)))
                    throw new InvalidDataException("The PLA learnset row is incomplete.");
                var after = new[] { "level", "mastery", "move" }.Select(key => fields[key].Value is RowClipboardUnsignedIntegerValue value
                    ? checked((ushort)value.Value) : throw new InvalidDataException("The PLA learnset value is invalid.")).ToArray();
                var index = start + offset;
                var before = index < rows.Count ? LearnsetValues(rows[index]) : [];
                if (index < rows.Count) rows[index] = after; else rows.Add(after);
                previews.Add(new(new(envelope.Editor.RowKind, target.TableId + ":slot:" + index.ToString(CultureInfo.InvariantCulture)), before, LearnsetValues(after)));
            }
            values["entries"] = JsonSerializer.Serialize(rows);
        }
        else
        {
            if (mode != RowClipboardPasteMode.Replace || target.Slot is not null || envelope.Rows.Length != 1)
                throw new InvalidDataException("The PLA record paste mode is invalid.");
            var before = new List<RowClipboardOwnedValue>();
            foreach (var owned in envelope.Rows[0].Values)
            {
                var field = page.Fields.SingleOrDefault(field => field.Key == owned.FieldKey);
                if (field is null || field.ReadOnly || record.ReadOnlyFields.Contains(field.Key))
                    throw new InvalidDataException("The PLA paste contains a protected field.");
                values[field.Key] = owned.Value switch
                {
                    RowClipboardSignedIntegerValue number when field.Kind != "decimal" => number.CanonicalValue,
                    RowClipboardDecimalValue number when field.Kind == "decimal" => number.CanonicalValue,
                    _ => throw new InvalidDataException("The PLA clipboard value type is invalid.")
                };
                before.Add(new(field.Key, field.Kind == "decimal" ? new RowClipboardDecimalValue(CanonicalDecimal(record.Values[field.Key]))
                    : new RowClipboardSignedIntegerValue(record.Values[field.Key])));
            }
            previews.Add(new(new(envelope.Editor.RowKind, target.TableId), before, envelope.Rows[0].Values));
        }
        var staged = service.Stage(paths, session, kind, record.Id, values, page.Revision);
        return new(staged, [], previews);
    }

    private static IReadOnlyList<RowClipboardOwnedValue> LearnsetValues(ushort[] row) =>
        new[] { "level", "mastery", "move" }.Select((key, index) => new RowClipboardOwnedValue(key,
            new RowClipboardUnsignedIntegerValue(row[index].ToString(CultureInfo.InvariantCulture)))).ToArray();

    private static string CanonicalDecimal(string value) => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture).ToString("0.############################", CultureInfo.InvariantCulture);
}
