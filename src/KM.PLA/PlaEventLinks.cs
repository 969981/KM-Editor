// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.PLA;

namespace KM.PLA;

// Resolve event consumers in their owning area. Encounter hashes are not globally unique.
internal sealed class PlaEventLinks
{
    internal const ulong EmptyHash = 0xCBF29CE484222645;
    internal const string GroupPath = "bin/field/encount/new_huge_outbreak_group.bin";
    internal const string LotteryPath = "bin/field/encount/new_huge_outbreak_group_lottery.bin";
    internal Dictionary<(string Path, ulong Table), HashSet<string>> Outbreaks { get; } = [];
    internal Dictionary<(string Path, ulong Table), HashSet<string>> Distortions { get; } = [];
    internal Dictionary<string, (string Area, string Encounter)> DistortionSpawns { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, HashSet<ulong>> Items { get; } = new(StringComparer.Ordinal);
    internal Dictionary<ulong, HashSet<string>> Groups { get; } = [];
    internal HashSet<ulong> Lotteries { get; } = [];
    internal HashSet<string> SpawnPaths { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> PlacementPaths { get; } = new(StringComparer.Ordinal);

    internal PlaEventLinks(PlaCatalog catalog, PlaFieldArchive archive)
    {
        var runtime = catalog.Scope.Compression;
        var groups = new PlaFlatBuffer(catalog.Scope.Base(GroupPath));
        var lottery = new PlaFlatBuffer(catalog.Scope.Base(LotteryPath));
        var groupRows = Unique(groups); var lotteryRows = Unique(lottery);
        var areas = new PlaFlatBuffer(archive.Read("bin/field/resident/AreaSettings.bin", runtime));
        var seen = new HashSet<(string, string)>();
        foreach (var area in areas.VectorReferences(areas.Root, 0, 256).Select(areas.TableAtReference))
        {
            var encounterPath = areas.String(area, 18); var name = areas.String(area, 0);
            if (encounterPath.Length == 0 || !archive.Contains(encounterPath)) continue;
            var encounters = new PlaFlatBuffer(archive.Read(encounterPath, runtime)); var ids = Unique(encounters);
            foreach (var field in new[] { 15, 35 })
            {
                var path = areas.String(area, field);
                if (path.Length == 0 || !archive.Contains(path) || !seen.Add((path, encounterPath))) continue;
                if (field == 35 && !DistortionSpawns.TryAdd(path, (name, encounterPath)) && DistortionSpawns[path].Encounter != encounterPath)
                    throw new InvalidDataException("A PLA distortion spawner has ambiguous area links.");
                var b = new PlaFlatBuffer(archive.Read(path, runtime));
                foreach (var row in b.VectorReferences(b.Root, 0, 4096).Select(b.TableAtReference))
                {
                    var conditions = b.VectorReferences(row, 2, 64).Select(b.TableAtReference).Select(r => b.String(r, 2)).ToArray();
                    var massive = conditions.Any(value => value.StartsWith("FSYS_NEW_OUTBREAK", StringComparison.Ordinal));
                    var outbreak = conditions.Any(value => value.StartsWith("WSYS_MASS_GANERATION", StringComparison.Ordinal));
                    if (field != 35 && !massive && !outbreak) continue;
                    SpawnPaths.Add(path);
                    var target = field == 35 ? Distortions : Outbreaks;
                    var consumer = name + "/" + b.Hash(row, 0).ToString("X16");
                    void Add(ulong table)
                    {
                        if (!ids.ContainsKey(table)) throw new InvalidDataException("A PLA event encounter link is unresolved.");
                        if (!target.TryGetValue((encounterPath, table), out var names)) target[(encounterPath, table)] = names = new(StringComparer.Ordinal);
                        names.Add(consumer);
                    }
                    void Group(ulong hash)
                    {
                        if (hash == EmptyHash) return;
                        if (!groupRows.TryGetValue(hash, out var group)) throw new InvalidDataException("A PLA outbreak group link is unresolved.");
                        if (!Groups.TryGetValue(hash, out var paths)) Groups[hash] = paths = new(StringComparer.Ordinal);
                        paths.Add(encounterPath);
                        Add(groups.Hash(group, 2));
                        foreach (var detail in groups.VectorReferences(group, 1, 64).Select(groups.TableAtReference)) Add(groups.Hash(detail, 7));
                    }
                    foreach (var link in b.VectorReferences(row, 20, 64).Select(b.TableAtReference))
                    {
                        var hash = b.Hash(link, 0);
                        if (massive && lotteryRows.TryGetValue(hash, out var selection))
                        {
                            Lotteries.Add(hash);
                            foreach (var category in new[] { 1, 2, 3 })
                                foreach (var entry in lottery.VectorReferences(selection, category, 1024).Select(lottery.TableAtReference)) Group(lottery.Hash(entry, 0));
                        }
                        else if (massive && groupRows.ContainsKey(hash)) Group(hash);
                        else Add(hash);
                    }
                }
            }
            var itemPath = areas.String(area, 36); var placementPath = areas.String(area, 34);
            if (itemPath.Length == 0 || placementPath.Length == 0 || !archive.Contains(itemPath) || !archive.Contains(placementPath)) continue;
            var items = new PlaFlatBuffer(archive.Read(itemPath, runtime)); var itemIds = Unique(items);
            PlacementPaths.Add(placementPath);
            var placement = new PlaFlatBuffer(archive.Read(placementPath, runtime));
            if (!Items.TryGetValue(itemPath, out var references)) Items[itemPath] = references = [];
            foreach (var row in placement.VectorReferences(placement.Root, 0, 4096).Select(placement.TableAtReference))
            {
                var hash = placement.Hash(row, 3);
                if (!itemIds.ContainsKey(hash)) throw new InvalidDataException("A PLA distortion item link is unresolved.");
                references.Add(hash);
            }
        }
    }

    private static Dictionary<ulong, int> Unique(PlaFlatBuffer b)
    {
        var result = new Dictionary<ulong, int>();
        foreach (var row in b.VectorReferences(b.Root, 0, 4096).Select(b.TableAtReference))
            if (!result.TryAdd(b.Hash(row, 0), row)) throw new InvalidDataException("A PLA event resource has duplicate identities.");
        return result;
    }
}
