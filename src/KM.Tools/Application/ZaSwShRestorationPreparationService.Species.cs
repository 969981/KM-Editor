// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed record SpeciesProfile(int NativeTemplate, int HatchedSpecies, int Experience,
        float BattleHeight, float FieldHeight, string TextureSuffix, bool SourceFraming = false, bool MeshVisibility = false, int FemalePercent = 50, byte GenderThreshold = 127, bool SeparateFemale = false, bool IdleFraming = false, bool MatchNativeGiantHeight = false,
        System.Numerics.Vector3? MouthPosition = null, float? MealVerticalOffset = null);

    private static readonly IReadOnlyDictionary<int, SpeciesProfile> Profiles = new Dictionary<int, SpeciesProfile>
    {
        [152] = new(1, 152, 64, 90, 90, "_body_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.023f, 0, .11f), MealVerticalOffset: 0),
        [153] = new(2, 152, 142, 120, 120, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.08f, 0, .202f)),
        [154] = new(3, 152, 236, 180, 180, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, SeparateFemale: true, IdleFraming: true,
            MouthPosition: new(.12f, 0, .303f)),
        [158] = new(7, 158, 63, 60, 60, "_body_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.044f, 0, .253f)),
        [159] = new(8, 158, 142, 110, 110, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.12f, 0, .366f)),
        // Native SwSh compresses large actors in battle. Tyranitar's 155 fits the
        // comparable upright silhouette; retain 230 for field and camp scale.
        [160] = new(9, 158, 239, 155, 230, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, IdleFraming: true, MatchNativeGiantHeight: true,
            MouthPosition: new(.70f, 0, .21f)),
        [13] = new(10, 13, 39, 30, 40, "_body_alb"),
        [14] = new(11, 13, 72, 60, 45, "_body_alb"),
        [15] = new(12, 13, 178, 100, 100, "_body_b_alb", SourceFraming: true),
        [16] = new(821, 16, 50, 30, 30, "_body_a_alb", MeshVisibility: true),
        [17] = new(822, 16, 122, 110, 110, "_body_a_alb", MeshVisibility: true),
        [18] = new(823, 16, 216, 150, 150, "_body_a_alb", MeshVisibility: true),
        [352] = new(560, 352, 154, 100, 100, "_body_a_alb", MeshVisibility: true),
    };
}
