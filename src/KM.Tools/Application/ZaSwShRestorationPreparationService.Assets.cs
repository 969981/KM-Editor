// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.SV;
using KM.Formats.ZA;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static readonly IReadOnlyDictionary<string, string> NativeClips = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ba01_landA01"] = "00152", ["ba01_landB01"] = "00153", ["ba01_landC01"] = "00155",
            ["ba10_waitA01"] = "00001", ["ba10_waitB01"] = "00010", ["ba20_buturi01"] = "00400", ["ba21_tokusyu01"] = "00450",
            ["ba30_damageS01"] = "00500", ["ba41_down01"] = "00520", ["eye01"] = "08000", ["mouth01"] = "08100", ["ba02_roar01"] = "00300",
            ["fi20_walk01"] = "00030", ["fi21_run01"] = "00100", ["kw20_drowseA01"] = "00280", ["kw20_drowseB01"] = "00281", ["kw20_drowseC01"] = "00282",
            ["kw21_sleepA01"] = "00280", ["kw21_sleepB01"] = "00281", ["kw30_hate01"] = "00563", ["kw32_happyA01"] = "00550", ["kw32_happyB01"] = "00550",
            ["kw32_happyC01"] = "00550", ["kw33_moveA01"] = "00150", ["kw33_moveC01"] = "00155", ["kw34_lonely01"] = "00560", ["kw35_playA01"] = "00310",
            ["kw36_mad01"] = "00563", ["kw50_eatA01"] = "00293", ["kw50_eatB01"] = "00294", ["kw50_eatC01"] = "00295", ["kw60_touch01"] = "00320",
        };
        internal IReadOnlyList<SwShRestorationContribution> Assets(int species)
        {
            currentSpecies = species; Report("reading");
            var genders = Profiles[species].SeparateFemale ? new[] { 0, 1 } : new[] { 0 };
            SwShRestorationGenderAudit.RequireModels(Donor("ik_pokemon/catalog/catalog/poke_resource_table.trpmcatalog"),
                species, 0, genders.ToDictionary(g => g, g => $"pm{species:0000}/pm{species:0000}_{g:00}_00/pm{species:0000}_{g:00}_00.trmdl"));
            var result = genders.SelectMany(g => Assets(species, g)).ToList();
            result.Add(File($"bin/appli/icon_pokemon/poke_icon_{species:0000}_00s_n.bntx", SwShRestorationPartyIcons.Create(species, Native, token)));
            return result;
        }

        private IReadOnlyList<SwShRestorationContribution> Assets(int species, int gender)
        {
            var profile = Profiles[species];
            var stem = $"pm{species:0000}_{gender:00}"; var sourceStem = stem + "_00"; var sourceFolder = $"ik_pokemon/data/pm{species:0000}/{sourceStem}/";
            var graph = ModelAssetGraph.Read(new[] { (sourceFolder + sourceStem + ".trmdl", (string?)null),
                (sourceFolder + sourceStem + "_rare.trmtr", null), (sourceFolder + sourceStem + "_base.tracn", null) }, (path, _) => Donor(path));
            var sourceScene = new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(sourceFolder + sourceStem + ".trmdl");
            Report("reading");
            var scene = NormalizeUvs(sourceScene, true);
            if (scene.Rig.Warnings.Length > 0) throw new InvalidDataException("Source model requires an additional conversion adapter.");
            var nativePack = SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0007_00.gfpak"));
            var nativeModel = Member(nativePack, "pm0007_00.gfbmdl"); var nativeMetadata = new ModelBuffer(nativeModel);
            var materialBytes = Donor(sourceFolder + sourceStem + ".trmtr");
            var textureTemplateName = sourceStem + profile.TextureSuffix;
            var textureTemplate = profile.MeshVisibility ? [] : Donor(sourceFolder + textureTemplateName + ".bntx");
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = "Origin", ["waist"] = "Waist", ["head"] = "Head", ["neck"] = "Neck", ["spine"] = "Spine",
                ["lower_jaw"] = "Jaw", ["jaw"] = "Jaw", ["left_hand"] = "LHand", ["right_hand"] = "RHand", ["left_foot"] = "LFoot", ["right_foot"] = "RFoot",
            };
            foreach (var bone in scene.Rig.Bones) aliases.TryAdd(bone.Name, bone.Name.Replace(sourceStem, stem, StringComparison.Ordinal));
            var locators = Locators(scene, aliases, Donor(sourceFolder + $"locators/{sourceStem}_{(species == 15 ? 20000 : 0):00000}_eff.trskl"), profile.MouthPosition);
            var members = new Dictionary<string, byte[]>(StringComparer.Ordinal); var modelFolder = $"bin/pokemon/{stem}/mdl/"; var animationFolder = $"bin/pokemon/{stem}/anm/";
            var textures = new RestorationTexturePool(members, modelFolder, true);
            if (profile.NativeFlames)
                members[modelFolder + sourceStem + "_fire_alb.bntx"] = Donor(sourceFolder + sourceStem + "_fire_alb.bntx");
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
                var variant = rare ? PreviewMaterialVariant.Shiny(new(Donor(sourceFolder + sourceStem + ".trmmt")), sourceFolder + sourceStem + ".trmmt")
                    ?? throw new InvalidDataException("Z-A shiny materials are missing.") : null;
                var variantScene = rare ? NormalizeUvs(new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(sourceFolder + sourceStem + ".trmdl", variant), true) : scene;
                if (variantScene.Primitives.Count != scene.Primitives.Count || variantScene.Primitives.Where((p, i) => p.Material.Name != scene.Primitives[i].Material.Name).Any())
                    throw new InvalidDataException("Z-A shiny material identities do not match the model.");
                var sourceMaterialBytes = rare ? Donor(variant!.Materials.Single()) : materialBytes;
                var baked = variantScene.Primitives.Select((primitive, index) =>
                {
                    var templateName = textureTemplateName; var template = textureTemplate;
                    if (profile.MeshVisibility)
                    {
                        // Use each authored material's image size, including the small eyes.
                        var sourceMaterials = new ModelBuffer(sourceMaterialBytes);
                        var material = sourceMaterials.Tables(sourceMaterials.Root, 1, 128).Single(t => sourceMaterials.Text(t, 0) == primitive.Material.Name);
                        var color = sourceMaterials.Tables(material, 2, 64).Single(t => sourceMaterials.Text(t, 0) == "BaseColorMap");
                        var path = sourceMaterials.Text(color, 1) ?? throw new InvalidDataException("Z-A material texture is unnamed.");
                        templateName = Path.GetFileNameWithoutExtension(path);
                        template = Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", path));
                    }
                    Report("textures", (rare ? scene.Primitives.Count : 0) + index, 2 * scene.Primitives.Count);
                    var materialBake = profile.NativeFlames && primitive.Material.Name == "fire"
                        ? ZaSwShFlameMaterial.Bake(sourceMaterialBytes, rare, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token)
                        : ZaSwShMaterialBaker.Bake(primitive, sourceMaterialBytes, index, rare,
                            template, templateName, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token);
                    Report("textures", (rare ? scene.Primitives.Count : 0) + index + 1, 2 * scene.Primitives.Count);
                    return materialBake with
                    {
                        ColorName = textures.Add(materialBake.ColorName, materialBake.Color),
                        NormalName = textures.Add(materialBake.NormalName, materialBake.Normal),
                        AmbientName = textures.Add(materialBake.AmbientName, materialBake.Ambient),
                    };
                }).ToArray();
                var emissionIndex = profile.EmissionMaterial is null ? -1 : Array.FindIndex(variantScene.Primitives.ToArray(), primitive => primitive.Material.Name == profile.EmissionMaterial);
                if (profile.EmissionMaterial is not null && emissionIndex < 0) throw new InvalidDataException("Z-A emission material is missing.");
                var emission = emissionIndex < 0 ? null : ZaSwShEmissionMaterial.Bake(sourceMaterialBytes, profile.EmissionMaterial!, baked[emissionIndex],
                    name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token);
                var emissionName = emission is null ? null : textures.Add(emission.Name, emission.Bytes);
                var bindings = variantScene.Primitives.Select((primitive, index) =>
                {
                    var nativeMaterial = primitive.Material.Name.Contains("eye", StringComparison.Ordinal) ? 1 : 0;
                    var textureBindings = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[nativeMaterial], 11, 64)
                        .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                    textureBindings["Col0Tex"] = baked[index].ColorName;
                    textureBindings["NormalMapTex"] = baked[index].NormalName; textureBindings["AmbientTex"] = baked[index].AmbientName;
                    textureBindings["LightTblTex"] = "Default_lta"; textureBindings["EffectTex"] = "projection_effect_col";
                    if (profile.NativeFlames && primitive.Material.Name == "fire") textureBindings["EmissionMaskTex"] = sourceStem + "_fire_alb";
                    if (index == emissionIndex) textureBindings["EmissionMaskTex"] = emissionName!;
                    return new SwShMaterialBinding(primitive.Material.Name, nativeMaterial, textureBindings);
                }).ToArray();
                var targetScene = UniqueMaterials(scene);
                bindings = bindings.Select((binding, index) => binding with { Name = targetScene.Primitives[index].Material.Name }).ToArray();
                Report("geometry", rare ? 1 : 0, 2);
                var converted = SwShModelWriter.Convert(targetScene, nativeModel, bindings, aliases, locators, cancellationToken: token, preserveMeshNames: profile.MeshVisibility);
                converted = converted with { Model = SwShRestorationMaterialResponse.Apply(converted.Model) };
                if (profile.NativeFlames) converted = converted with { Model = ZaSwShFlameMaterial.Emission(converted.Model) };
                if (emission is not null) converted = converted with { Model = ZaSwShFlameMaterial.Emission(converted.Model, profile.EmissionMaterial!, 0, emission.Gain) };
                converted = converted with { Model = CompactPrograms(converted.Model) };
                geometry ??= converted; members[modelFolder + stem + (rare ? "_rare" : "") + ".gfbmdl"] = converted.Model;
            }
            var sizeStem = $"pm{profile.NativeTemplate:0000}_00"; var sizePack = SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{sizeStem}.gfpak"));
            var materialNames = UniqueMaterials(scene).Primitives.Select(p => p.Material.Name).ToArray();
            var meshNames = scene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var configuration = Member(sizePack, sizeStem + ".gfbpokecfg");
            if (profile.SourceFraming)
            {
                // Keep authored framing and convert Z-A meters to SwSh centimeters.
                var framing = new ModelBuffer(Donor(sourceFolder + sourceStem + ".trpokecfg"));
                var target = new ModelBuffer(configuration);
                for (var field = 3; field <= 8; field++)
                {
                    var at = framing.Field(framing.Root, field);
                    var destination = target.Field(target.Root, field + 14);
                    if ((at == 0 && field <= 6) || destination == 0) throw new InvalidDataException("Z-A model framing is incomplete.");
                    var value = (at == 0 ? 0 : framing.Float(at)) * (field <= 6 ? 100 : 1);
                    if (!float.IsFinite(value) || (field == 3 && value <= 0)) throw new InvalidDataException("Z-A model framing is invalid.");
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(configuration.AsSpan(destination, 4), value);
                }
            }
            var clips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)).ToArray();
            Report("animations", 0, motions.Length + NativeClips.Count);
            foreach (var resource in motions)
            {
                token.ThrowIfCancellationRequested();
                var clip = PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones);
                clip = ZaSwShAnimationWriter.ReadClip(Donor(Path.ChangeExtension(resource.Id, ".tracm").Replace('\\', '/')), clip, profile.MeshVisibility, profile.EmissionMaterial);
                // A material can be shared by multiple mesh sections. Each converted
                // material needs the authored channel, including hidden mouth sections.
                clip = clip with { Materials = clip.Materials.SelectMany(track => scene.Primitives.Select((p, i) => (p, i))
                    .Where(pair => pair.p.Material.Name == track.Material)
                    .Select(pair => track with { Material = materialNames[pair.i] })).ToArray() };
                if (profile.SeparateFemale)
                {
                    var otherMesh = $"pm{species:0000}_{1 - gender:00}_00_body_c_mesh_shape";
                    if (clip.Visibility.Where(v => !meshNames.Contains(v.Name)).Any(v => v.Name != otherMesh || v.Keys.Any(k => k.Value.Length != 1 || k.Value[0] != 1)))
                        throw new InvalidDataException("Source gender visibility needs another conversion adapter.");
                    clip = clip with { Visibility = clip.Visibility.Where(v => meshNames.Contains(v.Name)).ToArray() };
                }
                Report("animations", clips.Count, motions.Length + NativeClips.Count);
                var name = Path.GetFileNameWithoutExtension(resource.Id); var code = name[sourceStem.Length..].TrimStart('_').Split('_')[0];
                if (!clips.TryAdd(code, (resource.Bytes, clip))) throw new InvalidDataException("Source animation code is ambiguous.");
                var convertedClip = ZaSwShAnimationWriter.Convert(resource.Bytes, clip.Materials, scene.Rig.Bones, aliases, completePose: false);
                members[animationFolder + name.Replace(sourceStem, stem, StringComparison.Ordinal) + ".gfbanm"] = profile.MeshVisibility
                    ? ZaSwShAnimationWriter.Visibility(convertedClip, clip.Visibility, meshNames) : convertedClip;
            }
            string Code(string value) => species == 15 ? (int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) + 20000).ToString("00000", System.Globalization.CultureInfo.InvariantCulture) : value;
            var nativeClipsDone = 0;
            foreach (var (name, code) in NativeClips)
            {
                Report("animations", motions.Length + nativeClipsDone++, motions.Length + NativeClips.Count);
                var expression = name == "eye01" ? SwShExpressionCurve.Eyes : name == "mouth01" ? SwShExpressionCurve.Mouth : SwShExpressionCurve.None;
                var empty = name == "mouth01" || (name == "eye01" && species == 14);
                if (!clips.TryGetValue(Code(empty ? "00000" : code), out var clip)) throw new InvalidDataException($"Required Z-A motion is unavailable: {code}");
                members[animationFolder + stem + "_" + name + ".gfbanm"] = ZaSwShAnimationWriter.Convert(clip.Skeleton, clip.Decoded.Materials,
                    scene.Rig.Bones, aliases, completePose: expression == SwShExpressionCurve.None, expression, empty);
                if (profile.MeshVisibility && expression == SwShExpressionCurve.None)
                    members[animationFolder + stem + "_" + name + ".gfbanm"] = ZaSwShAnimationWriter.Visibility(
                        members[animationFolder + stem + "_" + name + ".gfbanm"], clip.Decoded.Visibility, meshNames);
            }
            var framingGeometry = profile.IdleFraming
                ? SwShRestorationFraming.IdleBounds(members, modelFolder, animationFolder, stem, geometry!) : geometry!;
            members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"] = SwShModelWriter.Configuration(configuration, species, framingGeometry, materialNames,
                profile.BattleHeight, profile.FieldHeight);
            Report("packing");
            members[animationFolder + stem + "_battleidle01.gfbanm"] = SwShAnimationWriter.BattleIdle(members[animationFolder + stem + "_ba10_waitA01.gfbanm"]);
            members[animationFolder + stem + "_gloop01.gfbanm"] = SwShAnimationWriter.GlobalMaterialLoop(Member(nativePack, "pm0007_00_gloop01.gfbanm"), materialNames);
            if (profile.EmissionMaterial is not null)
                members[animationFolder + stem + "_gloop01.gfbanm"] = ZaSwShEmissionMaterial.KeepAnimationLocal(
                    members[animationFolder + stem + "_gloop01.gfbanm"], profile.EmissionMaterial);
            var flameBones = profile.NativeFlames ? SvSwShFlameAnimation.LoopBones(scene, clips["08201"].Decoded) : [];
            if (profile.NativeFlames)
            {
                var loop = clips["08201"];
                var convertedLoop = ZaSwShAnimationWriter.Convert(loop.Skeleton, loop.Decoded.Materials, scene.Rig.Bones, aliases, completePose: false);
                members[animationFolder + stem + "_gloop01.gfbanm"] = flameBones.Length == 0 ? convertedLoop
                    : SvSwShFlameAnimation.SkeletonLoop(convertedLoop, convertedLoop, flameBones.Select(name => aliases[name]).ToArray());
            }
            var facialBones = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var eyelids = scene.Rig.Bones.Where(b => b.Name.Contains("eyelid", StringComparison.OrdinalIgnoreCase)).Select(b => aliases[b.Name]).ToArray();
            if (eyelids.Length > 0) facialBones.Add("eye01", eyelids);
            foreach (var group in new[] { "battle", "camp", "field" })
            {
                var controller = ZaSwShAnimationWriter.Controller(Member(nativePack, "pm0007_00_" + group + ".gfbanmcfg"), "pm0007_00", stem, materialNames, facialBones);
                if (profile.NativeFlames) controller = SvSwShFlameAnimation.Controller(controller, flameBones.Select(name => aliases[name]).ToArray());
                members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = group == "battle" ? SwShAnimationWriter.BattleIdleController(controller) : controller;
            }
            var result = new List<SwShRestorationContribution>();
            foreach (var rare in new[] { false, true })
            {
                var suffix = rare ? "_rare" : "";
                result.Add(File($"bin/archive/pokemon/{stem}{suffix}.gfpak", SwShGfPackFile.CreateWithPaths(PaletteMembers(members, modelFolder + stem + suffix + ".gfbmdl")).Write()));
            }
            shaderNames.IntersectWith(new[] { 3, 5 }.SelectMany(f => ModelNames(new(members[modelFolder + stem + ".gfbmdl"]), f)));
            var giantDonor = SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0007_00.gfpak"));
            var giantSize = SwShGfPackFile.Parse(Native($"bin/archive/gpokemon/{sizeStem}.gfpak"));
            var giantConfiguration = Member(giantSize, sizeStem + ".gfbgpokecfg");
            if (profile.MatchNativeGiantHeight)
                SwShRestorationFraming.MatchGiantHeight(giantConfiguration, configuration, profile.BattleHeight);
            if (species == 15)
            {
                // The source idle already hovers above its origin. Butterfree's
                // negative giant offset would bury Beedrill below the arena.
                var giant = new ModelBuffer(giantConfiguration);
                var offset = giant.Field(giant.Root, 8);
                if (offset == 0) throw new InvalidDataException("Native giant flight configuration is incomplete.");
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(offset, 4), 0);
            }
            var giantMembers = new Dictionary<string, byte[]> { [$"bin/pokemon/{stem}/{stem}.gfbgpokecfg"] = giantConfiguration };
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                giantMembers[$"bin/pokemon/{stem}/g_shader/{name}{extension}"] = Member(giantDonor, name + extension);
            result.Add(File($"bin/archive/gpokemon/{stem}.gfpak", SwShGfPackFile.CreateWithPaths(giantMembers).Write()));
            foreach (var shiny in new[] { false, true })
            {
                var identity = new SwShPokemonResourceIdentity(species, 0, gender, shiny);
                var row = new SwShPokemonResourceRow(identity, modelFolder + stem + (shiny ? "_rare" : "") + ".gfbmdl",
                    $"bin/pokemon/{stem}/{stem}.gfbpokecfg", $"bin/archive/pokemon/{stem}{(shiny ? "_rare" : "")}.gfpak",
                    new[] { "battle", "camp", "field" }.ToDictionary(g => g, g => animationFolder + stem + "_" + g + ".gfbanmcfg", StringComparer.Ordinal));
                result.Add(new(SwShRestorationComposer.CatalogPath, SwShRestorationWriter.ModelCatalog, SwShRestorationComposer.CatalogKey(identity), null, JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            return result;
        }

        private static PreviewScene UniqueMaterials(PreviewScene scene)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return scene with { Primitives = scene.Primitives.Select((primitive, index) =>
            {
                var name = primitive.Material.Name;
                if (!names.Add(name)) { name += "_mesh" + index.ToString(System.Globalization.CultureInfo.InvariantCulture); if (!names.Add(name)) throw new InvalidDataException("Converted material name is ambiguous."); }
                return primitive with { Material = primitive.Material with { Name = name } };
            }).ToArray() };
        }

        private static PreviewScene NormalizeUvs(PreviewScene scene, bool compact = false)
        {
            return scene with { Primitives = scene.Primitives.Select(p =>
            {
                var indices = p.Indices;
                var vertices = p.Vertices.ToArray();
                if (compact)
                {
                    // Source material sections share a vertex buffer. Keep every referenced
                    // vertex and triangle without duplicating other sections in the target.
                    var used = indices.Distinct().Order().ToArray();
                    var remap = used.Select((source, index) => (source, index)).ToDictionary(pair => pair.source, pair => (uint)pair.index);
                    vertices = used.SelectMany(index => p.Vertices.Skip(checked((int)index * 16)).Take(16)).ToArray();
                    indices = indices.Select(index => remap[index]).ToArray();
                }
                for (var i = 0; i < vertices.Length; i += 16)
                    for (var c = 6; c <= 7; c++) vertices[i + c] -= MathF.Floor(vertices[i + c]);
                return p with { Vertices = vertices, Indices = indices };
            }).ToArray() };
        }

        private static SwShEffectLocator[] Locators(PreviewScene scene, IReadOnlyDictionary<string, string> aliases, byte[] bytes, Vector3? mouthPosition = null)
        {
            var source = new ModelBuffer(bytes); var nodes = source.Tables(source.Root, 1, 512);
            var transforms = PreviewRigReader.Skeleton(source);
            var entries = transforms.Select((bone, index) => (Bone: bone, Parent: source.Text(nodes[index], 6)))
                .ToDictionary(e => e.Bone.Name, StringComparer.Ordinal);
            SwShEffectLocator Convert(string name, string original)
            {
                if (!entries.TryGetValue(original, out var entry) || entry.Bone.Parent != -1 || entry.Bone.Scale.Any(v => !float.IsFinite(v) || MathF.Abs(v - 1) > .00001f)
                    || entry.Parent is null || !scene.Rig.Bones.Any(b => b.Name == entry.Parent))
                    throw new InvalidDataException("Z-A effect attachment requires another target adapter.");
                var p = entry.Bone.Translation; var q = entry.Bone.Rotation;
                return new(name, aliases[entry.Parent], new(p[0] * 100, p[1] * 100, p[2] * 100)) { Rotation = new(q[0], q[1], q[2], q[3]) };
            }
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EffMouth01"] = "eff_mouth02", ["EffOverHead01"] = "eff_overhead01", ["EffEye01"] = "eff_eye01", ["EffEye02"] = "eff_eye02",
                ["EffHeadCenter01"] = "eff_headcenter01", ["EffFront01"] = "eff_front01", ["EffCenter01"] = "eff_center01",
                ["EffShoot01_01"] = "eff_rangeattack01", ["EffShoot01_02"] = "eff_rangeattack02",
                ["EffAttack01_01"] = "eff_attack01", ["EffAttack01_02"] = "eff_attack02", ["EffAttack01_03"] = "eff_attack03",
                ["EffGcloud01"] = "eff_overhead01",
            };
            var result = mapping.Select(pair => Convert(pair.Key, pair.Value)).ToList();
            if (mouthPosition is { } mouth)
            {
                // A feeding target fixed to Origin cannot follow the face. The meal
                // camera needs a mouth attachment in the animated head's local space.
                if (!float.IsFinite(mouth.X) || !float.IsFinite(mouth.Y) || !float.IsFinite(mouth.Z)
                    || !aliases.TryGetValue("head", out var head) || !scene.Rig.Bones.Any(b => b.Name == "head"))
                    throw new InvalidDataException("Z-A mouth attachment requires another target adapter.");
                var reference = Convert("EffMouth01", "eff_headcenter01");
                if (reference.Parent != head) throw new InvalidDataException("Z-A mouth attachment requires another target adapter.");
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
