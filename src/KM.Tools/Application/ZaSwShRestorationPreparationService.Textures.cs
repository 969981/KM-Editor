// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed class RestorationTexturePool(Dictionary<string, byte[]> members, string folder, bool share)
    {
        private readonly Dictionary<string, string> identities = new(StringComparer.Ordinal);

        internal string Add(string name, byte[] bytes)
        {
            if (share)
            {
                // Compare the complete container with a neutral identity. All mip pixels,
                // formats and sampling metadata must agree before sharing a texture.
                var neutral = ModelTextureIdentity.Rename(bytes, name, new string('x', name.Length));
                var key = Convert.ToHexString(SHA256.HashData(neutral));
                if (identities.TryGetValue(key, out var existing)) return existing;
                identities.Add(key, name);
                if (members.TryGetValue(folder + name + ".bntx", out var previous) && !previous.AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("Converted texture identity has different image content.");
            }
            members[folder + name + ".bntx"] = bytes;
            return name;
        }
    }
}
