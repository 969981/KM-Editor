// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;

namespace KM.Formats.PLA;

public sealed class PlaCompressionRuntime : IDisposable
{
    public const string RequiredFileName = "oo2core_8_win64.dll";
    private nint handle;
    private readonly Decode decode;
    private readonly Encode encode;
    private readonly Capacity capacity;

    public PlaCompressionRuntime(string? folder)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new InvalidDataException("PLA archive compression requires a supported 64 bit runtime.");
        if (string.IsNullOrWhiteSpace(folder) || !File.Exists(Path.Combine(folder, RequiredFileName)))
            throw new InvalidDataException("Set the PLA compression support folder in Project Setup before opening Wild Encounters.");
        try
        {
            handle = NativeLibrary.Load(Path.GetFullPath(Path.Combine(folder, RequiredFileName)));
            decode = Export<Decode>("OodleLZ_Decompress");
            encode = Export<Encode>("OodleLZ_Compress");
            capacity = Export<Capacity>("OodleLZ_GetCompressedBufferSizeNeeded");
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Dispose();
            throw new InvalidDataException("The PLA compression runtime is incompatible or incomplete. Check the support folder.");
        }
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));
    private void RequireOpen() => ObjectDisposedException.ThrowIf(handle == 0, this);
    public byte[] Decompress(ReadOnlySpan<byte> bytes, int size)
    {
        RequireOpen();
        if (size is < 1 or > PlaFlatBuffer.MaximumLength || bytes.Length is < 1 or > PlaFlatBuffer.MaximumLength)
            throw new InvalidDataException("The PLA compressed member size is unsupported.");
        var input = bytes.ToArray();
        var output = new byte[size];
        if (decode(input, input.Length, output, size, 1, 0, 0, 0, 0, 0, 0, 0, 0, 3) != size)
            throw new InvalidDataException("The PLA archive member could not be decoded completely.");
        return output;
    }
    public byte[] Compress(ReadOnlySpan<byte> bytes)
    {
        RequireOpen();
        if (bytes.Length is < 1 or > PlaFlatBuffer.MaximumLength)
            throw new InvalidDataException("The PLA member size is unsupported.");
        var required = capacity(8, bytes.Length);
        if (required < bytes.Length || required > PlaFlatBuffer.MaximumLength + 1024 * 1024)
            throw new InvalidDataException("The PLA compression buffer size is invalid.");
        var input = bytes.ToArray();
        var output = new byte[(int)required];
        var length = encode(8, input, input.Length, output, 4, 0, 0, 0, 0, 0);
        if (length is < 1 || length > output.Length)
            throw new InvalidDataException("The PLA archive member could not be encoded.");
        var result = output.AsSpan(0, (int)length).ToArray();
        if (!Decompress(result, input.Length).AsSpan().SequenceEqual(input))
            throw new InvalidDataException("The encoded PLA archive member failed verification.");
        return result;
    }
    public void Dispose()
    {
        if (handle == 0) return;
        NativeLibrary.Free(handle);
        handle = 0;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Decode(byte[] input, long inputSize, byte[] output, long outputSize, int fuzzSafe, int crc, int verbosity,
        nint bufferBase, long bufferSize, nint callback, nint callbackData, nint memory, long memorySize, int phase);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Encode(int codec, byte[] input, long inputSize, byte[] output, int level, nint options, nint dictionary,
        nint matcher, nint memory, long memorySize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Capacity(int codec, long inputSize);
}
