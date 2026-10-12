// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.SwSh.Editing;

/// <summary>Tracks every effective and base input used by coordinated scene edits.</summary>
internal sealed class SwShSceneSources(OpenedProject project)
{
    private readonly Dictionary<string, ProjectFileGraphEntry> graph = project.FileGraph.Entries
        .ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, byte[]> Outputs { get; } = new(StringComparer.Ordinal);
    internal HashSet<ProjectFileReference> Sources { get; } = [];
    internal bool Contains(string path) => graph.ContainsKey(path);

    internal byte[] Read(string path, bool vanilla = false)
    {
        if (!vanilla && Outputs.TryGetValue(path, out var output)) return output;
        if (!graph.TryGetValue(path, out var entry)) throw new InvalidDataException($"Scene source is missing: {path}.");
        var reference = vanilla ? entry.BaseFile : entry.LayeredFile ?? entry.BaseFile;
        if (reference is null) throw new InvalidDataException($"Scene base source is missing: {path}.");
        Sources.Add(reference);
        var root = reference.Layer == ProjectFileLayer.Base ? project.Paths.BaseRomFsPath : project.Paths.OutputRootPath;
        if (string.IsNullOrWhiteSpace(root) || !path.StartsWith("romfs/", StringComparison.Ordinal))
            throw new InvalidDataException("Scene source root is unavailable.");
        return File.ReadAllBytes(Path.Combine(root, reference.Layer == ProjectFileLayer.Base ? path[6..] : path));
    }

    internal void VerifyModel(int species, int form)
    {
        var catalog = new ModelBuffer(Read("romfs/bin/pokemon/table/poke_resource_table.gfbpmcatalog"));
        var row = catalog.Tables(catalog.Root, 1, 8192).FirstOrDefault(row =>
        {
            var id = catalog.Table(row, 0);
            int Number(int field) => catalog.Field(id, field) is var at && at != 0 ? catalog.U16(at) : 0;
            return id != 0 && Number(0) == species && Number(1) == form;
        });
        if (row == 0) throw new InvalidDataException("The selected Pokemon form has no field model in the current catalog.");
        var archive = catalog.Text(row, 3) ?? throw new InvalidDataException("The Pokemon model archive is missing.");
        var model = catalog.Text(row, 1) ?? throw new InvalidDataException("The Pokemon model resource is missing.");
        if (Contains("romfs/" + model)) _ = Read("romfs/" + model);
        else if (!SwShGfPackFile.Parse(Read("romfs/" + archive)).ContainsFileName(Path.GetFileName(model)))
            throw new InvalidDataException("The Pokemon model resource is unavailable.");
    }

    internal (SwShGameTextFile Current, SwShGameTextFile Vanilla) ReadTextPair(string path)
    {
        var current = SwShGameTextFile.Parse(Read(path));
        var vanilla = SwShGameTextFile.Parse(Read(path, true));
        var keys = SwShAhtbFile.Parse(Read(Path.ChangeExtension(path, ".tbl"))).Entries;
        var originals = SwShAhtbFile.Parse(Read(Path.ChangeExtension(path, ".tbl"), true)).Entries;
        if (current.Lines.Count < vanilla.Lines.Count || keys.Count < vanilla.Lines.Count || originals.Count < vanilla.Lines.Count
            || !keys.Take(vanilla.Lines.Count).SequenceEqual(originals.Take(vanilla.Lines.Count)))
            throw new InvalidDataException("Scene dialogue has an unsupported message layout.");
        return (current, vanilla);
    }
}
