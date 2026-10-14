// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationIconUpdate
{
    internal static SwShPreparedRestoration Rebase(SwShPreparedRestoration previous, SwShPreparedRestoration next,
        SwShPreparedRestoration customized, out SwShRestorationContribution[] changed)
    {
        if (previous.DefinitionId != next.DefinitionId || previous.Game != next.Game || previous.DefinitionVersion != next.DefinitionVersion
            || previous.ConverterVersion != next.ConverterVersion || customized.DefinitionId != next.DefinitionId
            || customized.Game != next.Game || customized.DefinitionVersion != next.DefinitionVersion || customized.ConverterVersion != next.ConverterVersion)
            throw new InvalidDataException("Restoration updates require matching definitions and targets.");
        var icons = SwShRestorationDefinitions.Get(next.DefinitionId).Identities.Select(i =>
            $"romfs/bin/appli/icon_pokemon/poke_icon_{i.Species:0000}_{i.Form:00}s_n.bntx").ToHashSet(StringComparer.Ordinal);
        var old = previous.Contributions.ToDictionary(c => (c.Path, c.Key));
        if (old.Count != next.Contributions.Count) throw new InvalidDataException("Restoration update changes resource ownership.");
        var updates = new List<SwShRestorationContribution>();
        foreach (var item in next.Contributions)
        {
            if (!old.TryGetValue((item.Path, item.Key), out var original) || original.Writer != item.Writer || !Equal(original.Before, item.Before))
                throw new InvalidDataException("Restoration update changes resource ownership.");
            if (Equal(original.After, item.After)) continue;
            if (item.Writer != SwShRestorationWriter.File || !icons.Contains(item.Path))
                throw new InvalidDataException("Only party icon updates are supported for installed restorations.");
            var image = new ModelTextureDocument(item.After);
            if (image.Width != 136 || image.Height != 112 || image.MipCount != 1)
                throw new InvalidDataException("Updated party icon layout is invalid.");
            updates.Add(item with { Before = original.After });
        }
        var replacements = next.Contributions.ToDictionary(c => (c.Path, c.Key));
        changed = updates.ToArray();
        // Keep edited models, gameplay, loose resources, and custom icons. Only
        // a value that still equals the previous default follows the new default.
        return next with { Contributions = customized.Contributions.Select(item =>
            old.TryGetValue((item.Path, item.Key), out var original) && Equal(original.After, item.After)
                ? replacements[(item.Path, item.Key)] : item).ToArray() };
    }
    private static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
