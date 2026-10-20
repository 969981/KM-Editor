// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed record SpeciesProfile(int NativeTemplate, int HatchedSpecies, int Experience,
        int FemalePercent, byte GenderThreshold, float BattleHeight, float FieldHeight, bool AuthoredMaterials = false, int FramingTemplate = 0,
        string BattleIdle = "00001", bool NativeFlames = false, string? ResourceStem = null, bool CompactResources = true, string FlameMaterial = "fire",
        System.Numerics.Vector3? MouthPosition = null, bool MeshVisibility = false, bool SharedMaterialTextures = false, bool OpaqueEyeSurface = false,
        bool CornealEyes = false, float? GiantScale = null);

    private static readonly IReadOnlyDictionary<int, SpeciesProfile> Profiles = new Dictionary<int, SpeciesProfile>
    {
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
