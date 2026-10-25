// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.SV;
using KM.SwSh.Restoration;
using KM.SwSh.Pokemon;

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
        internal IReadOnlyList<SwShRestorationContribution> Assets(SwShPokemonIdentity pokemon)
        {
            var species = pokemon.Species; currentIdentity = pokemon; currentSpecies = species; Report("reading");
            var profile = Profile(pokemon);
            var stem = $"pm{species:0000}_{pokemon.Form:00}"; var sourceStem = profile.SourceStem ?? stem + "_00"; var sourceFolder = $"pokemon/data/pm{species:0000}/{sourceStem}/";
            SwShRestorationGenderAudit.RequireSharedModel(Donor("pokemon/catalog/catalog/poke_resource_table.trpmcatalog"),
                species, pokemon.Form, $"pm{species:0000}/{sourceStem}/{sourceStem}.trmdl");
            var graph = ModelAssetGraph.Read(new[] { (sourceFolder + sourceStem + ".trmdl", (string?)null),
                (sourceFolder + sourceStem + "_rare.trmtr", null), (sourceFolder + sourceStem + "_base.tracn", null) }, (path, _) => Donor(path));
            Report("reading");
            var scene = new TrinityPreviewReader(Donor).Load(sourceFolder + sourceStem + ".trmdl");
            if (scene.Rig.Warnings.Length > 0) throw new InvalidDataException("Source model requires an additional conversion adapter.");
            if (profile.NativeFlames && profile.FlameMaterial != "fire")
                scene = scene with { Primitives = scene.Primitives.Select(p => p.Material.Name == profile.FlameMaterial
                    ? p with { Material = p.Material with { Name = "fire" } } : p).ToArray() };
            byte[] Materials(bool rare) => profile.NativeFlames
                ? SvSwShFlameMaterial.NormalizeName(Donor(sourceFolder + sourceStem + (rare ? "_rare" : "") + ".trmtr"), profile.FlameMaterial)
                : Donor(sourceFolder + sourceStem + (rare ? "_rare" : "") + ".trmtr");
            var flameAtlas = profile.NativeFlames && SvSwShFlameMaterial.IsUndisplaced(Materials(false));
            var targetScene = UniqueMaterials(profile.CompactResources ? CompactGeometry(scene) : scene);
            if (flameAtlas)
                targetScene = targetScene with { Primitives = targetScene.Primitives.Select(p => p.Material.Name == "fire"
                    ? SvSwShFlameMaterial.AtlasGeometry(p) : p).ToArray() };
            if (profile.NativeFlames)
            {
                var flame = targetScene.Primitives.Single(p => p.Material.Name == "fire");
                targetScene = targetScene with { Primitives = targetScene.Primitives.Append(flame with
                    { Name = flame.Name + "_mask", Material = flame.Material with { Name = "fire_mask" } }).ToArray() };
            }
            var nativePack = SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0007_00.gfpak"));
            var bodyModel = Member(nativePack, "pm0007_00.gfbmdl");
            var firePack = profile.NativeFlames ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0004_00.gfpak")) : null;
            var materialBytes = Materials(false);
            var fireModel = firePack is null ? null : Member(firePack, "pm0004_00.gfbmdl");
            var nativeModel = fireModel is null ? bodyModel : SvSwShFlameMaterial.Template(bodyModel, fireModel, materialBytes).Model;
            var cornealPack = profile.CornealEyes ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0060_00.gfpak")) : null;
            if (cornealPack is not null) nativeModel = SvSwShCornealMaterial.Template(nativeModel, Member(cornealPack, "pm0060_00.gfbmdl"));
            if (profile.LayerEmission) nativeModel = SvSwShLayerEmission.Template(nativeModel);
            var emissionContracts = profile.LayerEmission ? SvSwShLayerEmission.Contracts(materialBytes, scene) : null;
            var emissionNames = targetScene.Primitives.Select((p, i) => (p, i)).Where(p => emissionContracts?.ContainsKey(scene.Primitives[p.i].Material.Name) == true)
                .Select(p => p.p.Material.Name).ToHashSet(StringComparer.Ordinal);
            var originalNames = targetScene.Primitives.Select((p, i) => (p.Material.Name, Original: scene.Primitives[i].Material.Name))
                .ToDictionary(p => p.Name, p => p.Original, StringComparer.Ordinal);
            byte[] EmissionShader(SwShGfPackFile pack, string extension) => extension == ".bnsh_fsh"
                ? SvSwShLayerEmissionShader.Convert(Member(pack, "Body00" + extension)) : Member(pack, "Body00" + extension);
            var nativeMetadata = new ModelBuffer(nativeModel);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = "Origin", ["waist"] = "Waist", ["head"] = "Head", ["neck"] = "Neck", ["spine"] = "Spine",
                ["lower_jaw"] = "Jaw", ["jaw"] = "Jaw", ["left_hand"] = "LHand", ["right_hand"] = "RHand", ["left_foot"] = "LFoot", ["right_foot"] = "RFoot",
            };
            foreach (var bone in scene.Rig.Bones) aliases.TryAdd(bone.Name, bone.Name.Replace(sourceStem, stem, StringComparison.Ordinal));
            var locators = profile.AuthoredMaterials
                ? SourceLocators(scene, aliases, Donor(sourceFolder + $"locators/{sourceStem}_{profile.AnimationOffset:00000}_eff.trskl"), profile.MouthPosition)
                : Locators(scene, aliases);
            var members = new Dictionary<string, byte[]>(StringComparer.Ordinal); var modelFolder = $"bin/pokemon/{stem}/mdl/"; var animationFolder = $"bin/pokemon/{stem}/anm/";
            var textures = new RestorationTexturePool(members, modelFolder, profile.CompactResources);
            var fireTextures = profile.NativeFlames ? SvSwShFlameMaterial.Textures(materialBytes,
                name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding) : null;
            if (fireTextures is not null)
            {
                foreach (var pair in fireTextures.Files) members[modelFolder + pair.Key + ".bntx"] = pair.Value;
                members[modelFolder + "viewport_mask_col.bntx"] = Member(firePack!, "viewport_mask_col.bntx");
            }
            foreach (var name in new[] { "Default_lta", "dummy_col", "projection_effect_col", "pm0007_00_Eye_amb" })
                members[modelFolder + name + ".bntx"] = Member(nativePack, name + ".bntx");
            if (profile.CornealEyes)
                members[modelFolder + SvSwShCornealMaterial.EnvironmentName + ".bntx"] = Member(
                    SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0330_00.gfpak")), SvSwShCornealMaterial.EnvironmentName + ".bntx");
            var shaderNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in new[] { 3, 5 })
            {
                var (start, count) = nativeMetadata.Vector(nativeMetadata.Root, field, 4, 256);
                for (var i = 0; i < count; i++) shaderNames.Add(nativeMetadata.StringAt(start + i * 4));
            }
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                members[modelFolder + name + extension] = profile.LayerEmission && name == SvSwShLayerEmission.ProgramName
                    ? EmissionShader(nativePack, extension) : profile.CornealEyes && name == SvSwShCornealMaterial.ProgramName
                    ? Member(cornealPack!, SvSwShCornealMaterial.NativeProgramName + extension)
                    : nativePack.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(firePack ?? nativePack, name + extension);
            SwShConvertedGeometry? geometry = null;
            foreach (var rare in new[] { false, true })
            {
                var paletteBytes = Materials(rare);
                var sourceMaterial = new ModelBuffer(paletteBytes);
                var paletteEmission = profile.LayerEmission ? SvSwShLayerEmission.Contracts(paletteBytes, scene) : null;
                var palette = BakePalette(scene, paletteBytes, rare, sourceFolder + sourceStem + ".trmtr", textures, profile.NativeFlames);
                var flameTemplate = fireModel is null ? null : SvSwShFlameMaterial.Template(bodyModel, fireModel, paletteBytes, materialBytes);
                SwShMaterialBinding FireBinding(bool mask) => new(mask ? "fire_mask" : "fire", mask ? flameTemplate!.Mask : flameTemplate!.Core,
                    mask ? new Dictionary<string, string> { ["Mask0Texture"] = fireTextures!.White, ["Mask1Texture"] = fireTextures.White }
                    : new Dictionary<string, string> { ["LerpTex"] = fireTextures!.Control, ["Blend0Tex"] = fireTextures.Layer,
                        ["Blend1Tex"] = fireTextures.Noise, ["MaskTex"] = "viewport_mask_col" });
                var bindings = scene.Primitives.Select((primitive, index) =>
                {
                    if (profile.NativeFlames && primitive.Material.Name == "fire") return FireBinding(false);
                    var material = palette[index] ?? throw new InvalidDataException("Converted material is missing.");
                    var original = sourceMaterial.Tables(sourceMaterial.Root, 1, 128).Single(t => sourceMaterial.Text(t, 0) == primitive.Material.Name);
                    var textureNames = sourceMaterial.Tables(original, 2, 64).ToDictionary(t => sourceMaterial.Text(t, 0)!, t => Path.GetFileNameWithoutExtension(sourceMaterial.Text(t, 1)!));
                    var eye = primitive.Material.Name.Contains("eye", StringComparison.Ordinal); var nativeMaterial = eye ? 1 : 0;
                    if (paletteEmission?.ContainsKey(primitive.Material.Name) == true) nativeMaterial = nativeMetadata.Tables(nativeMetadata.Root, 6, 128).Length - 1;
                    var textureBindings = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[nativeMaterial], 11, 64)
                        .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                    textureBindings["Col0Tex"] = material.ColorName;
                    textureBindings["NormalMapTex"] = material.NormalName; textureBindings["AmbientTex"] = material.AmbientName;
                    textureBindings["LightTblTex"] = "Default_lta"; textureBindings["EffectTex"] = "projection_effect_col";
                    if (paletteEmission?.TryGetValue(primitive.Material.Name, out var emission) == true)
                    {
                        var path = TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", emission.Mask);
                        textureBindings["EmissionMaskTex"] = textures.Add(Path.GetFileNameWithoutExtension(emission.Mask), Donor(path));
                    }
                    if (profile.CornealEyes && eye) textureBindings["SphereMapTex"] = SvSwShCornealMaterial.EnvironmentName;
                    return new SwShMaterialBinding(targetScene.Primitives[index].Material.Name, nativeMaterial, textureBindings)
                        { NormalUvScale = material.NormalUvScale };
                }).ToArray();
                if (profile.NativeFlames) bindings = bindings.Append(FireBinding(true)).ToArray();
                Report("geometry", rare ? 1 : 0, 2);
                var paletteScene = profile.CornealEyes ? targetScene with { Primitives = targetScene.Primitives.Select((primitive, index) =>
                    primitive.Material.Name.Contains("eye", StringComparison.Ordinal)
                        ? SvSwShCornealMaterial.Normals(primitive, palette[index]!.Normal) : primitive).ToArray() } : targetScene;
                var converted = SwShModelWriter.Convert(paletteScene, flameTemplate?.Model ?? nativeModel, bindings, aliases, locators,
                    cancellationToken: token, preserveMeshNames: profile.NativeFlames || profile.MeshVisibility);
                converted = converted with { Model = SwShRestorationMaterialResponse.Apply(converted.Model) };
                if (paletteEmission is not null) converted = converted with { Model = SvSwShLayerEmission.Bind(converted.Model, paletteEmission, originalNames) };
                if (profile.CompactResources) converted = converted with { Model = CompactPrograms(converted.Model) };
                geometry ??= converted; members[modelFolder + stem + (rare ? "_rare" : "") + ".gfbmdl"] = converted.Model;
            }
            var sizeStem = profile.ResourceStem ?? $"pm{profile.NativeTemplate:0000}_00";
            var sizePack = SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{sizeStem}.gfpak"));
            var materialNames = targetScene.Primitives.Select(p => p.Material.Name).ToArray();
            var frameStem = profile.FramingTemplate == 0 ? sizeStem : $"pm{profile.FramingTemplate:0000}_00";
            var framePack = profile.FramingTemplate == 0 ? sizePack : SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{frameStem}.gfpak"));
            var clips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)).ToArray();
            var meshNames = targetScene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            byte[] ConvertMotion(byte[] skeleton, PreviewClip decoded, bool complete, SwShExpressionCurve expression = SwShExpressionCurve.None)
            {
                var converted = SwShAnimationWriter.Convert(skeleton, decoded.Materials.Where(t => (!profile.NativeFlames || t.Material != "fire") && (!profile.LayerEmission || !t.Parameter.StartsWith("EmissionIntensityLayer", StringComparison.Ordinal))).ToArray(),
                    scene.Rig.Bones, aliases, complete, expression);
                if (expression != SwShExpressionCurve.None) return converted;
                if (profile.LayerEmission) converted = SvSwShLayerEmission.Animation(converted, decoded.Materials);
                if (!profile.NativeFlames) return profile.MeshVisibility ? SwShAnimationWriter.Visibility(converted, decoded.Visibility, meshNames) : converted;
                converted = SvSwShFlameAnimation.Apply(converted, decoded.Materials);
                var fireMesh = scene.Primitives.Single(p => p.Material.Name == "fire").Name;
                var visibility = decoded.Visibility.SelectMany(v => v.Name == fireMesh ? new[] { v, v with { Name = fireMesh + "_mask" } } : [v]).ToArray();
                return SwShAnimationWriter.Visibility(converted, visibility, meshNames);
            }
            Report("animations", 0, motions.Length + NativeClips.Count);
            foreach (var resource in motions)
            {
                token.ThrowIfCancellationRequested();
                var clip = PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones);
                var warnings = new List<string>();
                var materialAnimation = Donor(Path.ChangeExtension(resource.Id, ".tracm").Replace('\\', '/'));
                clip = profile.NativeFlames ? SvSwShFlameAnimation.Read(materialAnimation, clip, profile.FlameMaterial) : profile.LayerEmission ? SvSwShLayerEmission.Read(materialAnimation, clip, scene, emissionContracts!)
                    : PreviewMaterialAnimation.Read(new(materialAnimation), clip, warnings, true);
                if (warnings.Count > 0 || (!profile.NativeFlames && !profile.MeshVisibility && clip.Visibility.Any(v => v.Keys.Any(k => k.Value.Any(x => x == 0)))))
                    throw new InvalidDataException("Source animation needs an unsupported material or visibility adapter.");
                if (clip.Materials.Any(t => !scene.Primitives.Any(p => p.Material.Name == t.Material)))
                    throw new InvalidDataException("Source animation has an undeclared material target.");
                if (flameAtlas && clip.Materials.Any(t => t.Material == "fire"))
                    throw new InvalidDataException("An animated flame atlas requires another UV adapter.");
                clip = clip with { Materials = clip.Materials.SelectMany(track => scene.Primitives.Select((p, i) => (p, i))
                    .Where(pair => pair.p.Material.Name == track.Material)
                    .Select(pair => track with { Material = targetScene.Primitives[pair.i].Material.Name })).ToArray() };
                Report("animations", clips.Count, motions.Length + NativeClips.Count);
                var name = Path.GetFileNameWithoutExtension(resource.Id); var sourceCode = name[sourceStem.Length..].TrimStart('_').Split('_')[0];
                var number = int.Parse(sourceCode, System.Globalization.CultureInfo.InvariantCulture) - profile.AnimationOffset;
                if (number is < 0 or > 99999) throw new InvalidDataException("Source animation code is outside the declared profile.");
                var code = number.ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
                if (!clips.TryAdd(code, (resource.Bytes, clip))) throw new InvalidDataException("Source animation code is ambiguous.");
                members[animationFolder + name.Replace(sourceStem, stem, StringComparison.Ordinal) + ".gfbanm"] = ConvertMotion(resource.Bytes, clip, false);
            }
            var nativeClipsDone = 0;
            foreach (var (name, code) in NativeClips)
            {
                var sourceCode = name == "ba10_waitA01" ? profile.BattleIdle : code;
                if (!clips.TryGetValue(sourceCode, out var clip)) throw new InvalidDataException($"Required animation is unavailable: {sourceCode}");
                Report("animations", motions.Length + nativeClipsDone++, motions.Length + NativeClips.Count);
                var expression = name == "eye01" ? SwShExpressionCurve.Eyes : name == "mouth01" ? SwShExpressionCurve.Mouth : SwShExpressionCurve.None;
                members[animationFolder + stem + "_" + name + ".gfbanm"] = ConvertMotion(clip.Skeleton, clip.Decoded, expression == SwShExpressionCurve.None, expression);
            }
            Report("packing");
            members[animationFolder + stem + "_battleidle01.gfbanm"] = SwShAnimationWriter.BattleIdle(members[animationFolder + stem + "_ba10_waitA01.gfbanm"]);
            var globalLoop = SwShAnimationWriter.GlobalMaterialLoop(Member(nativePack, "pm0007_00_gloop01.gfbanm"),
                materialNames.Where(n => n is not ("fire" or "fire_mask")).ToArray(),
                scene.Primitives.Select((primitive, index) => (Name: targetScene.Primitives[index].Material.Name,
                    Scale: profile.NormalUvScales?.GetValueOrDefault(primitive.Name, Vector2.One) ?? Vector2.One))
                    .Where(entry => entry.Scale != Vector2.One).ToDictionary(entry => entry.Name, entry => entry.Scale));
            if (profile.LayerEmission) globalLoop = SvSwShLayerEmission.GlobalLoop(globalLoop, emissionNames);
            if (profile.CornealEyes) globalLoop = SvSwShCornealMaterial.GlobalLoop(globalLoop);
            globalLoop = profile.NativeFlames
                ? SvSwShFlameAnimation.Apply(globalLoop, clips["08201"].Decoded.Materials, clips["08201"].Decoded) : globalLoop;
            var flameBones = profile.NativeFlames ? SvSwShFlameAnimation.LoopBones(scene, clips["08201"].Decoded) : [];
            if (flameBones.Length > 0)
                globalLoop = SvSwShFlameAnimation.SkeletonLoop(globalLoop,
                    SwShAnimationWriter.Convert(clips["08201"].Skeleton, [], scene.Rig.Bones, aliases, false),
                    flameBones.Select(b => aliases[b]).ToArray());
            members[animationFolder + stem + "_gloop01.gfbanm"] = globalLoop;
            var facialBones = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var name in new[] { "eye01", "mouth01" })
            {
                var bones = clips[NativeClips[name]].Decoded.Tracks.Select(t => scene.Rig.Bones[t.Bone].Name)
                    .Where(b => b.Contains(name == "eye01" ? "eyelid" : "jaw", StringComparison.OrdinalIgnoreCase)).Select(b => aliases[b]).ToArray();
                if (bones.Length > 0) facialBones.Add(name, bones);
            }
            foreach (var group in new[] { "battle", "camp", "field" })
            {
                var controller = SwShAnimationWriter.Controller(Member(nativePack, "pm0007_00_" + group + ".gfbanmcfg"), "pm0007_00", stem, materialNames, facialBones);
                if (profile.LayerEmission) controller = SvSwShLayerEmission.Controller(controller, emissionNames);
                if (group == "battle") controller = SwShAnimationWriter.BattleIdleController(controller);
                members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = profile.NativeFlames
                    ? SvSwShFlameAnimation.Controller(controller, flameBones.Select(b => aliases[b]).ToArray()) : controller;
            }
            var framingGeometry = profile.AuthoredMaterials ? SwShRestorationFraming.IdleBounds(members, modelFolder, animationFolder, stem, geometry!) : geometry!;
            members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"] = SwShModelWriter.Configuration(Member(framePack, frameStem + ".gfbpokecfg"), species, framingGeometry, materialNames,
                profile.BattleHeight, profile.FieldHeight);
            var result = new List<SwShRestorationContribution>();
            foreach (var rare in profile.CompactResources ? new[] { false, true } : new[] { false })
            {
                var suffix = rare ? "_rare" : "";
                var selected = profile.CompactResources ? PaletteMembers(members, modelFolder + stem + suffix + ".gfbmdl") : members;
                result.Add(File($"bin/archive/pokemon/{stem}{suffix}.gfpak", SwShGfPackFile.CreateWithPaths(selected).Write()));
            }
            if (profile.CompactResources)
                shaderNames.IntersectWith(new[] { 3, 5 }.SelectMany(f => ModelNames(new(members[modelFolder + stem + ".gfbmdl"]), f)));
            var giantDonor = SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0007_00.gfpak"));
            var giantFire = profile.NativeFlames ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0004_00.gfpak")) : null;
            var giantCorneal = profile.CornealEyes ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0060_00.gfpak")) : null;
            var giantSize = SwShGfPackFile.Parse(Native($"bin/archive/gpokemon/{sizeStem}.gfpak"));
            var giantConfiguration = Member(giantSize, sizeStem + ".gfbgpokecfg");
            if (profile.GiantScale is { } giantScale) SwShRestorationFraming.SetGiantScale(giantConfiguration, giantScale);
            if (profile.GiantVerticalOffset is { } giantVerticalOffset) SwShRestorationFraming.SetGiantVerticalOffset(giantConfiguration, giantVerticalOffset);
            var giantMembers = new Dictionary<string, byte[]> { [$"bin/pokemon/{stem}/{stem}.gfbgpokecfg"] = giantConfiguration };
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                giantMembers[$"bin/pokemon/{stem}/g_shader/{name}{extension}"] = profile.LayerEmission && name == SvSwShLayerEmission.ProgramName
                    ? EmissionShader(giantDonor, extension) : profile.CornealEyes && name == SvSwShCornealMaterial.ProgramName
                    ? Member(giantCorneal!, SvSwShCornealMaterial.NativeProgramName + extension)
                    : giantDonor.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(giantFire ?? giantDonor, name + extension);
            result.Add(File($"bin/archive/gpokemon/{stem}.gfpak", SwShGfPackFile.CreateWithPaths(giantMembers).Write()));
            result.Add(File($"bin/appli/icon_pokemon/poke_icon_{species:0000}_{pokemon.Form:00}s_n.bntx", SwShRestorationPartyIcons.Create(species, Native, token, pokemon.Form, textureEncoding)));
            foreach (var shiny in new[] { false, true })
            {
                var identity = new SwShPokemonResourceIdentity(species, pokemon.Form, 0, shiny);
                var row = new SwShPokemonResourceRow(identity, modelFolder + stem + (shiny ? "_rare" : "") + ".gfbmdl",
                    $"bin/pokemon/{stem}/{stem}.gfbpokecfg", $"bin/archive/pokemon/{stem}{(shiny && profile.CompactResources ? "_rare" : "")}.gfpak",
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
