// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Output;
using KM.Core.Projects;
using KM.SwSh.Editing;

namespace KM.SwSh.Restoration;

/// <summary>Reuses verified immutable artifacts during one operation, never mutable output state.</summary>
public sealed class SwShRestorationReadScope : IDisposable
{
    private const long MaximumCachedBytes = 512L * 1024 * 1024;
    // Leave room for one artifact at the coordinator's per-file limit. Stop before
    // prefetching would evict the prefix that the caller is about to consume.
    private const long PrefetchStopBytes = MaximumCachedBytes - 192L * 1024 * 1024;
    private static readonly AsyncLocal<SwShRestorationReadScope?> Ambient = new();
    private readonly SwShRestorationReadScope? parent;
    private readonly SwShRestorationReadScope owner;
    private readonly ProjectPaths paths;
    private readonly string? outputRoot;
    private readonly Dictionary<string, Artifact> artifacts = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private long cachedBytes;
    private bool disposed;

    private sealed class Artifact(byte[] bytes, FileStream lease)
    {
        internal readonly byte[] Bytes = bytes;
        internal readonly FileStream Lease = lease;
        internal SwShPreparedRestoration? Prepared;
        internal long Size = bytes.LongLength;
    }

    private SwShRestorationReadScope(ProjectPaths project)
    {
        paths = SwShChangePlanSourceGuard.OriginalPaths(project);
        outputRoot = NormalizeRoot(paths.OutputRootPath);
        parent = Ambient.Value;
        owner = Find(paths) ?? this;
        Ambient.Value = this;
    }

    public static SwShRestorationReadScope Begin(ProjectPaths paths) => new(paths);

    internal static SwShRestorationReadScope? Find(ProjectPaths paths)
    {
        var outputRoot = NormalizeRoot(paths.OutputRootPath);
        for (var scope = Ambient.Value; scope is not null; scope = scope.parent)
            if (!scope.disposed && !scope.owner.disposed && paths.SelectedGame == scope.paths.SelectedGame
                && outputRoot is not null && string.Equals(scope.outputRoot, outputRoot,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return scope.owner;
        return null;
    }

    private static string? NormalizeRoot(string? path) => string.IsNullOrWhiteSpace(path)
        ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal byte[] ReadBytes(string hash)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Ensure([hash]);
            return artifacts[hash].Bytes.ToArray();
        }
    }

    internal void Prefetch(IEnumerable<string> hashes)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cachedBytes >= PrefetchStopBytes) return;
            var missing = hashes.Distinct(StringComparer.Ordinal).Where(h => !artifacts.ContainsKey(h)).ToArray();
            OutputTransactionCoordinator.ForProject(paths).VisitPreservationArtifactsAsync(missing, (hash, bytes, lease) =>
            {
                artifacts.Add(hash, new(bytes, lease));
                cachedBytes += bytes.LongLength;
                Trim(hash);
                return cachedBytes < PrefetchStopBytes;
            }).GetAwaiter().GetResult();
        }
    }

    internal IReadOnlyDictionary<string, SwShPreparedRestoration> ReadPrepared(IReadOnlyCollection<string> hashes)
    {
        if (hashes.Count > 468) throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var result = new Dictionary<string, SwShPreparedRestoration>(StringComparer.Ordinal);
            long total = 0;
            // Bound each acquisition independently. Large valid sets can evict earlier entries.
            foreach (var hash in hashes)
            {
                Ensure([hash]);
                var artifact = artifacts[hash];
                total += artifact.Bytes.LongLength;
                if (total > MaximumCachedBytes)
                    throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
                if (artifact.Prepared is null)
                {
                    var prepared = SwShPreparedRestoration.Decode(artifact.Bytes);
                    if (prepared.Game != paths.SelectedGame)
                        throw new InvalidDataException("Prepared restoration belongs to another game edition.");
                    artifact.Prepared = prepared;
                    var size = prepared.Contributions.SelectMany(c => c.Before is null ? new[] { c.After } : new[] { c.Before, c.After })
                        .Distinct<byte[]>(ReferenceEqualityComparer.Instance).Sum(b => b.LongLength);
                    artifact.Size += size;
                    cachedBytes += size;
                    Trim(hash);
                }
                // Public callers may edit contribution buffers. Keep the verified copy private.
                result.Add(hash, Copy(artifact.Prepared));
            }
            return result;
        }
    }

    private void Ensure(IReadOnlyCollection<string> hashes)
    {
        var missing = hashes.Where(h => !artifacts.ContainsKey(h)).ToArray();
        if (missing.Length == 0) return;
        var acquired = OutputTransactionCoordinator.ForProject(paths).AcquirePreservationArtifactsAsync(missing).GetAwaiter().GetResult();
        try
        {
            foreach (var (hash, value) in acquired)
            {
                artifacts.Add(hash, new(value.Bytes, value.Lease));
                cachedBytes += value.Bytes.LongLength;
                Trim(hash);
            }
        }
        catch
        {
            foreach (var (hash, value) in acquired)
                if (!artifacts.ContainsKey(hash)) value.Lease.Dispose();
            throw;
        }
    }

    private void Trim(string retained)
    {
        foreach (var hash in artifacts.Keys.Where(h => h != retained).ToArray())
        {
            if (cachedBytes <= MaximumCachedBytes && artifacts.Count <= 468) break;
            var artifact = artifacts[hash];
            artifacts.Remove(hash);
            cachedBytes -= artifact.Size;
            artifact.Lease.Dispose();
        }
    }

    private static SwShPreparedRestoration Copy(SwShPreparedRestoration prepared) => prepared with
    {
        Sources = prepared.Sources.ToArray(),
        Contributions = prepared.Contributions.Select(c => c with { Before = c.Before?.ToArray(), After = c.After.ToArray() }).ToArray(),
        UnavailableSourceMoves = prepared.UnavailableSourceMoves.ToDictionary(p => p.Key, p => p.Value.ToArray()),
    };

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (Ambient.Value == this) Ambient.Value = parent;
            if (owner != this) return;
            foreach (var artifact in artifacts.Values) artifact.Lease.Dispose();
            artifacts.Clear();
            cachedBytes = 0;
        }
    }
}
