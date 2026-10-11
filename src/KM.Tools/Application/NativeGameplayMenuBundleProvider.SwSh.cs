// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.RuntimeSettings;

namespace KM.Tools.Application;

public sealed partial class NativeGameplayMenuBundleProvider
{
    private static AdventureCompositionProof? TryVerifyAdventureComposition(ProjectPaths paths,
        ProjectGame game, byte[] baselineMain, byte[] currentMain, CancellationToken token)
    {
        const int maximumBytes = 64 * 1024 * 1024;
        var relative = new RelativeOutputPath(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        try
        {
            var baseline = InGameSettingsPackageApplicationService.ReviewTargetAsync(paths.BaseRomFsPath!,
                new(relative.Value[6..]), maximumBytes, token).GetAwaiter().GetResult();
            if (!baseline.Exists) return null;
            var current = InGameSettingsPackageApplicationService.ReviewTargetAsync(paths.OutputRootPath!,
                relative, maximumBytes, token).GetAwaiter().GetResult();
            if (!SwShKnownExecutableCompositionVerifier.IsCompatibleRegisteredOutputWithAdventureArchive(baselineMain, currentMain,
                    game, baseline.Bytes.ToArray(), (current.Exists ? current : baseline).Bytes.ToArray())) return null;
            // Both archive observations are part of the reviewed proof, including an absent output archive.
            return new(new(relative, current.State), new(
                Path.GetFullPath(Path.Combine(paths.BaseRomFsPath!, relative.Value[6..])),
                baseline.State.LengthBytes, baseline.State.Sha256, maximumBytes));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed record AdventureCompositionProof(OutputReadDependency OutputDependency,
        InGameSettingsExternalSourceDependency BaseDependency);
}
