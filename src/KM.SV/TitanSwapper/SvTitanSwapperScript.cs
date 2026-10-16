// SPDX-License-Identifier: GPL-3.0-only
using KM.SV.RuntimeSettings;
using KM.SV.Workflows;

namespace KM.SV.TitanSwapper;

internal static class SvTitanSwapperScript
{
    public const string VirtualPath = SvGameplayOptionsLuaTransformer.SourceRelativePath;

    public static IReadOnlyList<string> ReadLabels(byte[] source) => SvSharedScript.Read(source).TitanLabels;

    public static byte[] Apply(byte[] source, IEnumerable<string> labels)
    {
        var current = SvSharedScript.Read(source);
        var list = labels.Order(StringComparer.Ordinal).ToArray();
        SvSharedScript.ValidateLabels(list);
        return current.TitanLabels.SequenceEqual(list) ? source.ToArray()
            : SvSharedScript.Compose(current with { TitanLabels = list });
    }

    public static (byte[] Bytes, IReadOnlyList<string> Labels, bool HasGameplayOptions) Remove(byte[] source)
    {
        var current = SvSharedScript.Read(source);
        return (current.TitanLabels.Count == 0 ? source.ToArray() : SvSharedScript.Compose(current with { TitanLabels = [] }),
            current.TitanLabels, current.HasGameplayOptions);
    }
}
