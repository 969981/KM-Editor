// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;

namespace KM.PLA.Audio;

/// <summary>Owns PLA audio source admission and consistent read only media transfers.</summary>
public sealed class PlaSoundSource : IDisposable
{
    private sealed record Source(string Path, string Hash);
    private readonly ProjectPaths paths;
    private readonly Dictionary<string, Source> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileStream> transfer = new(StringComparer.Ordinal);
    private (string File, string Bank, long Offset, long Length)? selection;
    private long sourceBytes;

    public PlaSoundSource(ProjectPaths paths)
    {
        using var scope = new PlaSourceScope(paths);
        this.paths = paths;
    }

    public static bool Owns(string relative) => relative.StartsWith("romfs/bin/sound/NX64/wwise/bank/", StringComparison.Ordinal)
        && relative.Split('/').Length == 7 && Path.GetExtension(relative) is ".bnk" or ".pck";

    private string EffectivePath(string relative)
    {
        if (!Owns(relative)) throw new InvalidDataException("Select a supported PLA audio source.");
        var baseline = PlaSourceScope.Resolve(paths.BaseRomFsPath!, relative[6..]);
        var output = string.IsNullOrWhiteSpace(paths.OutputRootPath) ? null : PlaSourceScope.Resolve(paths.OutputRootPath, relative);
        return output is not null && File.Exists(output) ? output : baseline;
    }

    public void Observe(string relative, string path, Stream stream, CancellationToken cancellationToken)
    {
        if (!string.Equals(EffectivePath(relative), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The PLA audio source changed. Reload Sound Studio.");
        sourceBytes = checked(sourceBytes + stream.Length);
        if (sources.Count >= 256 || sourceBytes > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("The PLA audio catalog exceeds its source budget.");
        sources.Add(relative, new(Path.GetFullPath(path), Fingerprint(stream, cancellationToken)));
    }

    private static string Fingerprint(Stream stream, CancellationToken cancellationToken)
    {
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = stream.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer.AsSpan(0, count));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { stream.Position = position; }
    }

    public byte[] ReadMedia(string file, string bank, long offset, long length, long readOffset, CancellationToken cancellationToken)
    {
        if (offset < 0 || length is <= 0 or > 64L * 1024 * 1024 || readOffset < 0 || readOffset >= length)
            throw new InvalidDataException("The PLA audio selection is invalid.");
        var key = (file, bank, offset, length);
        try
        {
            if (selection != key || readOffset == 0)
            {
                EndTransfer();
                foreach (var relative in new[] { file, bank }.Distinct(StringComparer.Ordinal))
                {
                    if (!sources.TryGetValue(relative, out var source)) throw new InvalidDataException("The PLA audio source is unavailable.");
                    CheckSource(relative, source);
                    var lease = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    transfer.Add(relative, lease);
                    if (Fingerprint(lease, cancellationToken) != source.Hash)
                        throw new InvalidDataException("The PLA audio source changed. Reload Sound Studio.");
                }
                selection = key;
            }
            foreach (var relative in transfer.Keys) CheckSource(relative, sources[relative]);
            cancellationToken.ThrowIfCancellationRequested();
            var stream = transfer[file];
            if (offset > stream.Length || length > stream.Length - offset) throw new InvalidDataException("The PLA audio selection exceeds its source.");
            var result = new byte[(int)Math.Min(512 * 1024, length - readOffset)];
            stream.Position = checked(offset + readOffset);
            stream.ReadExactly(result);
            if (readOffset + result.Length == length) EndTransfer();
            return result;
        }
        catch { EndTransfer(); throw; }
    }

    private void CheckSource(string relative, Source source)
    {
        if (!string.Equals(EffectivePath(relative), source.Path, StringComparison.OrdinalIgnoreCase) || !File.Exists(source.Path))
            throw new InvalidDataException("The PLA audio source changed. Reload Sound Studio.");
    }

    private void EndTransfer()
    {
        foreach (var stream in transfer.Values) stream.Dispose();
        transfer.Clear();
        selection = null;
    }

    public void ReleaseMedia(string file, string bank, long offset, long length)
    {
        if (selection == (file, bank, offset, length)) EndTransfer();
    }

    public void Dispose() => EndTransfer();
}
