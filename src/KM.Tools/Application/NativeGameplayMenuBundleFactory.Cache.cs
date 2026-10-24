// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Core.Projects;
using KM.SV.RuntimeSettings;
using KM.SwSh.RuntimeSettings;
using KM.ZA.RuntimeSettings;

namespace KM.Tools.Application;

public static partial class NativeGameplayMenuBundleFactory
{
    private const long MaximumRetainedMainBytes = 64L * 1024 * 1024;
    private static readonly object RuntimeMainSync = new();
    private static readonly Dictionary<(ProjectGame Game, string Base, string Source), byte[]> RuntimeMains = [];
    private static long retainedMainBytes;

    private static byte[] GetRuntimeMain(ProjectGame game, byte[] sourceMain, byte[] composedSourceMain)
    {
        // These are private copies made by CreateEntry. Only the deterministic
        // transform is reused. Package, ownership and current source checks still run.
        var key = (game, Convert.ToHexString(SHA256.HashData(sourceMain)),
            Convert.ToHexString(SHA256.HashData(composedSourceMain)));
        lock (RuntimeMainSync)
        {
            if (RuntimeMains.TryGetValue(key, out var retained)) return retained.ToArray();
            var result = game switch
            {
                ProjectGame.Scarlet => SvGameplaySettingsMainPatcher.BuildRuntimeManaged(
                    sourceMain, composedSourceMain, SvGameplayRuntimeEdition.Scarlet).Main,
                ProjectGame.Violet => SvGameplaySettingsMainPatcher.BuildRuntimeManaged(
                    sourceMain, composedSourceMain, SvGameplayRuntimeEdition.Violet).Main,
                ProjectGame.Sword or ProjectGame.Shield => SwShStaticGameplaySettingsMainPatcher
                    .BuildRuntimeManaged(sourceMain, composedSourceMain, game).Main,
                ProjectGame.ZA => ZaStaticGameplaySettingsMainPatcher.BuildRuntimeManaged(
                    sourceMain, composedSourceMain, game).Main,
                _ => throw new ArgumentOutOfRangeException(nameof(game)),
            };
            if (result.LongLength <= MaximumRetainedMainBytes)
            {
                if (RuntimeMains.Count >= 2 || retainedMainBytes + result.LongLength > MaximumRetainedMainBytes)
                {
                    RuntimeMains.Clear();
                    retainedMainBytes = 0;
                }
                RuntimeMains[key] = result.ToArray();
                retainedMainBytes += result.LongLength;
            }
            return result;
        }
    }
}
