// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

/// <summary>Preserves existing family installations as independently owned species.</summary>
public static class SwShRestorationCompatibility
{
    private static readonly IReadOnlyDictionary<int, IReadOnlySet<uint>> VoiceObjects = SwShRestorationDefinitions.All
        .SelectMany(d => d.Identities).ToDictionary(i => i.Species, i => SwShVoiceWriter.ObjectIds(i.Species));
    private static readonly IReadOnlyDictionary<int, HashSet<uint>> VoiceMedia = SwShRestorationDefinitions.All
        .SelectMany(d => d.Identities).ToDictionary(i => i.Species, i => SwShVoiceWriter.States.Select(s => SwShVoiceWriter.MediaId(i.Species, s)).ToHashSet());
    public static SwShRestorationState Normalize(ProjectPaths paths, SwShRestorationState state)
    {
        var legacy = state.Selections.SingleOrDefault(s => s.DefinitionId == SwShRestorationDefinitions.Piplup.Id);
        if (legacy is null) return state;
        var selections = state.Selections.Where(s => s != legacy).ToList();
        foreach (var definition in SwShRestorationDefinitions.PiplupSpecies)
        {
            if (selections.Any(s => s.DefinitionId == definition.Id)) throw new InvalidDataException("Family and individual restoration records overlap.");
            selections.Add(new(definition.Id, Split(paths, legacy.PreparedHash, definition.Id), legacy.Enabled,
                legacy.CustomizationHash is { } custom ? Split(paths, custom, definition.Id) : null) { AssetRevision = legacy.AssetRevision });
        }
        return state with { Selections = selections.OrderBy(s => s.DefinitionId, StringComparer.Ordinal).ToArray() };
    }

    public static string Split(ProjectPaths paths, string hash, string definitionId)
    {
        var prepared = SwShRestorationStateStore.ReadPrepared(paths, hash);
        if (prepared.DefinitionId == definitionId) return hash;
        if (prepared.DefinitionId != SwShRestorationDefinitions.Piplup.Id) throw new InvalidDataException("Prepared restoration does not match its selection.");
        var definition = SwShRestorationDefinitions.Get(definitionId);
        if (!SwShRestorationDefinitions.PiplupSpecies.Contains(definition)) throw new InvalidDataException("An individual member of the original family is required.");
        var all = SwShRestorationDefinitions.PiplupSpecies.ToDictionary(d => d.Id, d => prepared.Contributions.Where(c => Owns(d, c)).ToArray());
        if (all.Values.Sum(c => c.Length) != prepared.Contributions.Count || prepared.Contributions.Any(c => SwShRestorationDefinitions.PiplupSpecies.Count(d => Owns(d, c)) != 1))
            throw new InvalidDataException("Existing family resources cannot be separated without losing ownership.");
        var split = prepared with { DefinitionId = definition.Id, DefinitionVersion = definition.Version, ConverterVersion = definition.ConverterVersion,
            Contributions = all[definition.Id], UnavailableSourceMoves = prepared.UnavailableSourceMoves.Where(p => definition.Identities.Any(i => i.Species == p.Key)).ToDictionary() };
        var stored = SwShRestorationStateStore.Store(paths, split); using (stored.Lease) { }
        return stored.Hash;
    }

    private static bool Owns(SwShRestorationDefinition definition, SwShRestorationContribution item)
    {
        var species = definition.Identities.Single().Species;
        var number = species.ToString(CultureInfo.InvariantCulture);
        return item.Writer switch
        {
            SwShRestorationWriter.Personal or SwShRestorationWriter.Learnset or SwShRestorationWriter.Icons => item.Key == number,
            SwShRestorationWriter.ModelCatalog => item.Key.StartsWith(number + ":0:", StringComparison.Ordinal),
            SwShRestorationWriter.Camp => item.Key.EndsWith($"poke_{species:0000}_XX_X.bin", StringComparison.Ordinal)
                || new[] { "motion", "face", "ball", "turning" }.Any(g => item.Key.EndsWith($"poke_{g}_{species:0000}_00_0.bin", StringComparison.Ordinal)),
            SwShRestorationWriter.File => item.Path == $"romfs/bin/archive/pokemon/pm{species:0000}_00.gfpak"
                || item.Path == $"romfs/bin/archive/gpokemon/pm{species:0000}_00.gfpak"
                || item.Path == $"romfs/bin/appli/icon_pokemon/poke_icon_{species:0000}_00s_n.bntx"
                || item.Path == $"romfs/bin/pml/evolution/evo_{species:000}.bin"
                || item.Path == $"romfs/bin/pml/tamagowaza/tamagowaza_{species:0000}.bin"
                || item.Path.StartsWith($"romfs/bin/pokemon/pm{species:0000}_00/", StringComparison.Ordinal),
            SwShRestorationWriter.Voices => uint.TryParse(item.Key.Split(':').Last(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && (item.Key.StartsWith("media:", StringComparison.Ordinal) ? VoiceMedia[species].Contains(id) : VoiceObjects[species].Contains(id)),
            _ => false,
        };
    }
}
