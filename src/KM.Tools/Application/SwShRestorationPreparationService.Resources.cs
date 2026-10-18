// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed class RestorationTexturePool(Dictionary<string, byte[]> members, string folder, bool share)
    {
        private readonly Dictionary<string, string> identities = new(StringComparer.Ordinal);

        internal string Add(string name, byte[] bytes)
        {
            if (share)
            {
                // Compare the entire container, including every mip and sampling field.
                // A palette or material name alone is never sufficient for sharing.
                var neutral = ModelTextureIdentity.Rename(bytes, name, new string('x', name.Length));
                var key = Convert.ToHexString(SHA256.HashData(neutral));
                if (identities.TryGetValue(key, out var existing)) return existing;
                identities.Add(key, name);
            }
            if (!members.TryAdd(folder + name + ".bntx", bytes))
                throw new InvalidDataException("Converted texture identity is duplicated.");
            return name;
        }
    }

    private sealed partial class Preparation
    {
        private static string[] ModelNames(ModelBuffer model, int field)
        {
            var (at, count) = model.Vector(model.Root, field, 4, 256);
            return Enumerable.Range(0, count).Select(i => model.StringAt(at + 4 * i)).ToArray();
        }

        private static byte[] CompactPrograms(byte[] bytes)
        {
            var model = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
            var materials = model.Tables(model.Root, 6, 128);
            foreach (var (list, index) in new[] { (3, 8), (5, 10) })
            {
                var original = ModelNames(model, list);
                string Program(int material)
                {
                    var slot = model.Value(material, index);
                    return slot < original.Length ? original[slot] : throw new InvalidDataException("Native shader index is invalid.");
                }
                var referenced = materials.Select(Program).ToHashSet(StringComparer.Ordinal);
                var names = original.Where(referenced.Contains).ToArray();
                var slots = writer.Vector(model.Root, list, names.Length);
                for (var i = 0; i < names.Length; i++) writer.Pointer(slots[i], writer.String(names[i]));
                foreach (var material in materials)
                {
                    var mapped = Array.IndexOf(names, Program(material));
                    if (model.Field(material, index) != 0) writer.Int32(writer.Field(material, index), mapped);
                    else if (mapped != 0) throw new InvalidDataException("Native implicit shader index cannot be remapped.");
                }
            }
            return writer.Finish(model.Root);
        }

        private static Dictionary<string, byte[]> PaletteMembers(Dictionary<string, byte[]> members, string modelPath)
        {
            var model = new ModelBuffer(members[modelPath]); var folder = modelPath[..(modelPath.LastIndexOf('/') + 1)];
            var textures = ModelNames(model, 2).Select(n => folder + n + ".bntx").ToHashSet(StringComparer.Ordinal);
            var shaders = new[] { 3, 5 }.SelectMany(f => ModelNames(model, f)).Distinct(StringComparer.Ordinal)
                .SelectMany(n => new[] { folder + n + ".bnsh_vsh", folder + n + ".bnsh_fsh" }).ToHashSet(StringComparer.Ordinal);
            if (textures.Concat(shaders).Any(path => !members.ContainsKey(path)))
                throw new InvalidDataException("Converted palette resource is missing.");
            var selected = members.Where(pair => pair.Key.EndsWith(".gfbmdl", StringComparison.Ordinal) ? pair.Key == modelPath
                : pair.Key.EndsWith(".bntx", StringComparison.Ordinal) ? textures.Contains(pair.Key)
                : pair.Key.EndsWith(".bnsh_vsh", StringComparison.Ordinal) || pair.Key.EndsWith(".bnsh_fsh", StringComparison.Ordinal) ? shaders.Contains(pair.Key)
                : true).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            SwShRestorationResourceBudget.Validate(selected);
            return selected;
        }

        private static PreviewScene CompactGeometry(PreviewScene scene) => scene with
        {
            Primitives = scene.Primitives.Select(primitive =>
            {
                // Material sections can each expose the whole shared source buffer.
                // Preserve every triangle corner and skin attribute, omitting only unused vertices.
                var used = primitive.Indices.Distinct().Order().ToArray();
                var remap = used.Select((source, index) => (source, index))
                    .ToDictionary(pair => pair.source, pair => (uint)pair.index);
                return primitive with
                {
                    Vertices = used.SelectMany(index => primitive.Vertices.Skip(checked((int)index * 16)).Take(16)).ToArray(),
                    Indices = primitive.Indices.Select(index => remap[index]).ToArray(),
                };
            }).ToArray(),
        };
    }
}
