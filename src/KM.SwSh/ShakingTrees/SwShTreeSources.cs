// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Files;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Encounters;
using KM.SwSh.HyperTraining;
using KM.SwSh.Placement;
using KM.SwSh.Workflows;

namespace KM.SwSh.ShakingTrees;

internal sealed class SwShTreeSources
{
    internal const string PlacementPath = SwShPlacementWorkflowService.PlacementDataPath;
    internal const string EncounterPath = SwShEncountersWorkflowService.WildDataPath;
    internal const string MainPath = "exefs/main";
    internal readonly ProjectPaths Paths;
    internal readonly bool CanEdit;
    internal readonly Dictionary<string, byte[]> Current = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, byte[]> Vanilla = new(StringComparer.Ordinal);
    internal readonly HashSet<ProjectFileReference> Sources = [];
    internal readonly Dictionary<string, Cell> Cells = new(StringComparer.Ordinal);
    internal readonly List<Tree> Trees = [];
    internal readonly List<Area> Areas = [];
    internal readonly SwShGfPackFile Placement;
    internal readonly SwShGfPackFile Encounters;
    internal readonly SwShWildEncounterArchive EncounterTable;
    internal readonly string EncounterMember;
    internal readonly IReadOnlyDictionary<int, ulong> ItemHashes;
    internal readonly SwShTreeOption[] Items;
    internal readonly SwShTreePokemonOption[] Pokemon;

    internal SwShTreeSources(OpenedProject project, EditSession? session = null)
    {
        Paths = project.Paths;
        if (Paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield) || !project.Health.CanOpenReadOnlyWorkflows)
            throw new InvalidDataException("Shaking Trees requires readable Sword or Shield project sources.");
        CanEdit = project.Health.CanOpenEditableWorkflows;
        var language = SwShGameTextLanguage.Resolve(Paths);
        var names = Names(language, "itemname");
        var hashes = SwShItemHashTable.Parse(Read(SwShPlacementWorkflowService.ItemHashPath));
        ItemHashes = hashes.ToHashByItemId();
        var byHash = hashes.ToItemIdByHash();
        Items = hashes.Entries.Where(row => row.ItemId > 0 && row.ItemId < names.Count && !string.IsNullOrWhiteSpace(names[row.ItemId].Text))
            .Select(row => new SwShTreeOption(row.ItemId, names[row.ItemId].Text)).OrderBy(row => row.Value).ToArray();
        var speciesNames = Names(language, "monsname");
        var personal = SwShTreeComposition.Pokemon(SwShPersonalTable.Parse(Read(SwShPersonalTable.PersonalDataRelativePath), 4096).Records, session);
        var pokemon = new List<SwShTreePokemonOption>();
        for (var species = 1; species <= 898 && species < personal.Count && species < speciesNames.Count; species++)
        {
            var row = personal[species];
            for (var form = 0; form < Math.Max(1, row.FormCount); form++)
            {
                var index = form == 0 || row.FormStatsIndex == 0 ? species : row.FormStatsIndex + form - 1;
                if (index >= personal.Count || !personal[index].IsPresentInGame) continue;
                pokemon.Add(new(species, form, speciesNames[species].Text));
            }
        }
        Pokemon = pokemon.ToArray();
        Placement = SwShGfPackFile.Parse(Read(PlacementPath));
        Encounters = SwShGfPackFile.Parse(Read(EncounterPath));
        EncounterMember = Paths.SelectedGame == ProjectGame.Sword ? "encount_k.bin" : "encount_t.bin";
        EncounterTable = SwShWildEncounterArchive.Parse(Encounters.GetFileByName(EncounterMember), 50_000);
        var members = SwShAhtbFile.Parse(Placement.GetFileByName("AreaNameHashTable.tbl")).Entries.Select(row => row.Name + ".bin").Distinct();
        var labels = SwShAhtbFile.Parse(Placement.GetFileByName("ObjectNameHashTable.tbl")).ToDictionary();
        foreach (var member in members.Where(Placement.ContainsFileName).Order(StringComparer.Ordinal))
        {
            var archive = SwShPlacementZoneArchive.Parse(Placement.GetFileByName(member));
            foreach (var zone in archive.Zones)
            foreach (var tree in zone.RawObjects.Where(row => row.ObjectType == "BerryTree"))
            {
                var actor = tree.Fields.Single(row => row.Field == "raw.BerryTree.Field_00.Field_00.Field_00.HashObjectName").Value;
                var actorHash = ulong.Parse(actor.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var id = $"tree:{member}:{zone.ZoneId:X16}:{actorHash:X16}";
                var areaId = $"area:{zone.ZoneId:X16}";
                var itemFields = tree.Fields.Where(row => row.Field.StartsWith("raw.BerryTree.Field_01[", StringComparison.Ordinal) && row.Field.EndsWith(".Hash", StringComparison.Ordinal)).ToArray();
                if (itemFields.Length is < 1 or > 10) throw new InvalidDataException("A tree has an unsupported item slot count.");
                for (var slot = 0; slot < itemFields.Length; slot++)
                {
                    var prefix = $"raw.BerryTree.Field_01[{slot}]";
                    var hash = ulong.Parse(tree.Fields.Single(row => row.Field == prefix + ".Hash").Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (!byHash.TryGetValue(hash, out var item)) throw new InvalidDataException("A tree item hash is not in the current item catalog.");
                    Add(id, $"item.{slot}", item, 1, ushort.MaxValue, PlacementPath);
                    Add(id, $"percent.{slot}", int.Parse(tree.Fields.Single(row => row.Field == prefix + ".Rate").Value, CultureInfo.InvariantCulture), 0, 100, PlacementPath);
                }
                Trees.Add(new(id, labels.GetValueOrDefault(actorHash, actor), areaId, SwShEncounterZoneNames.Format(zone.ZoneId), member,
                    zone.ZoneIndex, tree.ObjectIndex, itemFields.Length, archive));
                if (Areas.Any(row => row.Id == areaId)) continue;
                var matches = EncounterTable.Tables.Select((row, index) => (row, index)).Where(pair => pair.row.ZoneId == zone.ZoneId).ToArray();
                if (matches.Length != 1 || matches[0].row.SubTables.Count <= 9) throw new InvalidDataException("The tree encounter area is missing or ambiguous.");
                var encounter = matches[0].row.SubTables[9];
                if (encounter.Slots.Count is < 1 or > 20) throw new InvalidDataException("The tree encounter slot count is unsupported.");
                Add(areaId, "levelMin", encounter.LevelMin, 1, 100, EncounterPath);
                Add(areaId, "levelMax", encounter.LevelMax, 1, 100, EncounterPath);
                for (var slot = 0; slot < encounter.Slots.Count; slot++)
                {
                    Add(areaId, $"species.{slot}", encounter.Slots[slot].Species, 0, 898, EncounterPath);
                    Add(areaId, $"form.{slot}", encounter.Slots[slot].Form, 0, 255, EncounterPath);
                    Add(areaId, $"percent.{slot}", encounter.Slots[slot].Probability, 0, 100, EncounterPath);
                }
                Areas.Add(new(areaId, SwShEncounterZoneNames.Format(zone.ZoneId), matches[0].index, encounter.Slots.Count));
            }
        }
        if (Trees.Count is < 1 or > 512 || Trees.Select(row => row.Id).Distinct().Count() != Trees.Count)
            throw new InvalidDataException("The tree catalog is missing or ambiguous.");
        var script = Read(SwShTreeScript.Path);
        var scriptValues = SwShTreeScript.Read(Read(SwShTreeScript.Path, true), script);
        for (var i = 0; i < scriptValues.Length; i++)
            Add("settings", SwShTreeScript.Fields[i], scriptValues[i], i is 2 or 3 ? 1 : 0, i == 4 ? 101 : 99, SwShTreeScript.Path);
        var quantity = SwShTreeQuantityPatcher.Read(Read(MainPath), Paths.SelectedGame.Value);
        var originalQuantity = SwShTreeQuantityPatcher.Read(Read(MainPath, true), Paths.SelectedGame.Value);
        if (originalQuantity.Min != 1 || originalQuantity.Max != 3) throw new InvalidDataException("Base ExeFS has modified tree quantities.");
        Add("settings", "dropMin", quantity.Min, 1, 3, MainPath);
        Add("settings", "dropMax", quantity.Max, 1, 3, MainPath);
    }

    internal byte[] Read(string path, bool vanilla = false)
    {
        var cache = vanilla ? Vanilla : Current;
        if (cache.TryGetValue(path, out var bytes)) return bytes;
        var original = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(Paths, path)
            ?? throw new InvalidDataException($"A required source is missing: {path}.");
        var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(Paths, path);
        if (!vanilla && target is not null && Directory.Exists(target)) throw new InvalidDataException($"The output target is a directory: {path}.");
        var layered = !vanilla && target is not null && File.Exists(target);
        var actual = layered ? target! : original;
        var info = new FileInfo(actual);
        if (info.Length is < 1 or > 128 * 1024 * 1024) throw new InvalidDataException($"Source size is unsupported: {path}.");
        Sources.Add(new(layered ? ProjectFileLayer.Layered : ProjectFileLayer.Base, path));
        return cache[path] = File.ReadAllBytes(actual);
    }

    private IReadOnlyList<SwShGameTextLine> Names(string language, string stem)
    {
        var path = SwShGameTextLanguage.CommonMessagePath(language, stem + ".dat");
        if (SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(Paths, path) is null)
            path = SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.English, stem + ".dat");
        return SwShGameTextFile.Parse(Read(path)).Lines;
    }

    private void Add(string id, string field, int value, int min, int max, string path) => Cells.Add(Key(id, field), new(value, min, max, path));
    internal static string Key(string id, string field) => id + "|" + field;
    internal Dictionary<string, int> Values() => Cells.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
    internal static int Get(IReadOnlyDictionary<string, int> values, string id, string field) => values[Key(id, field)];
    internal static SwShTreeSettings Settings(IReadOnlyDictionary<string, int> values) => new(Get(values, "settings", "initialRiskMin"), Get(values, "settings", "initialRiskMax"),
        Get(values, "settings", "riskStepMin"), Get(values, "settings", "riskStepMax"), Get(values, "settings", "loseAllRolls"), Get(values, "settings", "dropMin"), Get(values, "settings", "dropMax"));
    internal SwShShakingTreesWorkflow Workflow(IReadOnlyList<KM.Core.Diagnostics.ValidationDiagnostic> diagnostics) => new(CanEdit,
        Trees.Select(row => new SwShTreeRecord(row.Id, row.Label, row.AreaId, row.Name, Enumerable.Range(0, row.Slots)
            .Select(slot => new SwShTreeDrop(Cells[Key(row.Id, $"item.{slot}")].Value, Cells[Key(row.Id, $"percent.{slot}")].Value)).ToArray())).ToArray(),
        Areas.Select(row => new SwShTreeArea(row.Id, row.Name, Cells[Key(row.Id, "levelMin")].Value, Cells[Key(row.Id, "levelMax")].Value,
            Enumerable.Range(0, row.Slots).Select(slot => new SwShTreeSlot(Cells[Key(row.Id, $"species.{slot}")].Value,
                Cells[Key(row.Id, $"form.{slot}")].Value, Cells[Key(row.Id, $"percent.{slot}")].Value)).ToArray())).ToArray(),
        Settings(Values()), Items, Pokemon, diagnostics);

    internal sealed record Cell(int Value, int Min, int Max, string Path);
    internal sealed record Tree(string Id, string Label, string AreaId, string Name, string Member, int Zone, int Object, int Slots, SwShPlacementZoneArchive Archive);
    internal sealed record Area(string Id, string Name, int Table, int Slots);
}
