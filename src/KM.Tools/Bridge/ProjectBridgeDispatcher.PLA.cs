// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Editing;
using KM.Api.PLA;
using KM.Api.Workflows;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.PLA;

namespace KM.Tools.Bridge;

public sealed partial class ProjectBridgeDispatcher
{
    private readonly PlaWorkflowService plaWorkflowService = new();

    private static bool IsPlaAllowedCommand(string command) => command is KmCommandNames.LoadPla or KmCommandNames.StagePla
        or KmCommandNames.ListWorkflows or KmCommandNames.StartEditSession or KmCommandNames.ValidateEditSession
        or KmCommandNames.CreateChangePlan or KmCommandNames.ApplyChangePlan
        or KmCommandNames.ModelCatalog or KmCommandNames.ModelPrepare or KmCommandNames.ModelTextures
        or KmCommandNames.ModelTextureStage or KmCommandNames.ModelProperties or KmCommandNames.ModelAssetStage
        or KmCommandNames.SoundStudio
        || command.StartsWith("project.", StringComparison.Ordinal)
        || command.StartsWith("workspace.", StringComparison.Ordinal)
        || IsOutputSafetyCommand(command) || IsChangeSetCommand(command);

    private static ListWorkflowsResponse ListPlaWorkflows() => new([
        new("pokemon", "Pokemon", "PLA personal data, learnsets, mastery, evolutions, and move tutoring.", WorkflowAvailabilityDto.Available, []),
        new("moves", "Moves", "Verified PLA move and style values.", WorkflowAvailabilityDto.Available, []),
        new("text", "Text", "PLA message text with preserved keys and control tokens.", WorkflowAvailabilityDto.Available, []),
        new("trainers", "Trainers", "Existing PLA parties, levels, effort levels, and moves with protected event roles.", WorkflowAvailabilityDto.Available, []),
        new("giftPokemon", "Gift Pokemon", "Supported existing PLA gifts with protected story identities.", WorkflowAvailabilityDto.Available, []),
        new("staticEncounters", "Static Encounters", "Supported existing PLA battles with protected event identities.", WorkflowAvailabilityDto.Available, []),
        new("items", "Items", "Supported PLA inventory item prices and stack limits.", WorkflowAvailabilityDto.Available, []),
        new("crafting", "Crafting", "Existing PLA recipes, ingredients, and result quantities.", WorkflowAvailabilityDto.Available, []),
        new("shops", "Shops", "Existing PLA shop slots and ordinary Ginkgo offers with preserved unlock conditions.", WorkflowAvailabilityDto.Available, []),
        new("pokemonDrops", "Pokemon Drops", "Shared PLA field and Alpha drop items and chances.", WorkflowAvailabilityDto.Available, []),
        new("research", "Research", "Supported PLA task thresholds, research points, and rank requirements.", WorkflowAvailabilityDto.Available, []),
        new("encounters", "Wild Encounters", "Existing PLA encounter slots, Alpha traits, eligibility, and supported spawn counts.", WorkflowAvailabilityDto.Available, []),
        new("outbreaks", "Outbreaks", "PLA outbreak encounters, counts, selection weights, and duration choices.", WorkflowAvailabilityDto.Available, []),
        new("distortions", "Space Time Distortions", "Supported PLA distortion encounters, spawn counts, and item selections.", WorkflowAvailabilityDto.Available, []),
        new("weather", "Weather", "PLA area weather selection weights with preserved story conditions.", WorkflowAvailabilityDto.Available, []),
        new("gameplayRules", "Gameplay Rules", "Supported PLA capture factors, friendship changes, and shiny bonuses.", WorkflowAvailabilityDto.Available, []),
        new("typeChart", "Type Chart", "PLA type matchups with preserved combined damage rules.", WorkflowAvailabilityDto.Available, []),
        new("modelViewer", "3D Model Editor", "PLA Pokemon models, animations, materials, and texture colors.", WorkflowAvailabilityDto.Available, []),
        new("soundStudio", "Sound Studio", "Browse PLA recordings and listen without changing audio files or events.", WorkflowAvailabilityDto.Available, []),
    ]);

    private string DispatchLoadPla(string requestJson)
    {
        var request = DeserializeRequest<LoadPlaRequest>(requestJson);
        var payload = request.Payload;
        var page = plaWorkflowService.Load(ProjectBridgeMapper.ToCore(payload.Paths),
            payload.Session is null ? EditSession.Start() : EditSessionBridgeMapper.ToCore(payload.Session),
            payload.Kind, payload.TextFile, payload.Query, payload.Offset, payload.MessageLanguage);
        return SerializeSuccess(new LoadPlaResponse(new PlaPageDto(page.Kind, page.Revision,
            page.Fields.Select(ToPlaFieldDto).ToArray(), page.RowFields.Select(ToPlaFieldDto).ToArray(),
            page.Records.Select(record => new PlaRecordDto(record.Id, record.Label, record.Values, record.ReadOnlyFields, record.Metadata,
                record.RelatedRecords.Select(related => new PlaRelatedRecordDto(related.Kind, related.Id, related.Label)).ToArray())).ToArray(),
            page.Total, page.TextFiles, page.TextFile, page.Notices, page.PokemonSpriteNames,
            page.Groups.Select(group => new PlaChoiceDto(group.Value, group.Label)).ToArray())), request.RequestId);
    }

    private static PlaFieldDto ToPlaFieldDto(PlaField field) => new(field.Key, field.Label, field.Kind, field.Minimum, field.Maximum,
        field.Choices?.Select(choice => new PlaChoiceDto(choice.Value, choice.Label)).ToArray(), field.ReadOnly,
        field.ChoicesByMethod?.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlaChoiceDto>)pair.Value.Select(choice => new PlaChoiceDto(choice.Value, choice.Label)).ToArray()));

    private string DispatchStagePla(string requestJson)
    {
        var request = DeserializeRequest<StagePlaRequest>(requestJson);
        var payload = request.Payload;
        var paths = ProjectBridgeMapper.ToCore(payload.Paths);
        var session = payload.Session is null ? EditSession.Start() : EditSessionBridgeMapper.ToCore(payload.Session);
        return changeSetApplicationService.ExecuteWithAuthoringBindingAsync(session, paths, GetBoundOutputMode(session),
            (candidate, mode) => CreateChangePlanForSession(paths, candidate, mode),
            () => SerializeSuccess(new StagePlaResponse(EditSessionBridgeMapper.ToDto(plaWorkflowService.Stage(paths, session,
                payload.Kind, payload.Id, payload.Values, payload.Revision, payload.RestoreBase))), request.RequestId)).GetAwaiter().GetResult();
    }

    private ChangePlan CreatePlaChangePlan(ProjectPaths paths, EditSession session, ChangePlanOutputModeDto? mode)
    {
        if (mode is not (null or ChangePlanOutputModeDto.Standalone))
            return new(session.Id, [], [new(KM.Core.Diagnostics.DiagnosticSeverity.Error, "PLA supports standalone output.", Domain: "pla") { Code = "KM-PLA-INVALID-DATA" }]);
        return plaWorkflowService.CreateChangePlan(paths, session);
    }
}
