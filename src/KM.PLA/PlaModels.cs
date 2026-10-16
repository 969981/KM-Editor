// SPDX-License-Identifier: GPL-3.0-only

namespace KM.PLA;

public sealed record PlaChoice(string Value, string Label);
public sealed record PlaField(string Key, string Label, string Kind = "number", long? Minimum = null, long? Maximum = null,
    IReadOnlyList<PlaChoice>? Choices = null, bool ReadOnly = false,
    IReadOnlyDictionary<string, IReadOnlyList<PlaChoice>>? ChoicesByMethod = null);
public sealed record PlaRecord(string Id, string Label, IReadOnlyDictionary<string, string> Values)
{
    public IReadOnlyList<string> ReadOnlyFields { get; init; } = [];
}
public sealed record PlaPage(string Kind, string Revision, IReadOnlyList<PlaField> Fields, IReadOnlyList<PlaField> RowFields,
    IReadOnlyList<PlaRecord> Records, int Total, IReadOnlyList<string> TextFiles, string? TextFile, IReadOnlyList<string> Notices);
