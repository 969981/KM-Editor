// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Executable;
using KM.SwSh.RuntimeSettings;

namespace KM.Tools.Application;

public static partial class NativeGameplayMenuBundleFactory
{
    private static byte[] ConfigureSwShFeatures(byte[] runtime, SwShNativeMenuFeatures features)
    {
        if (features is not (SwShNativeMenuFeatures.Camera or SwShNativeMenuFeatures.Gameplay or SwShNativeMenuFeatures.All))
            throw new ArgumentOutOfRangeException(nameof(features));
        var nso = NsoFile.Parse(runtime);
        var data = nso.Data.DecompressedData.ToArray();
        ReadOnlySpan<byte> marker = "KMSWSHFEATURE01!"u8;
        var offset = data.AsSpan().IndexOf(marker);
        if (offset < 0 || offset + 20 > data.Length || data.AsSpan(offset + 1).IndexOf(marker) >= 0
            || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16)) != 3)
            throw new InvalidDataException("The native menu feature configuration is invalid.");
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 16), (uint)features);
        return nso.Write(dataDecompressedData: data);
    }
}
