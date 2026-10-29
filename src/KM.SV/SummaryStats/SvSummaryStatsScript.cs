// SPDX-License-Identifier: GPL-3.0-only
using KM.SV.Workflows;

namespace KM.SV.SummaryStats;

internal static class SvSummaryStatsScript
{
    public const string VirtualPath = RuntimeSettings.SvGameplayOptionsLuaTransformer.SourceRelativePath;

    public static bool IsEnabled(byte[] source) => SvSharedScript.Read(source).SummaryStats;

    public static byte[] Apply(byte[] source, bool enabled)
    {
        var current = SvSharedScript.Read(source);
        return current.SummaryStats == enabled && !current.SummaryStatsNeedsUpdate ? source.ToArray()
            : SvSharedScript.Compose(current with { SummaryStats = enabled });
    }
}
