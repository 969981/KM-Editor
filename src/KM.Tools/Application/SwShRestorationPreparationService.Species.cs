// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed record SpeciesProfile(int NativeTemplate, int HatchedSpecies, int Experience,
        int FemalePercent, byte GenderThreshold, float BattleHeight, float FieldHeight, bool AuthoredMaterials = false, int FramingTemplate = 0);

    private static readonly IReadOnlyDictionary<int, SpeciesProfile> Profiles = new Dictionary<int, SpeciesProfile>
    {
        [23] = new(147, 23, 58, 50, 127, 50, 50, AuthoredMaterials: true, FramingTemplate: 7),
        [24] = new(350, 23, 157, 50, 127, 160, 200, AuthoredMaterials: true),
        [393] = new(7, 393, 63, 12, 31, 66, 40),
        [394] = new(8, 393, 142, 12, 31, 100, 80),
        [395] = new(9, 393, 265, 12, 31, 150, 170),
    };
}
