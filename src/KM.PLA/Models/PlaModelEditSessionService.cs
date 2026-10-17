// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Models;

namespace KM.PLA.Models;

public sealed class PlaModelEditSessionService
{
    public EditSession Stage(ProjectPaths paths, EditSession? session, ModelTextureIntent intent)
    {
        intent.Validate();
        if (intent.Game != "PLA" || intent.Kind == "restore")
            throw new InvalidDataException("Select a PLA model edit, or use Restore Vanilla for restoration.");
        using var scope = new PlaSourceScope(paths);
        var source = new PlaModelSource(scope);
        var resource = source.Assets(intent.Model).SingleOrDefault(asset => asset.Id == intent.Texture)
            ?? throw new InvalidDataException("Select an asset belonging to this PLA model.");
        var encoded = ModelTextureEncodingCache.Encode(resource.Bytes, intent);
        var current = session ?? EditSession.Start();
        var edits = current.PendingEdits.Where(edit => edit.Domain != ModelTextureIntent.Domain || edit.RecordId != intent.Texture).ToList();
        if (encoded.ChangedPixels > 0)
            edits.Add(Edit(intent with { EncodedHash = ModelTextureIntent.Hash(encoded.Bytes) }));
        if (edits.Count(edit => edit.Domain == ModelTextureIntent.Domain) > 2048)
            throw new InvalidDataException("Too many staged PLA model edits.");
        scope.VerifyOutput();
        return current with { PendingEdits = edits };
    }

    public EditSession RestoreVanilla(ProjectPaths paths, EditSession? session, string model)
    {
        using var scope = new PlaSourceScope(paths);
        var source = new PlaModelSource(scope);
        var assets = source.Assets(model, true);
        var ids = assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var current = session ?? EditSession.Start();
        var edits = current.PendingEdits.Where(edit => edit.Domain != ModelTextureIntent.Domain
            || ModelTextureIntent.Parse(edit.NewValue).Model != model && (edit.RecordId is null || !ids.Contains(edit.RecordId))).ToList();
        foreach (var asset in assets)
        {
            var bytes = source.Read(asset.Id);
            if (!bytes.AsSpan().SequenceEqual(asset.Bytes))
                edits.Add(Edit(new("PLA", model, asset.Id, ModelTextureIntent.Hash(bytes), [], ModelTextureIntent.Hash(asset.Bytes), "restore")));
        }
        if (edits.Count(edit => edit.Domain == ModelTextureIntent.Domain) > 2048)
            throw new InvalidDataException("Too many staged PLA model restorations.");
        scope.VerifyOutput();
        return current with { PendingEdits = edits };
    }

    private static PendingEdit Edit(ModelTextureIntent intent) => new(ModelTextureIntent.Domain,
        $"{(intent.Kind == "restore" ? "Restore" : intent.Kind == "material" ? "Edit materials in" : "Recolor")} {Path.GetFileName(intent.Texture)}",
        [new(ProjectFileLayer.Base, "romfs/" + PlaModelSource.ArchivePath(intent.Texture))], intent.Texture, "recolor", intent.Serialize(), ModelTextureIntent.Domain);

    internal static Dictionary<string, byte[]> BuildFiles(PlaSourceScope scope, IEnumerable<PendingEdit> pending)
    {
        var edits = pending.ToArray();
        if (edits.Length == 0) return new(StringComparer.Ordinal);
        if (edits.Length > 2048) throw new InvalidDataException("Too many staged PLA model edits.");
        var source = new PlaModelSource(scope);
        var graphs = new Dictionary<(string Model, bool Vanilla), Dictionary<string, ModelTextureResource>>();
        var members = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
        long total = 0;
        foreach (var edit in edits)
        {
            if (edit.Domain != ModelTextureIntent.Domain || edit.Owner != ModelTextureIntent.Domain || edit.Field != "recolor")
                throw new InvalidDataException("The PLA model edit is unsupported.");
            var intent = ModelTextureIntent.Parse(edit.NewValue);
            if (intent.Game != "PLA" || intent.Texture != edit.RecordId || intent.EncodedHash == "")
                throw new InvalidDataException("The PLA model edit binding is invalid.");
            var key = (intent.Model, intent.Kind == "restore");
            if (!graphs.TryGetValue(key, out var graph))
                graphs.Add(key, graph = source.Assets(key.Model, key.Item2).ToDictionary(asset => asset.Id, StringComparer.Ordinal));
            if (!graph.TryGetValue(intent.Texture, out var asset))
                throw new InvalidDataException("The PLA asset is no longer associated with this model.");
            var result = ModelTextureEncodingCache.Encode(source.Read(asset.Id), intent, intent.Kind == "restore" ? asset.Bytes : null);
            var archive = PlaModelSource.ArchivePath(asset.Id);
            if (!members.TryGetValue(archive, out var changes)) members.Add(archive, changes = new(StringComparer.Ordinal));
            if (result.ChangedPixels == 0 || !changes.TryAdd(asset.Id, result.Bytes))
                throw new InvalidDataException("The PLA model edits conflict or contain no changes.");
            total += result.Bytes.Length;
            if (total > 64L * 1024 * 1024) throw new InvalidDataException("The PLA model output exceeds the editing budget.");
        }
        var files = members.ToDictionary(pair => pair.Key, pair => source.Archive(pair.Key).Compose(pair.Value, scope.Compression), StringComparer.Ordinal);
        scope.VerifyOutput();
        return files;
    }
}
