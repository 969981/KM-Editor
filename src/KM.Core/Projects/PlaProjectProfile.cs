// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Core.Projects;

public static class PlaProjectProfile
{
    public const string SupportedBuildId = "AEE8F150DDA1B5A838806E1A5EA6827AD9F3C51E000000000000000000000000";
    public const string SupportedVersion = "1.1.1";

    public static bool Matches(ReadOnlySpan<byte> header) => header.Length >= 0x60
        && header[..4].SequenceEqual("NSO0"u8) && Convert.ToHexString(header.Slice(0x40, 32)) == SupportedBuildId;

    public static bool MatchesMetadata(ReadOnlySpan<byte> metadata)
    {
        if (metadata.Length < 0x80 || !metadata[..4].SequenceEqual("META"u8)) return false;
        var accessOffset = BinaryPrimitives.ReadUInt32LittleEndian(metadata[0x70..]);
        return accessOffset <= metadata.Length - 0x18
            && metadata.Slice((int)accessOffset, 4).SequenceEqual("ACI0"u8)
            && BinaryPrimitives.ReadUInt64LittleEndian(metadata[((int)accessOffset + 0x10)..]) == ProjectGameMetadata.Get(ProjectGame.PLA).TitleId;
    }
}
