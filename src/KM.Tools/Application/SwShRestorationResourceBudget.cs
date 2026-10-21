// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.Application;

internal static class SwShRestorationResourceBudget
{
    internal static void Validate(IReadOnlyDictionary<string, byte[]> resources)
    {
        var textures = resources.Where(p => p.Key.EndsWith(".bntx", StringComparison.Ordinal)).Select(p => p.Value.LongLength).ToArray();
        var total = resources.Values.Sum(bytes => bytes.LongLength);
        if (total > 16L * 1024 * 1024
            || textures.Sum() > 8L * 1024 * 1024 || textures.Any(length => length > 2L * 1024 * 1024))
            throw new InvalidDataException($"Converted model resources exceed the supported loading budget: {total} decoded bytes, {textures.Sum()} texture bytes, and {textures.DefaultIfEmpty().Max()} bytes in the largest texture.");
    }
}
