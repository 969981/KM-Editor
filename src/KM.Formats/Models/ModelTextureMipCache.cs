// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.Models;

/// <summary>Reuses exact encoded mip payloads within one sequential conversion, retaining at most eight MiB.</summary>
public sealed class ModelTextureMipCache
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    private const int MaximumEntries = 256;
    private readonly Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
    private int retainedBytes;

    internal byte[] GetOrEncode(byte[] original, IReadOnlyList<byte[]> levels, Func<byte[]> encode, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, original.Length); hash.AppendData(length); hash.AppendData(original);
        BinaryPrimitives.WriteInt32LittleEndian(length, levels.Count); hash.AppendData(length);
        foreach (var level in levels)
        {
            token.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt32LittleEndian(length, level.Length); hash.AppendData(length); hash.AppendData(level);
        }
        token.ThrowIfCancellationRequested();
        var key = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (entries.TryGetValue(key, out var previous)) return previous.ToArray();
        var encoded = encode();
        if (entries.Count < MaximumEntries && encoded.Length <= MaximumBytes - retainedBytes)
        {
            entries.Add(key, encoded.ToArray()); retainedBytes += encoded.Length;
        }
        return encoded;
    }
}
