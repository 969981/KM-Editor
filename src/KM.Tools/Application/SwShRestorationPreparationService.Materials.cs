// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static PreviewScene UniqueMaterials(PreviewScene scene)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return scene with { Primitives = scene.Primitives.Select((primitive, index) =>
            {
                var name = primitive.Material.Name;
                if (!names.Add(name))
                {
                    name += "_mesh" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!names.Add(name)) throw new InvalidDataException("Converted material name is ambiguous.");
                }
                return primitive with { Material = primitive.Material with { Name = name } };
            }).ToArray() };
        }

        private SwShBakedMaterial?[] BakePalette(PreviewScene scene, byte[] materialBytes, bool rare, string sourcePath,
            RestorationTexturePool textures, bool nativeFlames = false)
        {
            return scene.Primitives.Select((primitive, index) =>
            {
                if (nativeFlames && primitive.Material.Name == "fire") return null;
                Report("textures", (rare ? scene.Primitives.Count : 0) + index, scene.Primitives.Count * 2);
                var material = SwShMaterialBaker.Bake(primitive, materialBytes, index,
                    name => Donor(TrinityPreviewReader.Resolve(sourcePath, name)), token, sourceNormalDimensions: Profiles[currentSpecies].CompactResources);
                // Eyes can share a layer mask while having different palette colors.
                // Give each material and palette its own native texture identity.
                (string Name, byte[] Bytes) Add(string original, byte[] bytes, char prefix)
                {
                    var name = prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + original[2..];
                    var renamed = ModelTextureIdentity.Rename(bytes, original, name);
                    return (textures.Add(name, renamed), renamed);
                }
                var color = Add(material.ColorName, material.Color, rare ? 'd' : 'c');
                var normal = Add(material.NormalName, material.Normal, rare ? 'o' : 'n');
                var ambient = Add(material.AmbientName, material.Ambient, rare ? 'b' : 'a');
                Report("textures", (rare ? scene.Primitives.Count : 0) + index + 1, scene.Primitives.Count * 2);
                return material with { ColorName = color.Name, Color = color.Bytes, NormalName = normal.Name, Normal = normal.Bytes,
                    AmbientName = ambient.Name, Ambient = ambient.Bytes };
            }).ToArray();
        }

        private static SwShEffectLocator[] SourceLocators(PreviewScene scene, IReadOnlyDictionary<string, string> aliases, byte[] bytes, Vector3? mouthPosition = null)
        {
            var source = new ModelBuffer(bytes); var nodes = source.Tables(source.Root, 1, 512);
            var transforms = PreviewRigReader.Skeleton(source);
            var entries = transforms.Select((bone, index) => (Bone: bone, Parent: source.Text(nodes[index], 6)))
                .ToDictionary(e => e.Bone.Name, StringComparer.Ordinal);
            SwShEffectLocator Convert(string name, string original)
            {
                if (!entries.TryGetValue(original, out var entry) || entry.Bone.Parent != -1 || entry.Bone.Scale.Any(v => !float.IsFinite(v) || MathF.Abs(v - 1) > .00001f)
                    || entry.Parent is null || !scene.Rig.Bones.Any(b => b.Name == entry.Parent))
                    throw new InvalidDataException("S/V effect attachment requires another target adapter.");
                var p = entry.Bone.Translation; var q = entry.Bone.Rotation;
                return new(name, aliases[entry.Parent], new(p[0] * 100, p[1] * 100, p[2] * 100)) { Rotation = new(q[0], q[1], q[2], q[3]) };
            }
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EffMouth01"] = "eff_mouth01", ["EffOverHead01"] = "eff_overhead01", ["EffEye01"] = "eff_eye01", ["EffEye02"] = "eff_eye02",
                ["EffHeadCenter01"] = "eff_headcenter01", ["EffFront01"] = "eff_front01", ["EffCenter01"] = "eff_center01",
                ["EffShoot01_01"] = "eff_rangeattack01", ["EffShoot01_02"] = "eff_rangeattack02",
                ["EffAttack01_01"] = "eff_attack01", ["EffAttack01_02"] = "eff_attack02",
                ["EffGcloud01"] = "eff_overhead01",
            };
            var result = mapping.Select(pair => Convert(pair.Key, pair.Value)).ToList();
            if (mouthPosition is { } mouth)
            {
                // A feeding target fixed to Origin cannot follow the face. The meal
                // camera needs a mouth attachment in the animated head's local space.
                if (!float.IsFinite(mouth.X) || !float.IsFinite(mouth.Y) || !float.IsFinite(mouth.Z)
                    || !aliases.TryGetValue("head", out var head) || !scene.Rig.Bones.Any(b => b.Name == "head"))
                    throw new InvalidDataException("S/V mouth attachment requires another target adapter.");
                var reference = Convert("EffMouth01", "eff_headcenter01");
                if (reference.Parent != head) throw new InvalidDataException("S/V mouth attachment requires another target adapter.");
                result[result.FindIndex(locator => locator.Name == "EffMouth01")] = reference with { Translation = mouth * 100 };
            }
            foreach (var (name, candidates) in new[] { ("EffHand01", new[] { "right_hand" }), ("EffHand02", new[] { "left_hand" }),
                ("EffFoot01", new[] { "right_foot", "right_foot_a" }), ("EffFoot02", new[] { "left_foot", "left_foot_a" }) })
            {
                var parent = candidates.FirstOrDefault(c => scene.Rig.Bones.Any(b => b.Name == c));
                result.Add(parent is null ? Convert(name, "eff_center01") : new(name, aliases[parent], Vector3.Zero));
            }
            return result.ToArray();
        }
    }
}
