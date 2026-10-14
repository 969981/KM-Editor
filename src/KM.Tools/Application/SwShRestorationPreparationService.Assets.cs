// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static readonly IReadOnlyDictionary<string, string> NativeClips = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ba01_landA01"] = "00152", ["ba01_landB01"] = "00153", ["ba01_landC01"] = "00155",
            ["ba10_waitA01"] = "00001", ["ba10_waitB01"] = "00010", ["ba20_buturi01"] = "00400", ["ba21_tokusyu01"] = "00450",
            ["ba30_damageS01"] = "00500", ["ba41_down01"] = "00520", ["eye01"] = "08000", ["mouth01"] = "08100", ["ba02_roar01"] = "00300",
            ["fi20_walk01"] = "00030", ["fi21_run01"] = "00100", ["kw20_drowseA01"] = "00280", ["kw20_drowseB01"] = "00281", ["kw20_drowseC01"] = "00282",
            ["kw21_sleepA01"] = "00280", ["kw21_sleepB01"] = "00281", ["kw30_hate01"] = "00563", ["kw32_happyA01"] = "00550", ["kw32_happyB01"] = "00011",
            ["kw32_happyC01"] = "00550", ["kw33_moveA01"] = "00150", ["kw33_moveC01"] = "00155", ["kw34_lonely01"] = "00560", ["kw35_playA01"] = "00011",
            ["kw36_mad01"] = "00563", ["kw50_eatA01"] = "00290", ["kw50_eatB01"] = "00291", ["kw50_eatC01"] = "00292", ["kw60_touch01"] = "00320",
        };
        internal IReadOnlyList<SwShRestorationContribution> Assets(int species)
        {
            currentSpecies = species; Report("reading");
            var stem = $"pm{species:0000}_00"; var sourceStem = stem + "_00"; var sourceFolder = $"pokemon/data/pm{species:0000}/{sourceStem}/";
            SwShRestorationGenderAudit.RequireSharedModel(Donor("pokemon/catalog/catalog/poke_resource_table.trpmcatalog"),
                species, 0, $"pm{species:0000}/{sourceStem}/{sourceStem}.trmdl");
            var graph = ModelAssetGraph.Read(new[] { (sourceFolder + sourceStem + ".trmdl", (string?)null),
                (sourceFolder + sourceStem + "_rare.trmtr", null), (sourceFolder + sourceStem + "_base.tracn", null) }, (path, _) => Donor(path));
            Report("reading");
            var scene = new TrinityPreviewReader(Donor).Load(sourceFolder + sourceStem + ".trmdl");
            if (scene.Rig.Warnings.Length > 0) throw new InvalidDataException("Source model requires an additional conversion adapter.");
            var nativePack = SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0007_00.gfpak"));
            var nativeModel = Member(nativePack, "pm0007_00.gfbmdl"); var nativeMetadata = new ModelBuffer(nativeModel);
            var materialBytes = Donor(sourceFolder + sourceStem + ".trmtr");
            var baked = scene.Primitives.Select((primitive, index) => {
                Report("textures", index, scene.Primitives.Count);
                var result = SwShMaterialBaker.Bake(primitive, materialBytes, index,
                    name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token);
                Report("textures", index + 1, scene.Primitives.Count); return result;
            }).ToArray();
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = "Origin", ["waist"] = "Waist", ["head"] = "Head", ["neck"] = "Neck", ["spine"] = "Spine",
                ["lower_jaw"] = "Jaw", ["jaw"] = "Jaw", ["left_hand"] = "LHand", ["right_hand"] = "RHand", ["left_foot"] = "LFoot", ["right_foot"] = "RFoot",
            };
            foreach (var bone in scene.Rig.Bones) aliases.TryAdd(bone.Name, bone.Name.Replace(sourceStem, stem, StringComparison.Ordinal));
            var locators = Locators(scene, aliases);
            var members = new Dictionary<string, byte[]>(StringComparer.Ordinal); var modelFolder = $"bin/pokemon/{stem}/mdl/"; var animationFolder = $"bin/pokemon/{stem}/anm/";
            foreach (var resource in graph.Where(r => r.Id.EndsWith(".bntx", StringComparison.Ordinal))) members[modelFolder + Path.GetFileName(resource.Id)] = resource.Bytes;
            foreach (var material in baked)
            {
                members[modelFolder + material.ColorName + ".bntx"] = material.Color;
                members[modelFolder + material.NormalName + ".bntx"] = material.Normal;
                members[modelFolder + material.AmbientName + ".bntx"] = material.Ambient;
            }
            foreach (var name in new[] { "Default_lta", "dummy_col", "projection_effect_col", "pm0007_00_Eye_amb" })
                members[modelFolder + name + ".bntx"] = Member(nativePack, name + ".bntx");
            var shaderNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in new[] { 3, 5 })
            {
                var (start, count) = nativeMetadata.Vector(nativeMetadata.Root, field, 4, 256);
                for (var i = 0; i < count; i++) shaderNames.Add(nativeMetadata.StringAt(start + i * 4));
            }
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                members[modelFolder + name + extension] = Member(nativePack, name + extension);
            SwShConvertedGeometry? geometry = null;
            foreach (var rare in new[] { false, true })
            {
                var sourceMaterial = new ModelBuffer(Donor(sourceFolder + sourceStem + (rare ? "_rare" : "") + ".trmtr"));
                var bindings = scene.Primitives.Select((primitive, index) =>
                {
                    var original = sourceMaterial.Tables(sourceMaterial.Root, 1, 128).Single(t => sourceMaterial.Text(t, 0) == primitive.Material.Name);
                    var textureNames = sourceMaterial.Tables(original, 2, 64).ToDictionary(t => sourceMaterial.Text(t, 0)!, t => Path.GetFileNameWithoutExtension(sourceMaterial.Text(t, 1)!));
                    var eye = primitive.Material.Name.Contains("eye", StringComparison.Ordinal); var nativeMaterial = eye ? 1 : 0;
                    var textureBindings = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[nativeMaterial], 11, 64)
                        .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                    textureBindings["Col0Tex"] = eye ? baked[index].ColorName : textureNames["BaseColorMap"];
                    textureBindings["NormalMapTex"] = baked[index].NormalName; textureBindings["AmbientTex"] = baked[index].AmbientName;
                    textureBindings["LightTblTex"] = "Default_lta"; textureBindings["EffectTex"] = "projection_effect_col";
                    return new SwShMaterialBinding(primitive.Material.Name, nativeMaterial, textureBindings);
                }).ToArray();
                Report("geometry", rare ? 1 : 0, 2);
                var converted = SwShModelWriter.Convert(scene, nativeModel, bindings, aliases, locators, cancellationToken: token);
                geometry ??= converted; members[modelFolder + stem + (rare ? "_rare" : "") + ".gfbmdl"] = converted.Model;
            }
            var sizeStem = $"pm{species - 386:0000}_00"; var sizePack = SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{sizeStem}.gfpak"));
            var materialNames = scene.Primitives.Select(p => p.Material.Name).ToArray();
            members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"] = SwShModelWriter.Configuration(Member(sizePack, sizeStem + ".gfbpokecfg"), species, geometry!, materialNames,
                new[] { 66f, 100f, 150f }[species - 393], new[] { 40f, 80f, 170f }[species - 393]);
            var clips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)).ToArray();
            Report("animations", 0, motions.Length + NativeClips.Count);
            foreach (var resource in motions)
            {
                token.ThrowIfCancellationRequested();
                var clip = PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones);
                var warnings = new List<string>();
                clip = PreviewMaterialAnimation.Read(new(Donor(Path.ChangeExtension(resource.Id, ".tracm").Replace('\\', '/'))), clip, warnings, true);
                if (warnings.Count > 0 || clip.Visibility.Any(v => v.Keys.Any(k => k.Value.Any(x => x == 0))))
                    throw new InvalidDataException("Source animation needs an unsupported material or visibility adapter.");
                Report("animations", clips.Count, motions.Length + NativeClips.Count);
                var name = Path.GetFileNameWithoutExtension(resource.Id); var code = name[sourceStem.Length..].TrimStart('_').Split('_')[0];
                if (!clips.TryAdd(code, (resource.Bytes, clip))) throw new InvalidDataException("Source animation code is ambiguous.");
                members[animationFolder + name.Replace(sourceStem, stem, StringComparison.Ordinal) + ".gfbanm"] = SwShAnimationWriter.Convert(resource.Bytes, clip.Materials, scene.Rig.Bones, aliases, completePose: false);
            }
            var nativeClipsDone = 0;
            foreach (var (name, code) in NativeClips)
            {
                if (!clips.TryGetValue(code, out var clip)) throw new InvalidDataException($"Required animation is unavailable: {code}");
                Report("animations", motions.Length + nativeClipsDone++, motions.Length + NativeClips.Count);
                var expression = name == "eye01" ? SwShExpressionCurve.Eyes : name == "mouth01" ? SwShExpressionCurve.Mouth : SwShExpressionCurve.None;
                members[animationFolder + stem + "_" + name + ".gfbanm"] = SwShAnimationWriter.Convert(clip.Skeleton, clip.Decoded.Materials,
                    scene.Rig.Bones, aliases, completePose: expression == SwShExpressionCurve.None, expression);
            }
            Report("packing");
            members[animationFolder + stem + "_gloop01.gfbanm"] = SwShAnimationWriter.GlobalMaterialLoop(Member(nativePack, "pm0007_00_gloop01.gfbanm"), materialNames);
            var facialBones = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var name in new[] { "eye01", "mouth01" }) facialBones.Add(name, clips[NativeClips[name]].Decoded.Tracks.Select(t => scene.Rig.Bones[t.Bone].Name)
                .Where(b => b.Contains(name == "eye01" ? "eyelid" : "jaw", StringComparison.OrdinalIgnoreCase)).Select(b => aliases[b]).ToArray());
            foreach (var group in new[] { "battle", "camp", "field" })
                members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = SwShAnimationWriter.Controller(Member(nativePack, "pm0007_00_" + group + ".gfbanmcfg"), "pm0007_00", stem, materialNames, facialBones);
            var result = new List<SwShRestorationContribution> { File($"bin/archive/pokemon/{stem}.gfpak", SwShGfPackFile.CreateWithPaths(members).Write()) };
            var giantDonor = SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0007_00.gfpak"));
            var giantSize = SwShGfPackFile.Parse(Native($"bin/archive/gpokemon/{sizeStem}.gfpak"));
            var giantMembers = new Dictionary<string, byte[]> { [$"bin/pokemon/{stem}/{stem}.gfbgpokecfg"] = Member(giantSize, sizeStem + ".gfbgpokecfg") };
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                giantMembers[$"bin/pokemon/{stem}/g_shader/{name}{extension}"] = Member(giantDonor, name + extension);
            result.Add(File($"bin/archive/gpokemon/{stem}.gfpak", SwShGfPackFile.CreateWithPaths(giantMembers).Write()));
            result.Add(File($"bin/appli/icon_pokemon/poke_icon_{species:0000}_00s_n.bntx", SwShRestorationPartyIcons.Create(species, Native, token)));
            foreach (var shiny in new[] { false, true })
            {
                var identity = new SwShPokemonResourceIdentity(species, 0, 0, shiny);
                var row = new SwShPokemonResourceRow(identity, modelFolder + stem + (shiny ? "_rare" : "") + ".gfbmdl",
                    $"bin/pokemon/{stem}/{stem}.gfbpokecfg", $"bin/archive/pokemon/{stem}.gfpak",
                    new[] { "battle", "camp", "field" }.ToDictionary(g => g, g => animationFolder + stem + "_" + g + ".gfbanmcfg", StringComparer.Ordinal));
                result.Add(new(SwShRestorationComposer.CatalogPath, SwShRestorationWriter.ModelCatalog, SwShRestorationComposer.CatalogKey(identity), null, JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            return result;
        }

        private static SwShEffectLocator[] Locators(PreviewScene scene, IReadOnlyDictionary<string, string> aliases)
        {
            var bounds = scene.Primitives.Select(p => SwShModelWriter.Bounds(p, 100)).ToArray();
            var min = bounds.Select(b => b.Min).Aggregate(Vector3.Min); var max = bounds.Select(b => b.Max).Aggregate(Vector3.Max);
            var names = scene.Rig.Bones.Select(b => aliases[b.Name]).ToHashSet(StringComparer.Ordinal);
            SwShEffectLocator L(string name, string parent, Vector3 position) => new(name, names.Contains(parent) ? parent : "Origin", position);
            return [L("EffMouth01", "Head", new(0, (max.Y - min.Y) * .03f, max.Z * .8f)), L("EffOverHead01", "Origin", new(0, max.Y, 0)),
                L("EffEye01", "Head", new(-(max.X - min.X) * .1f, 0, max.Z * .8f)), L("EffEye02", "Head", new((max.X - min.X) * .1f, 0, max.Z * .8f)),
                L("EffHeadCenter01", "Head", Vector3.Zero), L("EffHand01", "RHand", Vector3.Zero), L("EffHand02", "LHand", Vector3.Zero),
                L("EffFoot01", "RFoot", Vector3.Zero), L("EffFoot02", "LFoot", Vector3.Zero), L("EffFront01", "Origin", new(0, max.Y * .5f, max.Z)),
                L("EffCenter01", "Origin", new(0, max.Y * .5f, 0)), L("EffShoot01_01", "Head", new(0, 0, max.Z)), L("EffAttack01_01", "Head", new(0, 0, max.Z)),
                L("EffGcloud01", "Origin", new(0, max.Y * 1.2f, 0))];
        }
    }
}
