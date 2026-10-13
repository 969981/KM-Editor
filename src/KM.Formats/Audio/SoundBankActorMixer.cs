// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.Audio;

/// <summary>Reads references from the bounded version 128 mixer layout without optional parameter blocks.</summary>
public static class SoundBankActorMixer
{
    public static bool TryReadReferences(uint version, ReadOnlySpan<byte> payload, out uint[] references)
    {
        references = [];
        if (version != 128 || payload.Length < 30) return false;

        // Effects, attachment overrides, properties, positioning, auxiliary sends,
        // states, and RTPCs can introduce more fields. Admit only the complete
        // verified layout here, so unsupported blocks cannot conceal references.
        if (payload[0] > 1 || payload[1] != 0 || payload[2] != 0
            || (payload[11] & ~0x3F) != 0 || payload[12] != 0 || payload[13] != 0
            || payload[14] != 4 || payload[15] != 0 || payload[16] != 0
            || payload[17] > 2 || payload[20] > 2 || payload[21] != 0
            || payload[22] != 0 || payload[23] != 0 || payload[24] != 0 || payload[25] != 0)
            return false;

        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload[26..]);
        if (count > 200000 || payload.Length != 30L + count * 4L) return false;
        var result = new uint[checked((int)count + 2)];
        result[0] = BinaryPrimitives.ReadUInt32LittleEndian(payload[3..]); // Output bus.
        result[1] = BinaryPrimitives.ReadUInt32LittleEndian(payload[7..]); // Direct parent.
        for (var i = 0; i < count; i++)
            result[i + 2] = BinaryPrimitives.ReadUInt32LittleEndian(payload[(30 + i * 4)..]);
        references = result;
        return true;
    }
}
