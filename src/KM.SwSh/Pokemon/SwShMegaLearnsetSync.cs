// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.Restoration;

namespace KM.SwSh.Pokemon;

public static class SwShMegaLearnsetSync
{
    public static int? BaseForm(SwShPokemonRecord pokemon)
    {
        // Native forms can occupy the same identity as a planned restoration.
        // Require restored assets as well as a canonical Mega identity.
        if (!pokemon.HasRestoredAssets || pokemon.Personal.IsRegionalForm
            || SwShRestorationFormCandidates.ResolveFormLabel(pokemon.SpeciesId, pokemon.CanonicalForm)
                ?.StartsWith("Mega", StringComparison.Ordinal) != true)
            return null;

        return (pokemon.SpeciesId, pokemon.CanonicalForm) switch
        {
            (670, 6) => 5, // Eternal Flower Floette
            (678, 3) => 1, // Female Meowstic
            (801, 3) => 1, // Original Color Magearna
            _ => 0,
        };
    }
}
