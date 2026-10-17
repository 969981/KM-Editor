// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.PLA;

namespace KM.PLA.Models;

public sealed record PlaModelCatalogEntry(string Id, int Species, int Form, int Gender, string Name, string Category = "pokemon", bool Shiny = false);

public sealed class PlaModelPreviewService
{
    public IReadOnlyList<PlaModelCatalogEntry> Catalog(OpenedProject project)
    {
        using var scope = new PlaSourceScope(project.Paths);
        var source = new PlaModelSource(scope);
        var path = $"bin/message/{scope.Language}/common/monsname";
        var names = new PlaMessageFile(scope.Read(path + ".dat"), scope.Read(path + ".tbl")).Lines;
        var result = new List<PlaModelCatalogEntry>();
        foreach (var entry in source.Catalog())
        {
            var model = new PlaModelCatalogEntry(entry.Model, entry.Species, entry.Form, entry.Gender,
                entry.Species < names.Count ? names[entry.Species].Text : entry.Species.ToString(System.Globalization.CultureInfo.InvariantCulture));
            result.Add(model);
            // The catalog declares the variant table. Resolve its contents only when selected.
            if (!string.IsNullOrEmpty(entry.Materials)) result.Add(model with { Id = model.Id + "#shiny", Shiny = true });
        }
        scope.VerifyOutput();
        return result.OrderBy(entry => entry.Species).ThenBy(entry => entry.Form).ThenBy(entry => entry.Gender).ThenBy(entry => entry.Shiny).ToArray();
    }

    public ModelTextureResource[] Textures(OpenedProject project, string id) => ModelTextureResources.Capture(observe => Prepare(project, id, "rest", observe));

    public ModelTextureResource[] Assets(OpenedProject project, string id, bool vanilla = false)
    {
        using var scope = new PlaSourceScope(project.Paths);
        var result = new PlaModelSource(scope).Assets(id, vanilla);
        scope.VerifyOutput();
        return result;
    }

    public ModelTextureResource[] RestorationAssets(OpenedProject project, string id)
    {
        using var scope = new PlaSourceScope(project.Paths);
        var source = new PlaModelSource(scope);
        var result = source.Assets(id, true).Select(asset => asset with { Bytes = source.Read(asset.Id) }).ToArray();
        scope.VerifyOutput();
        return result;
    }

    public PreviewScene Prepare(OpenedProject project, string id, string? animation = null,
        Action<string, byte[], string?>? observe = null, Func<string, byte[], byte[]>? transform = null)
    {
        using var scope = new PlaSourceScope(project.Paths);
        var source = new PlaModelSource(scope);
        var entry = source.Select(id);
        byte[] Read(string path)
        {
            var bytes = source.Read(path);
            observe?.Invoke(path, bytes, PlaModelSource.ArchivePath(path));
            return transform?.Invoke(path, bytes) ?? bytes;
        }
        PreviewMaterialVariant? variant = null;
        if (id.EndsWith("#shiny", StringComparison.Ordinal))
        {
            var table = entry.Materials ?? throw new InvalidDataException("This PLA model has no shiny material variant.");
            variant = PreviewMaterialVariant.Shiny(new(Read(table)), table)
                ?? throw new InvalidDataException("This PLA model has no shiny material variant.");
        }
        var scene = ModelPreviewCache.Load("PLA", entry.Model, variant, Read, read => new TrinityPreviewReader(read, topOriginMaterialUv: true).Load(entry.Model, variant));
        var available = source.Animations(entry).Clips;
        var chosen = animation == "rest" ? null : animation is null
            ? available.FirstOrDefault(clip => clip.Id == "00000_defaultwait01_loop") ?? available.FirstOrDefault()
            : available.SingleOrDefault(clip => clip.Id == animation) ?? throw new InvalidDataException("Select an animation associated with this PLA model.");
        var warnings = scene.Rig.Warnings.ToList();
        PreviewClip? clip = null;
        if (chosen?.Skeleton is { } skeletal)
        {
            try { clip = PreviewRigReader.Animation(new(Read(skeletal)), chosen.Id, scene.Rig.Bones); }
            catch (IOException) { warnings.Add("animationUnsupported"); }
        }
        if (clip is not null && chosen?.Material is { } material)
        {
            try { clip = PreviewMaterialAnimation.Read(new(Read(material)), clip, warnings, highlightAnimation: true); }
            catch (IOException) { warnings.Add("materialAnimationUnsupported"); }
        }
        scope.VerifyOutput();
        return scene with { Rig = new(scene.Rig.Bones, available, clip, warnings.Distinct().ToArray()) };
    }
}
