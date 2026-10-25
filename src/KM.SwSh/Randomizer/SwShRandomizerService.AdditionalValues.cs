// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Placement;
using KM.SwSh.Shops;
using KM.SwSh.Trainers;
using KM.SwSh.ShakingTrees;
using T = KM.SwSh.Trainers.SwShTrainersWorkflowService;

namespace KM.SwSh.Randomizer;

public sealed partial class SwShRandomizerService
{
    private static readonly string[] TrainerTeamFields = [T.SpeciesIdField, T.FormField, T.LevelField, T.HeldItemIdField,
        T.Move1IdField, T.Move2IdField, T.Move3IdField, T.Move4IdField, T.GenderField, T.AbilityField, T.NatureField,
        T.EvHpField, T.EvAttackField, T.EvDefenseField, T.EvSpecialAttackField, T.EvSpecialDefenseField, T.EvSpeedField,
        T.DynamaxLevelField, T.CanGigantamaxField, T.IvHpField, T.IvAttackField, T.IvDefenseField,
        T.IvSpecialAttackField, T.IvSpecialDefenseField, T.IvSpeedField, T.ShinyField, T.CanDynamaxField];

    private static string[] TrainerSlotValues(SwShTrainerPokemonRecord row) => new[] { row.SpeciesId, row.Form, row.Level, row.HeldItemId,
        row.MoveIds.ElementAtOrDefault(0), row.MoveIds.ElementAtOrDefault(1), row.MoveIds.ElementAtOrDefault(2), row.MoveIds.ElementAtOrDefault(3),
        row.Gender, row.Ability, row.Nature, row.Evs.HP, row.Evs.Attack, row.Evs.Defense, row.Evs.SpecialAttack, row.Evs.SpecialDefense, row.Evs.Speed,
        row.DynamaxLevel, row.CanGigantamax ? 1 : 0, row.Ivs.HP, row.Ivs.Attack, row.Ivs.Defense, row.Ivs.SpecialAttack, row.Ivs.SpecialDefense, row.Ivs.Speed,
        row.Shiny ? 1 : 0, row.CanDynamax ? 1 : 0 }.Select(value => Number(value)).ToArray();

    private static int TrainerTeamCount(string[] values) => values.Where((value, index) => index % TrainerTeamFields.Length == 0 && value != "0").Count();

    private IReadOnlyList<RandomizerCurrentValue> ReadAdditionalRandomizerValues(OpenedProject project, string domain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var values = new List<RandomizerCurrentValue>();
        if (domain == "Trainers")
        {
            var workflow = trainersWorkflowService.Load(project);
            AddWorkflowErrors(workflow.Diagnostics, diagnostics);
            foreach (var trainer in workflow.Trainers)
            {
                var root = Number(trainer.TrainerId);
                ProjectFileReference[] sources = [new(trainer.Provenance.TeamSourceLayer, trainer.Provenance.TeamSourceFile),
                    new(trainer.Provenance.SourceLayer, trainer.Provenance.SourceFile)];
                var team = trainer.Team.OrderBy(row => row.Slot).ToArray();
                PendingEdit Edit(int slot, string field, string value) => new("workflow.trainers", "Restore Randomizer trainer value",
                    sources, T.CreateTeamRecordId(trainer.TrainerId, slot), field, value);
                values.Add(new(domain, root + "/team", trainer.Provenance.TeamSourceFile, team.SelectMany(TrainerSlotValues).ToArray(),
                    restored => team.SelectMany((row, index) => TrainerTeamFields.Select((field, fieldIndex) =>
                        Edit(row.Slot, field, restored[index * TrainerTeamFields.Length + fieldIndex])))));
                foreach (var row in team)
                {
                    var current = TrainerSlotValues(row);
                    void Group(string suffix, int[] indexes) => values.Add(new(domain, root + "/" + row.Slot + "/" + suffix,
                        trainer.Provenance.TeamSourceFile, indexes.Select(index => current[index]).ToArray(),
                        restored => indexes.Select((index, offset) => Edit(row.Slot, TrainerTeamFields[index], restored[offset]))));
                    Group("identity", [0, 1, 8, 9, 18]);
                    Group("moves", [4, 5, 6, 7]); Group("evs", [11, 12, 13, 14, 15, 16]); Group("ivs", [19, 20, 21, 22, 23, 24]);
                    foreach (var index in new[] { 2, 3, 10, 17, 25, 26 }) Group(TrainerTeamFields[index], [index]);
                }
            }
        }
        else if (domain == "Shops")
        {
            var workflow = shopsWorkflowService.Load(project);
            AddWorkflowErrors(workflow.Diagnostics, diagnostics);
            foreach (var shop in workflow.Shops)
            {
                var rows = shop.Inventory.OrderBy(row => row.Slot).ToArray();
                var identity = $"{shop.Kind}/{shop.SourceIndex}/{shop.SourceHash}/{shop.InventoryIndex}";
                values.Add(new(domain, identity, shop.Provenance.SourceFile, rows.Select(row => Number(row.ItemId)).ToArray(),
                    restored => rows.Select((row, index) => new PendingEdit("workflow.shops", "Restore Randomizer inventory",
                        [new(shop.Provenance.SourceLayer, shop.Provenance.SourceFile)],
                        SwShShopsWorkflowService.CreateInventoryRecordId(shop.ShopId, row.Slot), SwShShopsWorkflowService.ItemIdField, restored[index]))));
            }
        }
        else if (domain == "World Items")
        {
            var workflow = placementWorkflowService.Load(project);
            AddWorkflowErrors(workflow.Diagnostics, diagnostics);
            foreach (var row in workflow.Objects.Where(row => row.ObjectType is "FieldItem" or "HiddenItem" && row.ItemId.HasValue))
                values.Add(new(domain, row.ObjectId, row.Provenance.SourceFile, [Number(row.ItemId!.Value)],
                    restored => [new("workflow.placement", "Restore Randomizer pickup", [new(row.Provenance.SourceLayer, row.Provenance.SourceFile)],
                        row.ObjectId, SwShPlacementWorkflowService.ItemIdField, restored[0])]));
        }
        else if (domain == "Tree Items")
        {
            var workflow = shakingTreesService.Load(project.Paths);
            AddWorkflowErrors(workflow.Diagnostics, diagnostics);
            foreach (var tree in workflow.Trees)
            for (var slot = 0; slot < tree.Drops.Count; slot++)
            {
                var field = "item." + Number(slot);
                values.Add(new(domain, tree.Id + "/" + field, SwShPlacementWorkflowService.PlacementDataPath,
                    [Number(tree.Drops[slot].ItemId)], restored => [new(SwShShakingTreesService.Domain,
                        "Restore Randomizer tree reward", [], tree.Id, field, restored[0])]));
            }
        }
        return values;
    }

    private EditSession? StageAdditionalRandomizerRestore(ProjectPaths paths, string domain, IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics, SwShTrainersEditSessionService trainerRestoreService)
    {
        if (edits.Count == 0) return null;
        if (domain == "Trainers")
        {
            var updates = edits.Select(edit => {
                if (!T.TryParseTeamRecordId(edit.RecordId, out var trainer, out var slot)) throw new InvalidDataException("Trainer restoration identity is invalid.");
                return new SwShTrainerFieldUpdate(trainer, slot, edit.Field!, edit.NewValue!);
            }).ToArray();
            var result = trainerRestoreService.CreateBatchSession(paths, updates);
            AddWorkflowErrors(result.Diagnostics, diagnostics);
            return result.Session;
        }
        if (domain == "Shops")
        {
            var updates = edits.Select(edit => {
                if (!SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shop, out var slot)) throw new InvalidDataException("Shop restoration identity is invalid.");
                return new SwShShopInventoryItemUpdate(shop, slot, edit.Field!, edit.NewValue!);
            }).ToArray();
            var result = shopsEditSessionService.UpdateInventoryItems(paths, null, updates);
            AddWorkflowErrors(result.Diagnostics, diagnostics);
            return result.Session;
        }
        if (domain == "World Items")
        {
            var result = placementEditSessionService.UpdateObjectFields(paths, null,
                edits.Select(edit => new SwShPlacementObjectFieldUpdate(edit.RecordId!, edit.Field!, edit.NewValue!)).ToArray());
            AddWorkflowErrors(result.Diagnostics, diagnostics);
            return result.Session;
        }
        if (domain == "Tree Items")
        {
            var result = shakingTreesService.Stage(paths, edits.Select(edit => new SwShTreeUpdate(edit.RecordId!, edit.Field!,
                int.Parse(edit.NewValue!, System.Globalization.CultureInfo.InvariantCulture))).ToArray(), null);
            AddWorkflowErrors(result.Diagnostics, diagnostics);
            return result.Session;
        }
        return null;
    }
}
