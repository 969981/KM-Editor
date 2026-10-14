// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed record SpeciesProfile(int NativeTemplate, int HatchedSpecies, int Experience,
        float BattleHeight, float FieldHeight, string TextureSuffix, bool SourceFraming = false, bool MeshVisibility = false);

    private static readonly IReadOnlyDictionary<int, SpeciesProfile> Profiles = new Dictionary<int, SpeciesProfile>
    {
        [13] = new(10, 13, 39, 30, 40, "_body_alb"),
        [14] = new(11, 13, 72, 60, 45, "_body_alb"),
        [15] = new(12, 13, 178, 100, 100, "_body_b_alb", SourceFraming: true),
        [16] = new(821, 16, 50, 30, 30, "_body_a_alb", MeshVisibility: true),
        [17] = new(822, 16, 122, 110, 110, "_body_a_alb", MeshVisibility: true),
        [18] = new(823, 16, 216, 150, 150, "_body_a_alb", MeshVisibility: true),
        [352] = new(560, 352, 154, 100, 100, "_body_a_alb", MeshVisibility: true),
    };
}
