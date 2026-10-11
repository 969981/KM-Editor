// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Executable;

namespace KM.SwSh.RuntimeSettings;

public static class SwShNativeMenuFeatureReader
{
    public static SwShNativeMenuFeatures Read(byte[] runtime)
    {
        var nso = NsoFile.Parse(runtime);
        foreach (var segment in new[] { nso.Text, nso.Ro, nso.Data })
            if (!NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The native menu module checksum is invalid.");
        ReadOnlySpan<byte> marker = "KMSWSHFEATURE01!"u8;
        var data = nso.Data.DecompressedData.AsSpan();
        var offset = data.IndexOf(marker);
        if (offset < 0 || offset > data.Length - 20 || data[(offset + 1)..].IndexOf(marker) >= 0)
            throw new InvalidDataException("The native menu feature configuration is missing or ambiguous.");
        var features = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 16)..]);
        if (features is < 1 or > 7) throw new InvalidDataException("The native menu feature configuration is invalid.");
        return (SwShNativeMenuFeatures)features;
    }
}
