// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

/// <summary>Shares byte identical animation resources without changing controller playback.</summary>
public static class SwShAnimationResourceAliases
{
    public static Dictionary<string, byte[]> Compact(IReadOnlyDictionary<string, byte[]> members)
    {
        var result = members.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in members.Where(p => p.Key.EndsWith(".gfbanm", StringComparison.Ordinal))
            .GroupBy(p => Convert.ToHexString(SHA256.HashData(p.Value))))
        {
            var ordered = group.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(); var first = ordered[0];
            foreach (var duplicate in ordered.Skip(1))
            {
                if (!first.Value.AsSpan().SequenceEqual(duplicate.Value)) throw new InvalidDataException("Animation identity is ambiguous.");
                aliases.Add(duplicate.Key, first.Key);
            }
        }
        foreach (var pair in members.Where(p => p.Key.EndsWith(".gfbanmcfg", StringComparison.Ordinal)))
        {
            var source = new ModelBuffer(pair.Value); var writer = new ModelTableBuilder(pair.Value); var changed = false;
            foreach (var binding in source.Tables(source.Table(source.Root, 6), 0, 2048))
            {
                var path = TrinityPreviewReader.Resolve(pair.Key, source.Text(binding, 1)
                    ?? throw new InvalidDataException("Animation controller dependency is unnamed."));
                if (!members.ContainsKey(path)) throw new InvalidDataException("Animation controller dependency is missing.");
                if (!aliases.TryGetValue(path, out var shared)) continue;
                var relative = Path.GetRelativePath(Path.GetDirectoryName(pair.Key)!, shared).Replace('\\', '/');
                if (TrinityPreviewReader.Resolve(pair.Key, relative) != shared) throw new InvalidDataException("Shared animation path is invalid.");
                writer.Text(binding, 1, relative); changed = true;
            }
            if (changed) result[pair.Key] = writer.Finish(source.Root);
        }
        foreach (var path in aliases.Keys) result.Remove(path);
        return result;
    }
}
