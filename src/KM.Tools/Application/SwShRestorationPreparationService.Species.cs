// SPDX-License-Identifier: GPL-3.0-only
using KM.SwSh.Pokemon;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed record SpeciesProfile(int NativeTemplate, int HatchedSpecies, int Experience,
        int FemalePercent, byte GenderThreshold, float BattleHeight, float FieldHeight, bool AuthoredMaterials = false, int FramingTemplate = 0,
        string BattleIdle = "00001", bool NativeFlames = false, string? ResourceStem = null, bool CompactResources = true, string FlameMaterial = "fire",
        System.Numerics.Vector3? MouthPosition = null, bool MeshVisibility = false, bool SharedMaterialTextures = false, bool OpaqueEyeSurface = false,
        bool CornealEyes = false, float? GiantScale = null, int AnimationOffset = 0,
        IReadOnlyDictionary<string, float>? CornealNormalHeights = null,
        IReadOnlyDictionary<string, System.Numerics.Vector2>? NormalUvScales = null, float? GiantVerticalOffset = null, string? SourceStem = null, int HatchedForm = 0,
        IReadOnlyDictionary<string, int>? NormalUvVerticalTiles = null, int? SourceVoiceForm = null, bool LayerEmission = false,
        bool ScalarEyes = false, IReadOnlyDictionary<string, string>? MotionOverrides = null, bool AuthoredAlpha = false,
        int? SourceSpecies = null, bool EmptyMouth = false, bool TransparentEyes = false,
        IReadOnlyDictionary<string, System.Numerics.Vector2>? StaticUvScales = null);

    private static readonly IReadOnlyDictionary<SwShPokemonIdentity, SpeciesProfile> FormProfiles = new Dictionary<SwShPokemonIdentity, SpeciesProfile>
    {
        [new(74, 1)] = new(109, 74, 60, 50, 127, 50, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            AnimationOffset: 20000, CornealEyes: true, GiantVerticalOffset: 0, SourceStem: "pm0074_00_11", HatchedForm: 1, SourceVoiceForm: 0),
        [new(75, 1)] = new(525, 74, 137, 50, 127, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            CornealEyes: true, SourceStem: "pm0075_00_11", HatchedForm: 1, SourceVoiceForm: 0, LayerEmission: true),
        [new(76, 1)] = new(112, 74, 223, 50, 127, 170, 170, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            CornealEyes: true, SourceStem: "pm0076_00_11", HatchedForm: 1,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .01f, ["r_eye"] = .01f },
            NormalUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm0076_00_11_bodyinc_mesh_shape"] = new(1, 2),
                ["pm0076_00_11_bodybinc_mesh_shape"] = new(1, 2),
            }, NormalUvVerticalTiles: new Dictionary<string, int> { ["pm0076_00_11_bodybinc_mesh_shape"] = 1 }, SourceVoiceForm: 0, LayerEmission: true),
    };
    private static bool TryProfile(SwShPokemonIdentity identity, out SpeciesProfile profile) => identity.Form == 0
        ? Profiles.TryGetValue(identity.Species, out profile!) : FormProfiles.TryGetValue(identity, out profile!);
    private static SpeciesProfile Profile(SwShPokemonIdentity identity) => TryProfile(identity, out var profile) ? profile
        : throw new InvalidDataException("Restoration form profile is missing.");

    private static readonly IReadOnlyDictionary<int, SpeciesProfile> Profiles = new Dictionary<int, SpeciesProfile>
    {
        [48] = new(595, 48, 61, 50, 127, 100, 100, AuthoredMaterials: true, FramingTemplate: 596, MeshVisibility: true,
            CornealEyes: true, ScalarEyes: true, BattleIdle: "00000", MotionOverrides: new Dictionary<string, string>
            { ["ba10_waitB01"] = "00000", ["kw32_happyB01"] = "00550", ["kw35_playA01"] = "00550", ["mouth01"] = "00000" }, EmptyMouth: true),
        [49] = new(12, 48, 158, 50, 127, 100, 150, AuthoredMaterials: true, MeshVisibility: true,
            AnimationOffset: 20000, BattleIdle: "00000", GiantVerticalOffset: 0, AuthoredAlpha: true, TransparentEyes: true,
            StaticUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm0049_00_00_l_eye_a_mesh_shape"] = new(1, 2),
                ["pm0049_00_00_r_eye_a_mesh_shape"] = new(1, 2),
                ["pm0049_00_00_l_eye_b_mesh_shape"] = new(4, 2),
                ["pm0049_00_00_r_eye_b_mesh_shape"] = new(4, 2),
                ["pm0049_00_00_l_hghtlight_mesh_shape"] = new(4, 8),
                ["pm0049_00_00_r_hghtlight_mesh_shape"] = new(4, 8),
            }),
        [979] = new(766, 56, 268, 50, 127, 150, 120, AuthoredMaterials: true, MeshVisibility: true,
            SourceStem: "pm1114_00_00", SourceSpecies: 1010, CornealEyes: true, EmptyMouth: true,
            MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" },
            NormalUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm1114_00_00_ring_mesh_shape"] = new(1, 2),
                ["pm1114_00_00_foot_mesh_shape"] = new(1, 2),
            },
            NormalUvVerticalTiles: new Dictionary<string, int>
            {
                ["pm1114_00_00_ring_mesh_shape"] = 1,
                ["pm1114_00_00_foot_mesh_shape"] = 1,
            }),
        [74] = new(109, 74, 60, 50, 127, 50, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, AnimationOffset: 20000, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .05f }, GiantVerticalOffset: 0),
        [75] = new(525, 74, 137, 50, 127, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [76] = new(112, 74, 223, 50, 127, 140, 140, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .01f, ["r_eye"] = .01f },
            NormalUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm0076_00_00_face_mesh_shape"] = new(1, 2),
                ["pm0076_00_00_leftfoot_mesh_shape"] = new(2, 1),
                ["pm0076_00_00_rightfoot_mesh_shape"] = new(2, 1),
                ["pm0076_00_00_lefthand_mesh_shape"] = new(2, 1),
                ["pm0076_00_00_righthand_mesh_shape"] = new(2, 1),
            }),
        [495] = new(252, 495, 62, 12, 31, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true),
        [496] = new(253, 495, 145, 12, 31, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, OpaqueEyeSurface: true),
        [497] = new(350, 495, 238, 12, 31, 160, 330, AuthoredMaterials: true, BattleIdle: "00000", MeshVisibility: true, SharedMaterialTextures: true, GiantScale: 12),
        [501] = new(7, 501, 62, 12, 31, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [502] = new(8, 501, 145, 12, 31, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [503] = new(365, 501, 238, 12, 31, 150, 150, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true),
        [155] = new(4, 155, 62, 12, 31, 50, 50, AuthoredMaterials: true, NativeFlames: true, FlameMaterial: "body_b",
            MouthPosition: new(.115f, 0, .162f)),
        [156] = new(5, 155, 142, 12, 31, 90, 90, AuthoredMaterials: true, NativeFlames: true, FlameMaterial: "body_b",
            MouthPosition: new(.187f, 0, .095f)),
        [157] = new(467, 155, 240, 12, 31, 170, 170, AuthoredMaterials: true, NativeFlames: true, FlameMaterial: "body_b",
            MouthPosition: new(.33f, 0, .135f)),
        [23] = new(147, 23, 58, 50, 127, 50, 50, AuthoredMaterials: true, FramingTemplate: 7),
        [24] = new(350, 23, 157, 50, 127, 160, 200, AuthoredMaterials: true),
        [387] = new(1, 387, 64, 12, 31, 60, 40, AuthoredMaterials: true, BattleIdle: "00000", CompactResources: true),
        [388] = new(2, 387, 142, 12, 31, 100, 110, AuthoredMaterials: true, CompactResources: true),
        [389] = new(3, 387, 263, 12, 31, 220, 220, AuthoredMaterials: true, CompactResources: true),
        [390] = new(810, 390, 62, 12, 31, 65, 50, AuthoredMaterials: true, NativeFlames: true, ResourceStem: "pm0948_00_00", CompactResources: true),
        [391] = new(811, 390, 142, 12, 31, 100, 90, AuthoredMaterials: true, NativeFlames: true, ResourceStem: "pm0949_00_00", CompactResources: true),
        [392] = new(766, 390, 267, 12, 31, 150, 120, AuthoredMaterials: true, NativeFlames: true, ResourceStem: "pm0815_00", CompactResources: true),
        [393] = new(7, 393, 63, 12, 31, 66, 40),
        [394] = new(8, 393, 142, 12, 31, 100, 80),
        [395] = new(9, 393, 265, 12, 31, 150, 170),
    };
}
