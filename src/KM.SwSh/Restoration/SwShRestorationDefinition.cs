// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.SwSh.Pokemon;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

public enum SwShRestorationSupport { NotSupported, Testing, Ready }
public enum SwShRestorationInstallation { Disabled, Enabled, NeedsRepair, External }
public sealed record SwShRestorationDefinition(string Id, int Version, int ConverterVersion,
    IReadOnlyList<SwShPokemonIdentity> Identities, IReadOnlyDictionary<ProjectGame, SwShRestorationSupport> Support,
    IReadOnlyList<string> RequiredContexts);

public static class SwShRestorationDefinitions
{
    public const string Domain = "workflow.restoreMissingPokemon";
    public static readonly SwShRestorationDefinition Piplup = new("piplup-family", 1, 1,
        Array.AsReadOnly(new[] { new SwShPokemonIdentity(393, 0), new SwShPokemonIdentity(394, 0), new SwShPokemonIdentity(395, 0) }),
        new System.Collections.ObjectModel.ReadOnlyDictionary<ProjectGame, SwShRestorationSupport>(new Dictionary<ProjectGame, SwShRestorationSupport>
        {
            [ProjectGame.Sword] = SwShRestorationSupport.Testing, [ProjectGame.Shield] = SwShRestorationSupport.Testing,
        }), Array.AsReadOnly(new[] { "summary", "battle", "field", "evolution", "breeding", "camp", "dynamax" }));
    public static IReadOnlyList<SwShRestorationDefinition> All { get; } = Array.AsReadOnly(new[] { Piplup });
    public static SwShRestorationDefinition Get(string id) => All.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidDataException("This restoration definition is not supported.");
}

public sealed record SwShRestorationCatalogEntry(int SpeciesId, string Name, string? DefinitionId,
    SwShRestorationSupport Support, SwShRestorationInstallation Installation, IReadOnlyList<int> Family);
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
        var entries = catalog.Entries.GroupBy(e => e.Identity.Species).Where(g => g.All(e => e.Origin != SwShPokemonOrigin.Native))
            .Select(group =>
            {
                var definition = SwShRestorationDefinitions.All.SingleOrDefault(d => d.Identities.Any(i => i.Species == group.Key));
                var selection = state.State.Selections.SingleOrDefault(s => s.DefinitionId == definition?.Id);
                var complete = group.Any(e => e.Capabilities.HasFlag(SwShPokemonCapability.Assignment));
                return new SwShRestorationCatalogEntry(group.Key,
                    group.Key < names.Count && !string.IsNullOrWhiteSpace(names[group.Key]) ? names[group.Key] : $"Pokemon {group.Key}",
                    definition?.Id, paths.SelectedGame is { } game && definition is not null ? definition.Support.GetValueOrDefault(game) : SwShRestorationSupport.NotSupported,
                    selection?.Enabled == true ? complete ? SwShRestorationInstallation.Enabled : SwShRestorationInstallation.NeedsRepair
                        : group.Any(e => e.Personal.IsPresentInGame) ? SwShRestorationInstallation.External : SwShRestorationInstallation.Disabled,
                    definition?.Identities.Select(i => i.Species).Distinct().ToArray() ?? [group.Key]);
            }).OrderBy(e => e.SpeciesId).ToArray();
        return new(catalog.Revision, entries, null);
    }
}
