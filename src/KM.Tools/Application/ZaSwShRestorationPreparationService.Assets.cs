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
        internal IReadOnlyList<SwShRestorationContribution> Assets(KM.SwSh.Pokemon.SwShPokemonIdentity identity)
        {
            var species = identity.Species; var profile = Profile(identity);
            currentSpecies = species; Report("reading");
            var genders = profile.SeparateFemale ? new[] { 0, 1 } : new[] { 0 };
            string ModelPath(int gender)
            {
                var resource = profile.ResourceStem ?? $"pm{species:0000}_{gender:00}_00";
                return $"{resource[..6]}/{resource}/{resource}.trmdl";
            }
            SwShRestorationGenderAudit.RequireModels(Donor("ik_pokemon/catalog/catalog/poke_resource_table.trpmcatalog"),
                species, identity.Form, genders.ToDictionary(g => g, ModelPath));
            var result = genders.SelectMany(g => Assets(species, g, profile, identity.Form)).ToList();
            result.Add(File($"bin/appli/icon_pokemon/poke_icon_{species:0000}_{identity.Form:00}s_n.bntx", SwShRestorationPartyIcons.Create(species, Native, token, identity.Form, textureEncoding)));
            return result;
        }

        private IReadOnlyList<SwShRestorationContribution> Assets(int species, int gender, SpeciesProfile? formProfile = null, int form = 0)
        {
            var profile = formProfile ?? Profiles[species];
            var groups = new[] { "battle", "camp", "field" };
            var nativeClips = NativeClips.ToArray();
            var flames = profile.PairedFlames ? profile.FlameMaterials ?? ["fire"] : Array.Empty<string>();
            var stem = profile.OutputStem ?? $"pm{species:0000}_{gender:00}"; var sourceStem = profile.ResourceStem ?? stem + "_00"; var sourceFolder = $"ik_pokemon/data/{sourceStem[..6]}/{sourceStem}/";
            var graph = ModelAssetGraph.Read(new[] { (sourceFolder + sourceStem + ".trmdl", (string?)null),
                (sourceFolder + sourceStem + "_rare.trmtr", null), (sourceFolder + sourceStem + "_base.tracn", null) }, (path, _) => Donor(path));
            var sourceScene = new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(sourceFolder + sourceStem + ".trmdl");
            Report("reading");
            var scene = NormalizeUvs(sourceScene, true);
            if (scene.Rig.Warnings.Length > 0) throw new InvalidDataException("Source model requires an additional conversion adapter.");
            string Code(string name, string value) => species == 15 || profile.AirborneBattle && name.StartsWith("ba", StringComparison.Ordinal)
                ? (int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) + 20000).ToString("00000", System.Globalization.CultureInfo.InvariantCulture) : value;
            var campClips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            if (species == 6 && form is 1 or 2)
            {
                foreach (var resource in graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)))
                {
                    var code = Path.GetFileNameWithoutExtension(resource.Id)[(sourceStem.Length + 1)..].Split('_')[0];
                    campClips.Add(code, (resource.Bytes, PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones)));
                }
                AddCharizardCampMotions(scene, campClips, nativeClips.Where(p => p.Key != "mouth01")
                    .Select(p => Code(p.Key, p.Value)).Append("00000"), form);
            }
            var nativePack = SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0007_00.gfpak"));
            var bodyModel = Member(nativePack, "pm0007_00.gfbmdl");
            var materialBytes = Donor(sourceFolder + sourceStem + ".trmtr");
            var firePack = profile.PairedFlames ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0004_00.gfpak")) : null;
            var fireModel = firePack is null ? null : Member(firePack, "pm0004_00.gfbmdl");
            (byte[] Model, Dictionary<string, SvSwShFlameTemplate> Flames) FlameTemplate(byte[] materials)
            {
                var model = bodyModel;
                var templates = new Dictionary<string, SvSwShFlameTemplate>(StringComparer.Ordinal);
                foreach (var name in flames)
                {
                    var template = SvSwShFlameMaterial.Template(model, fireModel!, SvSwShFlameMaterial.NormalizeName(materials, name),
                        SvSwShFlameMaterial.NormalizeName(materialBytes, name), templates.Count);
                    model = template.Model;
                    templates.Add(name, template);
                }
                return (model, templates);
            }
            var nativeModel = FlameTemplate(materialBytes).Model;
            var eyePack = profile.LayeredEyes ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0001_00.gfpak")) : null;
            var eyeContracts = profile.LayeredEyes ? ZaSwShEyelidMaterial.Materials(materialBytes, sourceScene, profile.EyeAtlasOffsets) : null;
            if (eyePack is not null) nativeModel = ZaSwShEyelidMaterial.Template(nativeModel, Member(eyePack, "pm0001_00.gfbmdl"));
            var nativeMetadata = new ModelBuffer(nativeModel);
            var targetScene = UniqueMaterials(scene);
            if (profile.PairedFlames)
            {
                var masks = flames.Select(name => targetScene.Primitives.Single(primitive => primitive.Material.Name == name))
                    .Select(flame => flame with { Name = flame.Name + "_mask", Material = flame.Material with { Name = flame.Material.Name + "_mask" } });
                targetScene = targetScene with { Primitives = targetScene.Primitives.Concat(masks).ToArray() };
            }
            var textureTemplateName = sourceStem + profile.TextureSuffix;
            var textureTemplate = profile.MeshVisibility ? [] : Donor(sourceFolder + textureTemplateName + ".bntx");
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = "Origin", ["waist"] = "Waist", ["head"] = "Head", ["neck"] = "Neck", ["spine"] = "Spine",
                ["lower_jaw"] = "Jaw", ["jaw"] = "Jaw", ["left_hand"] = "LHand", ["right_hand"] = "RHand", ["left_foot"] = "LFoot", ["right_foot"] = "RFoot",
            };
            foreach (var bone in scene.Rig.Bones) aliases.TryAdd(bone.Name, bone.Name.Replace(sourceStem, stem, StringComparison.Ordinal));
            var airborneOffset = profile.AirborneBattle ? AirborneOffset(species, form, Donor(sourceFolder + sourceStem + ".trpokecfg")) : 0;
            var locators = Locators(scene, aliases, Donor(sourceFolder + $"locators/{sourceStem}_{(species == 15 || profile.AirborneBattle ? 20000 : 0):00000}_eff.trskl"), profile.MouthPosition, airborneOffset);
            var members = new Dictionary<string, byte[]>(StringComparer.Ordinal); var modelFolder = $"bin/pokemon/{stem}/mdl/"; var animationFolder = $"bin/pokemon/{stem}/anm/";
            var textures = new RestorationTexturePool(members, modelFolder, true);
            var fireTextures = new Dictionary<string, SvSwShFlameTextures>(StringComparer.Ordinal);
            foreach (var flame in flames)
            {
                var converted = SvSwShFlameMaterial.ZaTextures(SvSwShFlameMaterial.NormalizeName(materialBytes, flame),
                    name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                var allocated = converted.Files.ToDictionary(pair => pair.Key, pair => textures.Add(pair.Key, pair.Value), StringComparer.Ordinal);
                fireTextures.Add(flame, converted with { Layer = allocated[converted.Layer], Noise = allocated[converted.Noise],
                    Control = allocated[converted.Control], White = allocated[converted.White] });
            }
            if (flames.Length > 0) members[modelFolder + "viewport_mask_col.bntx"] = Member(firePack!, "viewport_mask_col.bntx");
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
                members[modelFolder + name + extension] = profile.LayeredEyes && name == ZaSwShEyelidMaterial.ProgramName
                    ? extension == ".bnsh_fsh" ? ZaSwShEyeShader.Convert(Member(eyePack!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                        : ZaSwShEyeShader.Vertex(Member(eyePack!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                    : nativePack.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(firePack ?? nativePack, name + extension);
            SwShConvertedGeometry? geometry = null;
            foreach (var rare in new[] { false, true })
            {
                var variant = rare ? PreviewMaterialVariant.Shiny(new(Donor(sourceFolder + sourceStem + ".trmmt")), sourceFolder + sourceStem + ".trmmt")
                    ?? throw new InvalidDataException("Z-A shiny materials are missing.") : null;
                var variantScene = rare ? NormalizeUvs(new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(sourceFolder + sourceStem + ".trmdl", variant), true) : scene;
                if (variantScene.Primitives.Count != scene.Primitives.Count || variantScene.Primitives.Where((p, i) => p.Material.Name != scene.Primitives[i].Material.Name).Any())
                    throw new InvalidDataException("Z-A shiny material identities do not match the model.");
                var sourceMaterialBytes = rare ? Donor(variant!.Materials.Single()) : materialBytes;
                var paletteEyes = profile.LayeredEyes ? ZaSwShEyelidMaterial.Materials(sourceMaterialBytes, sourceScene, profile.EyeAtlasOffsets) : null;
                var eyeTextures = new Dictionary<int, ZaSwShEyelidMaterial.EyeTextures>();
                var flameTemplate = FlameTemplate(sourceMaterialBytes);
                SwShMaterialBinding FireBinding(string name, bool mask) => new(mask ? name + "_mask" : name,
                    mask ? flameTemplate.Flames[name].Mask : flameTemplate.Flames[name].Core,
                    mask ? new Dictionary<string, string> { ["Mask0Texture"] = fireTextures[name].White, ["Mask1Texture"] = fireTextures[name].White }
                    : new Dictionary<string, string> { ["LerpTex"] = fireTextures[name].Control, ["Blend0Tex"] = fireTextures[name].Layer,
                        ["Blend1Tex"] = fireTextures[name].Noise, ["MaskTex"] = "viewport_mask_col" });
                var baked = variantScene.Primitives.Select((primitive, index) =>
                {
                    if (flames.Contains(primitive.Material.Name, StringComparer.Ordinal)) return null;
                    if (paletteEyes?.TryGetValue(primitive.Material.Name, out var eyeContract) == true)
                    {
                        Report("textures", (rare ? scene.Primitives.Count : 0) + index, 2 * scene.Primitives.Count);
                        var images = ZaSwShEyelidMaterial.Textures(eyeContract, index, rare,
                            name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                        var allocated = images.Files.ToDictionary(pair => pair.Key, pair => textures.Add(pair.Key, pair.Value), StringComparer.Ordinal);
                        eyeTextures.Add(index, images with { Bindings = images.Bindings.ToDictionary(pair => pair.Key, pair => allocated[pair.Value], StringComparer.Ordinal) });
                        return null;
                    }
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
                        ? ZaSwShFlameMaterial.Bake(sourceMaterialBytes, rare, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding)
                        : ZaSwShMaterialBaker.Bake(primitive, sourceMaterialBytes, index, rare,
                            template, templateName, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, profile.LayeredEyes, textureEncoding);
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
                var emission = emissionIndex < 0 ? null : ZaSwShEmissionMaterial.Bake(sourceMaterialBytes, profile.EmissionMaterial!, baked[emissionIndex]!,
                    name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                var emissionName = emission is null ? null : textures.Add(emission.Name, emission.Bytes);
                var bindings = variantScene.Primitives.Select((primitive, index) =>
                {
                    if (flames.Contains(primitive.Material.Name, StringComparer.Ordinal)) return FireBinding(primitive.Material.Name, false);
                    var nativeMaterial = primitive.Material.Name.Contains("eye", StringComparison.Ordinal) ? 1 : 0;
                    var textureBindings = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[nativeMaterial], 11, 64)
                        .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                    if (eyeTextures.TryGetValue(index, out var eyeImages))
                    {
                        foreach (var pair in eyeImages.Bindings) textureBindings[pair.Key] = pair.Value;
                    }
                    else
                    {
                        var materialBake = baked[index] ?? throw new InvalidDataException("Converted material is missing.");
                        textureBindings["Col0Tex"] = materialBake.ColorName;
                        textureBindings["NormalMapTex"] = materialBake.NormalName; textureBindings["AmbientTex"] = materialBake.AmbientName;
                    }
                    textureBindings["LightTblTex"] = "Default_lta"; textureBindings["EffectTex"] = "projection_effect_col";
                    if (profile.NativeFlames && primitive.Material.Name == "fire") textureBindings["EmissionMaskTex"] = sourceStem + "_fire_alb";
                    if (index == emissionIndex) textureBindings["EmissionMaskTex"] = emissionName!;
                    return new SwShMaterialBinding(primitive.Material.Name, nativeMaterial, textureBindings);
                }).ToArray();
                if (profile.PairedFlames) bindings = bindings.Concat(flames.Select(name => FireBinding(name, true))).ToArray();
                bindings = bindings.Select((binding, index) => binding with { Name = targetScene.Primitives[index].Material.Name }).ToArray();
                Report("geometry", rare ? 1 : 0, 2);
                var converted = SwShModelWriter.Convert(targetScene, profile.PairedFlames ? flameTemplate.Model : nativeModel, bindings, aliases, locators, cancellationToken: token, preserveMeshNames: profile.MeshVisibility);
                if (paletteEyes is not null)
                    converted = converted with { Model = ZaSwShEyelidMaterial.Bind(converted.Model, paletteEyes,
                        targetScene.Primitives.Select((primitive, index) => (primitive.Material.Name, Original: scene.Primitives[index].Material.Name))
                            .ToDictionary(pair => pair.Name, pair => pair.Original)) };
                converted = converted with { Model = SwShRestorationMaterialResponse.Apply(converted.Model) };
                if (profile.NativeFlames) converted = converted with { Model = ZaSwShFlameMaterial.Emission(converted.Model) };
                if (emission is not null) converted = converted with { Model = ZaSwShFlameMaterial.Emission(converted.Model, profile.EmissionMaterial!, 0, emission.Gain) };
                converted = converted with { Model = CompactPrograms(converted.Model) };
                geometry ??= converted; members[modelFolder + stem + (rare ? "_rare" : "") + ".gfbmdl"] = converted.Model;
            }
            var sizeStem = $"pm{profile.NativeTemplate:0000}_00"; var sizePack = SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{sizeStem}.gfpak"));
            var materialNames = targetScene.Primitives.Select(p => p.Material.Name).ToArray();
            var meshNames = targetScene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
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
            if (species == 6 && form is 1 or 2)
            {
                // The Mega wing spans need a preview fit between the source and native
                // Charizard scales. Retain the authored camera position and angles.
                var framing = new ModelBuffer(configuration);
                var at = framing.Field(framing.Root, 17);
                if (at == 0) throw new InvalidDataException("Native Charizard preview framing is missing.");
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(configuration.AsSpan(at, 4), 300);
            }
            var clips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            byte[] ConvertMotion(byte[] skeleton, PreviewClip decoded, bool complete, SwShExpressionCurve expression = SwShExpressionCurve.None, bool empty = false)
            {
                var materialTracks = decoded.Materials.Where(track => !flames.Contains(track.Material, StringComparer.Ordinal)).ToArray();
                var converted = ZaSwShAnimationWriter.Convert(skeleton, materialTracks, scene.Rig.Bones, aliases, complete, expression, empty);
                if (expression != SwShExpressionCurve.None) return converted;
                if (!profile.PairedFlames) return profile.MeshVisibility ? ZaSwShAnimationWriter.Visibility(converted, decoded.Visibility, meshNames) : converted;
                foreach (var flame in flames) converted = SvSwShFlameAnimation.Apply(converted, decoded.Materials, materialName: flame);
                var fireMeshes = targetScene.Primitives.Where(primitive => flames.Contains(primitive.Material.Name, StringComparer.Ordinal))
                    .Select(primitive => primitive.Name).ToHashSet(StringComparer.Ordinal);
                var visibility = decoded.Visibility.SelectMany(track => fireMeshes.Contains(track.Name) ? new[] { track, track with { Name = track.Name + "_mask" } } : [track]).ToArray();
                return ZaSwShAnimationWriter.Visibility(converted, visibility, meshNames);
            }
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)).ToArray();
            Report("animations", 0, motions.Length + nativeClips.Length);
            foreach (var resource in motions)
            {
                token.ThrowIfCancellationRequested();
                var clip = PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones);
                var materialAnimation = Donor(Path.ChangeExtension(resource.Id, ".tracm").Replace('\\', '/'));
                clip = profile.PairedFlames ? ZaSwShAnimationWriter.ReadFlameClip(materialAnimation, clip, flames)
                    : ZaSwShAnimationWriter.ReadClip(materialAnimation, clip, profile.MeshVisibility, profile.EmissionMaterial, eyeContracts);
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
                clip = clip with { Visibility = VisibilitySections(scene, targetScene, clip.Visibility) };
                Report("animations", clips.Count, motions.Length + nativeClips.Length);
                var name = Path.GetFileNameWithoutExtension(resource.Id); var code = name[sourceStem.Length..].TrimStart('_').Split('_')[0];
                if (!clips.TryAdd(code, (resource.Bytes, clip))) throw new InvalidDataException("Source animation code is ambiguous.");
                members[animationFolder + name.Replace(sourceStem, stem, StringComparison.Ordinal) + ".gfbanm"] = ConvertMotion(resource.Bytes, clip, false);
            }
            foreach (var pair in campClips) clips.TryAdd(pair.Key, pair.Value);
            var nativeClipsDone = 0;
            foreach (var (name, code) in nativeClips)
            {
                Report("animations", motions.Length + nativeClipsDone++, motions.Length + nativeClips.Length);
                var expression = name == "eye01" ? SwShExpressionCurve.Eyes : name == "mouth01" ? SwShExpressionCurve.Mouth : SwShExpressionCurve.None;
                var empty = name == "mouth01" || (name == "eye01" && species == 14);
                // An empty facial layer only needs timing. It writes no bones
                // or material tracks and leaves the authored body motion intact.
                var sourceCode = Code(name, empty ? "00000" : code);
                if (!clips.TryGetValue(sourceCode, out var clip)) throw new InvalidDataException($"Required Z-A motion is unavailable: {name}/{sourceCode}");
                var convertedMotion = ConvertMotion(clip.Skeleton, clip.Decoded, expression == SwShExpressionCurve.None, expression, empty);
                if (profile.AirborneBattle && name.StartsWith("ba", StringComparison.Ordinal))
                    convertedMotion = SwShAnimationWriter.VerticalOffset(convertedMotion, "Waist", airborneOffset);
                members[animationFolder + stem + "_" + name + ".gfbanm"] = convertedMotion;
            }
            var framingGeometry = profile.IdleFraming
                ? SwShRestorationFraming.IdleBounds(members, modelFolder, animationFolder, stem, geometry!) : geometry!;
            members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"] = SwShModelWriter.Configuration(configuration, species, framingGeometry, materialNames,
                profile.BattleHeight, profile.FieldHeight);
            Report("packing");
            members[animationFolder + stem + "_battleidle01.gfbanm"] = SwShAnimationWriter.BattleIdle(members[animationFolder + stem + "_ba10_waitA01.gfbanm"]);
            members[animationFolder + stem + "_gloop01.gfbanm"] = SwShAnimationWriter.GlobalMaterialLoop(Member(nativePack, "pm0007_00_gloop01.gfbanm"),
                materialNames.Where(name => !flames.Any(flame => name == flame || name == flame + "_mask")).ToArray());
            if (eyeContracts is not null)
                members[animationFolder + stem + "_gloop01.gfbanm"] = ZaSwShEyelidMaterial.KeepAnimationLocal(
                    members[animationFolder + stem + "_gloop01.gfbanm"], materialNames.Where(name => name.Contains("eye", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal));
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
            if (profile.PairedFlames)
            {
                var loop = clips.GetValueOrDefault("08201");
                if (loop.Decoded is not null)
                {
                    flameBones = SvSwShFlameAnimation.LoopBones(scene, loop.Decoded, flames);
                    var convertedLoop = ZaSwShAnimationWriter.Convert(loop.Skeleton, [], scene.Rig.Bones, aliases, completePose: false);
                    var global = members[animationFolder + stem + "_gloop01.gfbanm"];
                    foreach (var flame in flames) global = SvSwShFlameAnimation.Apply(global, loop.Decoded.Materials, loop.Decoded, flame);
                    members[animationFolder + stem + "_gloop01.gfbanm"] = flameBones.Length == 0 ? global
                        : SvSwShFlameAnimation.SkeletonLoop(global, convertedLoop, flameBones.Select(name => aliases[name]).ToArray());
                }
            }
            var facialBones = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var eyelids = scene.Rig.Bones.Where(b => b.Name.Contains("eyelid", StringComparison.OrdinalIgnoreCase)).Select(b => aliases[b.Name]).ToArray();
            if (eyelids.Length > 0) facialBones.Add("eye01", eyelids);
            foreach (var group in groups)
            {
                var controller = ZaSwShAnimationWriter.Controller(Member(nativePack, "pm0007_00_" + group + ".gfbanmcfg"), "pm0007_00", stem, materialNames, facialBones, profile.LayeredEyes);
                if (profile.NativeFlames || profile.PairedFlames)
                    controller = SvSwShFlameAnimation.Controller(controller, flameBones.Select(name => aliases[name]).ToArray(), profile.PairedFlames ? flames : null);
                members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = group == "battle" ? SwShAnimationWriter.BattleIdleController(controller) : controller;
            }
            if (species == 6 && form is 1 or 2)
                foreach (var group in new[] { "camp", "field" })
                    members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = SwShAnimationWriter.AddGlobalLoop(
                        members[animationFolder + stem + "_" + group + ".gfbanmcfg"], members[animationFolder + stem + "_battle.gfbanmcfg"]);
            var result = new List<SwShRestorationContribution>();
            foreach (var rare in new[] { false, true })
            {
                var suffix = rare ? "_rare" : "";
                result.Add(File($"bin/archive/pokemon/{stem}{suffix}.gfpak", SwShGfPackFile.CreateWithPaths(PaletteMembers(members, modelFolder + stem + suffix + ".gfbmdl")).Write()));
            }
            shaderNames.IntersectWith(new[] { 3, 5 }.SelectMany(f => ModelNames(new(members[modelFolder + stem + ".gfbmdl"]), f)));
            var giantDonor = SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0007_00.gfpak"));
            var giantFire = profile.PairedFlames ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0004_00.gfpak")) : null;
            var giantEye = profile.LayeredEyes ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0001_00.gfpak")) : null;
            var giantSize = SwShGfPackFile.Parse(Native($"bin/archive/gpokemon/{sizeStem}.gfpak"));
            var giantConfiguration = Member(giantSize, sizeStem + ".gfbgpokecfg");
            if (profile.MatchNativeGiantHeight)
                SwShRestorationFraming.MatchGiantHeight(giantConfiguration, configuration, profile.BattleHeight);
            if (profile.AirborneBattle)
                SwShRestorationFraming.MatchGiantGround(giantConfiguration, configuration,
                    members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"]);
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
                giantMembers[$"bin/pokemon/{stem}/g_shader/{name}{extension}"] = profile.LayeredEyes && name == ZaSwShEyelidMaterial.ProgramName
                    ? extension == ".bnsh_fsh" ? ZaSwShEyeShader.Convert(Member(giantEye!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                        : ZaSwShEyeShader.Vertex(Member(giantEye!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                    : giantDonor.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(giantFire ?? giantDonor, name + extension);
            result.Add(File($"bin/archive/gpokemon/{stem}.gfpak", SwShGfPackFile.CreateWithPaths(giantMembers).Write()));
            foreach (var shiny in new[] { false, true })
            {
                var identity = new SwShPokemonResourceIdentity(species, form, gender, shiny);
                var row = new SwShPokemonResourceRow(identity, modelFolder + stem + (shiny ? "_rare" : "") + ".gfbmdl",
                    $"bin/pokemon/{stem}/{stem}.gfbpokecfg", $"bin/archive/pokemon/{stem}{(shiny ? "_rare" : "")}.gfpak",
                    groups.ToDictionary(g => g, g => animationFolder + stem + "_" + g + ".gfbanmcfg", StringComparer.Ordinal));
                result.Add(new(SwShRestorationComposer.CatalogPath, SwShRestorationWriter.ModelCatalog, SwShRestorationComposer.CatalogKey(identity), null, JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            return result;
        }

        private void AddCharizardCampMotions(PreviewScene scene,
            Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)> clips, IEnumerable<string> required, int form)
        {
            const string stem = "pm0006_00_00";
            const string folder = "ik_pokemon/data/pm0006/pm0006_00_00/";
            var source = new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(folder + stem + ".trmdl");
            var graph = ModelAssetGraph.Read(new[] { (folder + stem + "_base.tracn", (string?)null) }, (path, _) => Donor(path));
            var sourceBones = source.Rig.Bones.ToDictionary(b => b.Name, StringComparer.Ordinal);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var bone in scene.Rig.Bones)
            {
                var name = bone.Name == Profile(new(6, form)).ResourceStem ? stem : bone.Name;
                if (form == 2 && (name.StartsWith("left_wing_", StringComparison.Ordinal) || name.StartsWith("right_wing_", StringComparison.Ordinal)))
                    name = name.Replace("_wing_", "_wing_a_", StringComparison.Ordinal);
                if (!sourceBones.TryGetValue(name, out var original)) continue;
                var parent = bone.Parent < 0 ? null : scene.Rig.Bones[bone.Parent].Name;
                if (parent is not null)
                {
                    if (!map.TryGetValue(parent, out var mappedParent)) continue;
                    var at = original.Parent;
                    while (at >= 0 && source.Rig.Bones[at].Name != mappedParent) at = source.Rig.Bones[at].Parent;
                    if (at < 0) continue;
                }
                map.Add(bone.Name, name);
            }
            foreach (var name in new[] { "origin", "waist", "spine_01", "spine_02", "neck_01", "head", "jaw", "hips", "left_leg_01", "right_leg_01", "tail_01" })
                if (!map.ContainsKey(name)) throw new InvalidDataException("Charizard camp motion has an incomplete anatomical mapping.");
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)).ToDictionary(
                r => Path.GetFileNameWithoutExtension(r.Id)[(stem.Length + 1)..].Split('_')[0], StringComparer.Ordinal);
            foreach (var code in required.Distinct(StringComparer.Ordinal).Where(code => !clips.ContainsKey(code)))
            {
                if (!motions.TryGetValue(code, out var motion)) throw new InvalidDataException("Charizard camp source motion is missing: " + code);
                var original = PreviewRigReader.Animation(new(motion.Bytes), motion.Id, source.Rig.Bones);
                var skeleton = SwShAnimationWriter.Retarget(original, source.Rig.Bones, scene.Rig.Bones, map);
                clips.Add(code, (skeleton, PreviewRigReader.Animation(new(skeleton), motion.Id, scene.Rig.Bones)));
            }
        }

        private static PreviewVisibility[] VisibilitySections(PreviewScene source, PreviewScene target, IReadOnlyList<PreviewVisibility> tracks)
        {
            if (target.Primitives.Count < source.Primitives.Count || tracks.Any(track => !source.Primitives.Any(primitive => primitive.Name == track.Name)))
                throw new InvalidDataException("Source visibility targets an unavailable mesh.");
            return tracks.SelectMany(track => source.Primitives.Select((primitive, index) => (primitive, index))
                .Where(pair => pair.primitive.Name == track.Name)
                .Select(pair => track with { Name = target.Primitives[pair.index].Name })).ToArray();
        }

        private static PreviewScene UniqueMaterials(PreviewScene scene)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var meshes = new HashSet<string>(StringComparer.Ordinal);
            return scene with { Primitives = scene.Primitives.Select((primitive, index) =>
            {
                var name = primitive.Material.Name;
                if (!names.Add(name)) { name += "_mesh" + index.ToString(System.Globalization.CultureInfo.InvariantCulture); if (!names.Add(name)) throw new InvalidDataException("Converted material name is ambiguous."); }
                var mesh = primitive.Name;
                if (!meshes.Add(mesh)) { mesh += "_section" + index.ToString(System.Globalization.CultureInfo.InvariantCulture); if (!meshes.Add(mesh)) throw new InvalidDataException("Converted mesh name is ambiguous."); }
                return primitive with { Name = mesh, Material = primitive.Material with { Name = name } };
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

        private static SwShEffectLocator[] Locators(PreviewScene scene, IReadOnlyDictionary<string, string> aliases, byte[] bytes, Vector3? mouthPosition = null, float airborneOffset = 0)
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
            if (airborneOffset != 0)
            {
                var face = Convert("EffMouth01", "eff_face01");
                if (face.Parent != aliases["head"]) throw new InvalidDataException("Airborne mouth attachment must follow the head.");
                result[result.FindIndex(locator => locator.Name == "EffMouth01")] = face with { Rotation = Quaternion.Identity };
                for (var i = 0; i < result.Count; i++)
                    if (result[i].Parent == aliases["origin"])
                        result[i] = result[i] with { Translation = result[i].Translation + new Vector3(0, airborneOffset, 0) };
            }
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
