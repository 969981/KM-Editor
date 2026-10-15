// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;

namespace KM.Core.Output;

public sealed partial class OutputTransactionCoordinator
{
    private const int MaximumPreservationArtifactBytes = 192 * 1024 * 1024;

    /// <summary>Reads immutable restoration data referenced by committed ownership claims.</summary>
    public Task<byte[]> ReadPreservationArtifactAsync(string sha256, CancellationToken cancellationToken = default) =>
        ExecuteExclusiveOutputOperationAsync(token => ReadPreservationArtifactCoreAsync(sha256, token), cancellationToken);

    /// <summary>Verifies artifacts and keeps read leases until the caller finishes using their contents.</summary>
    public async Task<IReadOnlyDictionary<string, (byte[] Bytes, FileStream Lease)>> AcquirePreservationArtifactsAsync(
        IReadOnlyCollection<string> hashes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count > 468) throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
        var result = new Dictionary<string, (byte[] Bytes, FileStream Lease)>(StringComparer.Ordinal);
        if (hashes.Count == 0) return result;
        try
        {
            await ExecuteExclusiveOutputOperationAsync(async token =>
            {
                long total = 0;
                foreach (var hash in hashes.Distinct(StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    var artifact = await AcquirePreservationArtifactCoreAsync(hash, token).ConfigureAwait(false);
                    result.Add(hash, artifact);
                    total += artifact.Bytes.Length;
                    if (total > 512L * 1024 * 1024)
                        throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
                }
                return true;
            }, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            foreach (var artifact in result.Values) artifact.Lease.Dispose();
            throw;
        }
    }

    /// <summary>Verifies a bounded set of immutable artifacts under one recovery and workspace lease.</summary>
    public Task<IReadOnlyDictionary<string, byte[]>> ReadPreservationArtifactsAsync(IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count > 468) throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
        var identities = hashes.Distinct(StringComparer.Ordinal).ToArray();
        if (identities.Length == 0) return Task.FromResult<IReadOnlyDictionary<string, byte[]>>(new Dictionary<string, byte[]>());
        return ExecuteExclusiveOutputOperationAsync<IReadOnlyDictionary<string, byte[]>>(async token =>
        {
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal); long total = 0;
            foreach (var identity in identities)
            {
                token.ThrowIfCancellationRequested();
                var bytes = await ReadPreservationArtifactCoreAsync(identity, token).ConfigureAwait(false);
                total += bytes.Length;
                if (total > 512L * 1024 * 1024) throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
                result.Add(identity, bytes);
            }
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Durably stores restoration data before its ownership reference is committed. The returned
    /// lease protects it through publication; an unreferenced artifact grants no output authority.
    /// </summary>
    public Task<FileStream> StorePreservationArtifactAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is 0 or > MaximumPreservationArtifactBytes)
            throw new OutputLimitExceededException("The restoration artifact exceeds its supported size.");
        var content = bytes.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        return ExecuteExclusiveOutputOperationAsync(async token =>
        {
            var root = paths.GetContainedMetadataPath(paths.MetadataRoot, "preservation");
            paths.EnsureMetadataDirectory(root, paths.MetadataRoot);
            var destination = PreservationArtifactPath(hash);
            if (!File.Exists(destination))
            {
                long size = content.LongLength;
                var count = 0;
                foreach (var entry in Directory.EnumerateFiles(root))
                {
                    paths.ValidateMetadataFile(entry);
                    size += new FileInfo(entry).Length;
                    if (++count >= 1024 || size > OutputLimits.MaximumCheckpointBytes)
                        throw new OutputLimitExceededException("The private restoration store is full. Existing restoration data was preserved.");
                }
                var temporary = paths.GetContainedMetadataPath(root, "." + hash + ".pending.tmp");
                paths.ValidateMetadataFile(temporary);
                try
                {
                    await using (var output = metadata.OpenPrivateFile(temporary, FileMode.Create, FileAccess.Write,
                        FileShare.None, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await output.WriteAsync(content, token).ConfigureAwait(false);
                        await output.FlushAsync(token).ConfigureAwait(false);
                        output.Flush(flushToDisk: true);
                    }
                    token.ThrowIfCancellationRequested();
                    paths.ValidateMetadataFile(destination);
                    OutputFileSystemDurability.Move(temporary, destination, overwrite: false);
                }
                finally
                {
                    paths.ValidateMetadataFile(temporary);
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            await ReadPreservationArtifactCoreAsync(hash, token).ConfigureAwait(false);
            return metadata.OpenPrivateFile(destination, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        }, cancellationToken);
    }

    private string PreservationArtifactPath(string hash)
    {
        if (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new InvalidDataException("The restoration artifact identity is invalid.");
        var root = paths.GetContainedMetadataPath(paths.MetadataRoot, "preservation");
        return paths.GetContainedMetadataPath(root, hash + ".bin");
    }

    private async Task<byte[]> ReadPreservationArtifactCoreAsync(string hash, CancellationToken token)
    {
        var artifact = await AcquirePreservationArtifactCoreAsync(hash, token).ConfigureAwait(false);
        using (artifact.Lease) return artifact.Bytes;
    }

    private async Task<(byte[] Bytes, FileStream Lease)> AcquirePreservationArtifactCoreAsync(string hash, CancellationToken token)
    {
        var path = PreservationArtifactPath(hash);
        if (!File.Exists(path)) throw new InvalidDataException("The private restoration artifact is missing.");
        var stream = metadata.OpenPrivateFile(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length is 0 or > MaximumPreservationArtifactBytes)
                throw new InvalidDataException("The restoration artifact size is invalid.");
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash)
                throw new InvalidDataException("The private restoration artifact has changed.");
            return (bytes, stream);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
