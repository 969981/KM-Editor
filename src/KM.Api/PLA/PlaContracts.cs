// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.PLA;

public sealed record LoadPlaRequest(ProjectPathsDto Paths, EditSessionDto? Session, string Kind,
    string? TextFile = null, string? Query = null, int Offset = 0, string? MessageLanguage = null);
public sealed record StagePlaRequest(ProjectPathsDto Paths, EditSessionDto? Session, string Kind, string Id,
    IReadOnlyDictionary<string, string> Values, string Revision, bool RestoreBase = false);
public sealed record StagePlaResponse(EditSessionDto Session);
public sealed record PlaChoiceDto(string Value, string Label);
public sealed record PlaFieldDto(string Key, string Label, string Kind, long? Minimum, long? Maximum, IReadOnlyList<PlaChoiceDto>? Choices, bool ReadOnly,
    IReadOnlyDictionary<string, IReadOnlyList<PlaChoiceDto>>? ChoicesByMethod);
public sealed record PlaRecordDto(string Id, string Label, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> ReadOnlyFields);
public sealed record PlaPageDto(string Kind, string Revision, IReadOnlyList<PlaFieldDto> Fields, IReadOnlyList<PlaFieldDto> RowFields,
    IReadOnlyList<PlaRecordDto> Records, int Total, IReadOnlyList<string> TextFiles, string? TextFile, IReadOnlyList<string> Notices);
public sealed record LoadPlaResponse(PlaPageDto Page);
