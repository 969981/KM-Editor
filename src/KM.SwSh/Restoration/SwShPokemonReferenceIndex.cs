// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public sealed record SwShPokemonReference(int Species, int Form, string Editor, string File, string Record, string Field);
public sealed record SwShPokemonReferences(IReadOnlyList<SwShPokemonReference> References, IReadOnlyList<ProjectFileReference> Sources);

/// <summary>Indexes typed assignments in the final prospective project.</summary>
public static class SwShPokemonReferenceIndex
{
    public static SwShPokemonReferences Find(ProjectPaths paths, IReadOnlySet<int> removed)
    {
        var references = new List<SwShPokemonReference>(); var sources = new HashSet<ProjectFileReference>(); long readBytes = 0;
        void Add(int species, int form, string editor, string file, string record, string field = "species")
        {
            if (removed.Contains(species)) references.Add(new(species, form, editor, file, record, field));
            if (references.Count > 100000) throw new InvalidDataException("Pokemon reference count exceeds its review budget.");
        }
        byte[] Read(string relative)
        {
            var layered = Path.Combine(paths.OutputRootPath!, relative);
            var native = Path.Combine(paths.BaseRomFsPath!, relative[6..]);
            var output = File.Exists(layered); var file = output ? layered : native;
            var info = new FileInfo(file);
            if (!info.Exists || info.LinkTarget is not null || info.Length > 64L * 1024 * 1024 || (readBytes += info.Length) > 512L * 1024 * 1024)
                throw new InvalidDataException($"Pokemon references require a readable source: {relative}.");
            sources.Add(new(output ? ProjectFileLayer.Layered : ProjectFileLayer.Base, relative));
            return File.ReadAllBytes(file);
        }
        IEnumerable<string> Files(string directory, string pattern)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in new[] { Path.Combine(paths.BaseRomFsPath!, directory[6..]), Path.Combine(paths.OutputRootPath!, directory) })
            {
                if (!Directory.Exists(root)) continue;
                if (new DirectoryInfo(root).LinkTarget is not null) throw new InvalidDataException("Linked Pokemon reference folders are unsupported.");
                foreach (var path in Directory.EnumerateFiles(root, pattern)) result.Add(directory + "/" + Path.GetFileName(path));
            }
            if (result.Count > 10000) throw new InvalidDataException("Pokemon reference source count exceeds its budget.");
            return result.Order(StringComparer.Ordinal);
        }

        var personal = SwShPersonalTable.Parse(Read(SwShRestorationComposer.PersonalPath), 4096).Records;
        var identities = SwShEffectivePokemonCatalog.Identities(personal).ToDictionary(i => i.PersonalId);
        foreach (var row in identities.Values.Where(i => i.Personal.IsPresentInGame && !removed.Contains(i.Identity.Species)))
            Add(row.Personal.HatchedSpecies, 0, "pokemon", SwShRestorationComposer.PersonalPath, row.PersonalId.ToString(CultureInfo.InvariantCulture), "hatchedSpecies");
        foreach (var file in Files("romfs/bin/pml/evolution", "evo_*.bin"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file).AsSpan(4), out var owner)) throw new InvalidDataException("Evolution reference identity is invalid.");
            if (!identities.TryGetValue(owner, out var identity) || !identity.Personal.IsPresentInGame || removed.Contains(identity.Identity.Species)) continue;
            foreach (var evolution in SwShEvolutionSet.Parse(Read(file)).Evolutions)
                Add(evolution.Species, evolution.Form, "pokemon", file, $"{owner}:{evolution.Slot}", "evolution");
        }
        foreach (var file in Files(SwShTrainerTeamFile.TrainerPokeRootRelativePath, "*.bin"))
            foreach (var row in SwShTrainerTeamFile.Parse(Read(file)).Records)
                Add(row.SpeciesId, row.Form, "trainers", file, row.Slot.ToString(CultureInfo.InvariantCulture));

        const string behavior = KM.SwSh.Behavior.SwShBehaviorWorkflowService.BehaviorDataPath;
        foreach (var row in SwShSymbolBehaviorArchive.Parse(Read(behavior)).Entries)
            Add(row.SpeciesId, row.Form, "behavior", behavior, row.Index.ToString(CultureInfo.InvariantCulture), "speciesId");

        const string gifts = "romfs/bin/script_event_data/add_poke.bin";
        foreach (var row in SwShGiftPokemonArchive.Parse(Read(gifts)).Gifts) Add(row.Species, row.Form, "giftPokemon", gifts, $"gift:{row.Index}");
        var trades = "romfs/bin/script_event_data/field_trade.bin";
        if (!File.Exists(Path.Combine(paths.BaseRomFsPath!, trades[6..])) && !File.Exists(Path.Combine(paths.OutputRootPath!, trades)))
            trades = "romfs/bin/script_event_data/field_trade_data.bin";
        foreach (var row in SwShTradePokemonArchive.Parse(Read(trades)).Trades)
        {
            Add(row.Species, row.Form, "tradePokemon", trades, $"trade:{row.Index}");
            Add(row.RequiredSpecies, row.RequiredForm, "tradePokemon", trades, $"trade:{row.Index}", "requiredSpecies");
        }
        const string rentals = "romfs/bin/script_event_data/rental.bin";
        foreach (var row in SwShRentalPokemonArchive.Parse(Read(rentals)).Rentals) Add(row.Species, row.Form, "rentalPokemon", rentals, $"rental:{row.Index}");
        const string statics = "romfs/bin/script_event_data/event_encount_data.bin";
        foreach (var row in SwShStaticEncounterArchive.Parse(Read(statics)).Encounters) Add(row.Species, row.Form, "staticEncounters", statics, $"static:{row.Index}");
        const string adventure = "romfs/bin/appli/chika/data_table/underground_exploration_poke.bin";
        if (File.Exists(Path.Combine(paths.BaseRomFsPath!, adventure[6..])) || File.Exists(Path.Combine(paths.OutputRootPath!, adventure)))
            foreach (var row in SwShDynamaxAdventureArchive.Parse(Read(adventure)).Entries) Add(row.Species, row.Form, "dynamaxAdventures", adventure, row.EntryIndex.ToString(CultureInfo.InvariantCulture));

        const string wild = "romfs/bin/archive/field/resident/data_table.gfpak";
        var pack = SwShGfPackFile.ParseBoundedReadOnly(Read(wild), 50000, 64L * 1024 * 1024);
        var edition = paths.SelectedGame == ProjectGame.Sword ? "k" : "t";
        foreach (var member in new[] { $"encount_{edition}.bin", $"encount_symbol_{edition}.bin" })
        {
            if (!pack.TryGetFileByName(member, 32 * 1024 * 1024, out var bytes)) throw new InvalidDataException("Wild encounter reference member is missing.");
            var tables = SwShWildEncounterArchive.Parse(bytes, 50000).Tables;
            for (var table = 0; table < tables.Count; table++)
                for (var sub = 0; sub < tables[table].SubTables.Count; sub++)
                    for (var slot = 0; slot < tables[table].SubTables[sub].Slots.Count; slot++)
                    {
                        var row = tables[table].SubTables[sub].Slots[slot];
                        Add(row.Species, row.Form, sub == 9 ? "shakingTrees" : "encounters", wild, $"{member}:{table}:{sub}:{slot}");
                    }
        }
        if (!pack.TryGetFileByName("nest_hole_encount.bin", 32 * 1024 * 1024, out var nests)) throw new InvalidDataException("Raid reference member is missing.");
        foreach (var table in SwShEncounterNestArchive.Parse(nests).Tables)
            foreach (var row in table.Entries) Add(row.Species, row.Form, "raidBattles", wild, $"{table.TableId}:{row.EntryIndex}");

        const string placement = "romfs/bin/archive/field/resident/placement.gfpak";
        var placementPack = SwShGfPackFile.ParseBoundedReadOnly(Read(placement), 50000, 64L * 1024 * 1024);
        if (!placementPack.TryGetFileByName("AreaNameHashTable.tbl", 8 * 1024 * 1024, out var areas)) throw new InvalidDataException("Placement reference area list is missing.");
        foreach (var name in SwShAhtbFile.Parse(areas).ToDictionary().Values.Distinct(StringComparer.Ordinal))
        {
            var member = name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ? name : name + ".bin";
            if (!placementPack.TryGetFileByName(member, 16 * 1024 * 1024, out var bytes)) throw new InvalidDataException("Placement reference area is missing.");
            foreach (var zone in SwShPlacementZoneArchive.Parse(bytes).Zones)
                foreach (var item in zone.RawObjects)
                    foreach (var field in item.Fields.Where(f => f.Field.EndsWith(".Species", StringComparison.Ordinal)))
                    {
                        if (!int.TryParse(field.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var species)) throw new InvalidDataException("Placement Pokemon identity is invalid.");
                        var formValue = item.Fields.SingleOrDefault(f => f.Field == field.Field[..^7] + "Form")?.Value;
                        var form = formValue is null ? 0 : int.Parse(formValue, CultureInfo.InvariantCulture);
                        Add(species, form, "placement", placement, $"{member}:{zone.ZoneIndex}:{item.ObjectIndex}", field.Field);
                    }
        }
        return new(references, sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal).ToArray());
    }
}
