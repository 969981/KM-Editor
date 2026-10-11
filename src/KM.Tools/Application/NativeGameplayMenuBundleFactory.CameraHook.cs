// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.RuntimeSettings;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;

namespace KM.Tools.Application;

public static partial class NativeGameplayMenuBundleFactory
{
    internal static (InGameSettingsPackageDescriptorDto Package, byte[] Journal) CreateCameraHookSettings(
        ProjectGame game, GameplaySettingsValues? initialValues = null)
    {
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)) throw new ArgumentOutOfRangeException(nameof(game));
        var title = ProjectGameMetadata.Get(game).TitleId;
        var journal = GameplaySettingsJournal.CreateBootstrap(GameplaySettingsFamily.SwordShield, title,
            new(checked((ushort)PackageVersion.Major), checked((ushort)PackageVersion.Minor), checked((ushort)PackageVersion.Patch)),
            SettingsPresence | GameplaySettingPresence.UnlockedCamera, initialValues ?? GameplaySettingsValues.Vanilla);
        var identity = Hash(journal);
        return (new(title.ToString("X16"), "1.3.2", GetExpectedBuild(game),
            new(PackageVersion.Major, PackageVersion.Minor, PackageVersion.Patch), identity[..32], identity.ToLowerInvariant(), 2), journal);
    }
}
