// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Placement;
using KM.SwSh.Shops;
using KM.SwSh.ShakingTrees;

namespace KM.SwSh.Randomizer;

public sealed partial class SwShRandomizerService
{
    private readonly SwShShopsWorkflowService shopsWorkflowService = new();
    private readonly SwShShopsEditSessionService shopsEditSessionService = new();
    private readonly SwShPlacementWorkflowService placementWorkflowService = new();
    private readonly SwShPlacementEditSessionService placementEditSessionService = new();
    private readonly SwShShakingTreesService shakingTreesService = new(itemsOnly: true);

    private void AddShopPlan(OpenedProject project, IReadOnlyList<ItemCandidate> items, string key,
        SwShRandomizerOptions options, ICollection<RandomizerDomainPlan> plans, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.RandomizeShops || items.Count == 0) return;
        var workflow = shopsWorkflowService.Load(project);
        AddWorkflowErrors(workflow.Diagnostics, diagnostics);
        var valid = workflow.EditableFields.FirstOrDefault(field => field.Field == SwShShopsWorkflowService.ItemIdField)
            ?.Options.Select(option => option.Value).ToHashSet() ?? [];
        var pool = items.Where(item => valid.Contains(item.ItemId)).ToArray();
        if (pool.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "No available items match the shop inventory rules.", field: "shops"));
            return;
        }
        var safeOriginals = items.Select(item => item.ItemId).ToHashSet();
        var updates = new List<SwShShopInventoryItemUpdate>();
        foreach (var shop in workflow.Shops.OrderBy(row => row.ShopId, StringComparer.Ordinal))
        {
            var rng = DeterministicRandom.Create(key, "shops/" + shop.ShopId);
            foreach (var row in shop.Inventory.OrderBy(row => row.Slot))
                if (safeOriginals.Contains(row.ItemId)) updates.Add(new(shop.ShopId, row.Slot, SwShShopsWorkflowService.ItemIdField, Number(rng.Pick(pool).ItemId)));
        }
        var staged = shopsEditSessionService.UpdateInventoryItems(project.Paths, null, updates);
        AddWorkflowErrors(staged.Diagnostics, diagnostics);
        if (staged.Session.PendingEdits.Count > 0)
            plans.Add(new("Shops", staged.Session, shopsEditSessionService.CreateChangePlan, shopsEditSessionService.ApplyChangePlan));
    }

    private void AddWorldItemPlan(OpenedProject project, IReadOnlyList<ItemCandidate> items, string key,
        SwShRandomizerOptions options, ICollection<RandomizerDomainPlan> plans, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.RandomizeWorldItems || items.Count == 0) return;
        var workflow = placementWorkflowService.Load(project);
        AddWorkflowErrors(workflow.Diagnostics, diagnostics);
        var valid = workflow.EditableFields.FirstOrDefault(field => field.Field == SwShPlacementWorkflowService.ItemIdField)
            ?.Options.Select(option => option.Value).ToHashSet() ?? [];
        var pool = items.Where(item => valid.Contains(item.ItemId)).ToArray();
        if (pool.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "No available items have the mappings required for world pickups.", field: "worldItems"));
            return;
        }
        var safeOriginals = items.Select(item => item.ItemId).ToHashSet();
        var updates = workflow.Objects.Where(row => row.ObjectType is "FieldItem" or "HiddenItem"
                && row.ItemId is > 0 and <= int.MaxValue && safeOriginals.Contains((int)row.ItemId.Value))
            .OrderBy(row => row.ObjectId, StringComparer.Ordinal)
            .Select(row => new SwShPlacementObjectFieldUpdate(row.ObjectId, SwShPlacementWorkflowService.ItemIdField,
                Number(DeterministicRandom.Create(key, "worldItems/" + row.ObjectId).Pick(pool).ItemId))).ToArray();
        var staged = placementEditSessionService.UpdateObjectFields(project.Paths, null, updates);
        AddWorkflowErrors(staged.Diagnostics, diagnostics);
        if (staged.Session.PendingEdits.Count > 0)
            plans.Add(new("World Items", staged.Session, placementEditSessionService.CreateChangePlan, placementEditSessionService.ApplyChangePlan));
        var trees = shakingTreesService.Load(project.Paths);
        AddWorkflowErrors(trees.Diagnostics, diagnostics);
        var treeItems = trees.Items.Select(item => item.Value).ToHashSet();
        var treePool = items.Where(item => treeItems.Contains(item.ItemId)).ToArray();
        if (treePool.Length == 0 && trees.Trees.Count > 0)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "No available items match shaking tree rewards.", field: "worldItems"));
            return;
        }
        var treeUpdates = trees.Trees.OrderBy(tree => tree.Id, StringComparer.Ordinal).SelectMany(tree => tree.Drops
            .Select((drop, slot) => (drop, slot)).Where(row => safeOriginals.Contains(row.drop.ItemId))
            .Select(row => new SwShTreeUpdate(tree.Id, "item." + Number(row.slot),
                DeterministicRandom.Create(key, "treeItems/" + tree.Id + "/" + Number(row.slot)).Pick(treePool).ItemId))).ToArray();
        if (treeUpdates.Length == 0) return;
        var treeStage = shakingTreesService.Stage(project.Paths, treeUpdates, null);
        AddWorkflowErrors(treeStage.Diagnostics, diagnostics);
        if (treeStage.Session.PendingEdits.Count > 0)
            plans.Add(new("Tree Items", treeStage.Session, shakingTreesService.CreateChangePlan, shakingTreesService.ApplyChangePlan));
    }
}
