// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.SwSh.Pokemon;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

public enum SwShRestorationSupport { NotSupported, Testing, Ready }
public enum SwShRestorationInstallation { Disabled, Enabled, NeedsRepair, External }
public enum SwShRestorationDonor { ScarletViolet, LegendsZA, LegendsArceus }
public sealed record SwShRestorationModelVariant(SwShPokemonIdentity Identity, int Gender, string ResourceStem);
public sealed record SwShRestorationDefinition(string Id, int Version, int ConverterVersion,
    IReadOnlyList<SwShPokemonIdentity> Identities, IReadOnlyDictionary<ProjectGame, SwShRestorationSupport> Support,
    IReadOnlyList<string> RequiredContexts)
{
    public SwShRestorationDonor Donor { get; init; } = SwShRestorationDonor.ScarletViolet;
    public IReadOnlyList<SwShRestorationModelVariant> AdditionalModelVariants { get; init; } = [];
}

public static class SwShRestorationDefinitions
{
    public const string Domain = "workflow.restoreMissingPokemon";
    public static readonly SwShRestorationDefinition Piplup = new("piplup-family", 1, 1,
        Array.AsReadOnly(new[] { new SwShPokemonIdentity(393, 0), new SwShPokemonIdentity(394, 0), new SwShPokemonIdentity(395, 0) }),
        new System.Collections.ObjectModel.ReadOnlyDictionary<ProjectGame, SwShRestorationSupport>(new Dictionary<ProjectGame, SwShRestorationSupport>
        {
            [ProjectGame.Sword] = SwShRestorationSupport.Testing, [ProjectGame.Shield] = SwShRestorationSupport.Testing,
        }), Array.AsReadOnly(new[] { "summary", "battle", "field", "evolution", "breeding", "camp", "dynamax" }));
    public static IReadOnlyList<SwShRestorationDefinition> PiplupSpecies { get; } = Array.AsReadOnly(new[]
    {
        Species("piplup", 393), Species("prinplup", 394), Species("empoleon", 395),
    });
    public static IReadOnlyList<SwShRestorationDefinition> All { get; } = Array.AsReadOnly(PiplupSpecies.Concat(new[]
    {
        Species("chikorita", 152) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("bayleef", 153) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("meganium", 154) with { Donor = SwShRestorationDonor.LegendsZA,
            AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(154, 0), 1, "pm0154_01") }) },
        Species("cyndaquil", 155), Species("quilava", 156), Species("typhlosion", 157),
        Species("totodile", 158) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("croconaw", 159) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("feraligatr", 160) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("snivy", 495), Species("servine", 496), Species("serperior", 497),
        Species("tepig", 498) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pignite", 499) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("emboar", 500) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("oshawott", 501), Species("dewott", 502), Species("samurott", 503),
        Species("ekans", 23), Species("arbok", 24),
        Species("turtwig", 387), Species("grotle", 388), Species("torterra", 389),
        Species("chimchar", 390), Species("monferno", 391), Species("infernape", 392),
        Species("weedle", 13) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("kakuna", 14) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("beedrill", 15) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pidgey", 16) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pidgeotto", 17) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pidgeot", 18) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("kecleon", 352) with { Donor = SwShRestorationDonor.LegendsZA },
    }).ToArray());
    private static SwShRestorationDefinition Species(string id, int species) => Piplup with
        { Id = id, Identities = Array.AsReadOnly(new[] { new SwShPokemonIdentity(species, 0) }) };
    public static SwShRestorationDefinition Get(string id) => id == Piplup.Id ? Piplup : All.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidDataException("This restoration definition is not supported.");
}

public sealed record SwShRestorationCatalogEntry(int SpeciesId, string Name, string? DefinitionId,
    SwShRestorationSupport Support, SwShRestorationInstallation Installation, IReadOnlyList<int> Family)
{
    public int Form { get; init; }
    public string? FormLabel { get; init; }
    public int? PlannedPersonalId { get; init; }
    public SwShRestorationDonor? Donor { get; init; }
}
public sealed record SwShRestorationCatalog(string Revision, IReadOnlyList<SwShRestorationCatalogEntry> Entries, string? UnavailableReason)
{
    public static SwShRestorationCatalog Discover(ProjectPaths paths)
    {
        var relative = SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.Resolve(paths), "monsname.dat");
        var file = Path.Combine(paths.OutputRootPath ?? "", relative);
        if (!File.Exists(file)) file = Path.Combine(paths.BaseRomFsPath ?? "", relative[6..]);
        var names = File.Exists(file) && new FileInfo(file).Length < 4 * 1024 * 1024
            ? SwShGameTextFile.Parse(File.ReadAllBytes(file), 2048).Lines.Select(l => l.Text).ToArray() : [];
        return Discover(paths, names);
    }
    public static SwShRestorationCatalog Discover(ProjectPaths paths, IReadOnlyList<string> names)
    {
        var catalog = SwShEffectivePokemonCatalog.Load(paths);
        if (!catalog.Available) return new(catalog.Revision, [], catalog.UnavailableReason);
        var state = SwShRestorationStateStore.Read(paths);
        var entries = catalog.Entries.Where(e => e.Identity.Form == 0).GroupBy(e => e.Identity.Species).Where(g => g.All(e => e.Origin != SwShPokemonOrigin.Native))
            .Select(group =>
            {
                var definition = SwShRestorationDefinitions.All.SingleOrDefault(d => d.Identities.Contains(new(group.Key, 0)));
                var selection = state.State.Selections.SingleOrDefault(s => s.DefinitionId == definition?.Id);
                var complete = group.Any(e => e.Capabilities.HasFlag(SwShPokemonCapability.Assignment));
                return new SwShRestorationCatalogEntry(group.Key,
                    group.Key < names.Count && !string.IsNullOrWhiteSpace(names[group.Key]) ? names[group.Key] : $"Pokemon {group.Key}",
                    definition?.Id, paths.SelectedGame is { } game && definition is not null ? definition.Support.GetValueOrDefault(game) : SwShRestorationSupport.NotSupported,
                    selection?.Enabled == true ? complete ? SwShRestorationInstallation.Enabled : SwShRestorationInstallation.NeedsRepair
                        : group.Any(e => e.Personal.IsPresentInGame) ? SwShRestorationInstallation.External : SwShRestorationInstallation.Disabled,
                    definition?.Identities.Select(i => i.Species).Distinct().ToArray() ?? [group.Key]) { Donor = definition?.Donor };
            });
        return new(catalog.Revision + ":forms-1", SwShRestorationFormCandidates.Include(entries, catalog, names), null);
    }
}
