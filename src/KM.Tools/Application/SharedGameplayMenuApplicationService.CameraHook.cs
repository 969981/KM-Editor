// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.RuntimeSettings;
using KM.SwSh.RuntimeSettings;
using static KM.Tools.Application.InGameSettingsPackageApplicationService;

namespace KM.Tools.Application;

internal sealed partial class SharedGameplayMenuApplicationService
{
    private static async Task<GameplaySettingsValues?> ReadInitialCameraHookValues(Loaded loaded, CancellationToken token)
    {
        // Installing a new hook can retain an existing project's verified choices,
        // but preparing game output is never a prerequisite for installing the hook.
        var source = loaded.Context.Source.Paths;
        try
        {
            var journal = await ReviewTargetAsync(source.OutputRootPath!,
                new($"config/km-editor/gameplay-settings/{loaded.TitleId:X16}/settings.bin"), MaximumBytes, token).ConfigureAwait(false);
            if (journal.Exists)
            {
                var inspection = GameplaySettingsJournal.Inspect(journal.Bytes.ToArray(), GameplaySettingsFamily.SwordShield, loaded.TitleId);
                if (inspection.WritesAllowed && inspection.ActiveSnapshot is { } snapshot)
                {
                    Bind(source.OutputRootPath!, journal);
                    return snapshot.Values;
                }
            }
            var current = await ReviewTargetAsync(source.OutputRootPath!, new("exefs/main"), MaximumBytes, token).ConfigureAwait(false);
            if (!current.Exists || string.IsNullOrWhiteSpace(source.BaseExeFsPath)) return null;
            var baseline = await ReviewTargetAsync(source.BaseExeFsPath, new("main"), MaximumBytes, token).ConfigureAwait(false);
            if (!baseline.Exists) return null;
            var analysis = SwShStaticGameplaySettingsMainPatcher.Analyze(baseline.Bytes.ToArray(), current.Bytes.ToArray(), source.SelectedGame!.Value);
            if (analysis.Kind != SwShStaticGameplaySettingsMainKind.Configured
                || analysis.ExperienceShareEnabled is not { } share || analysis.ExperienceRateBasisPoints is not { } rate) return null;
            Bind(source.OutputRootPath!, current);
            Bind(source.BaseExeFsPath, baseline);
            return new(share, rate, analysis.LevelCapEnabled, analysis.LevelCap);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            return null;
        }

        void Bind(string root, ReviewedTarget target) => loaded.ExternalDependencies = loaded.ExternalDependencies.Add(new(
            Path.GetFullPath(Path.Combine(root, target.Path.Value)), target.State.LengthBytes, target.State.Sha256, MaximumBytes));
    }
}
