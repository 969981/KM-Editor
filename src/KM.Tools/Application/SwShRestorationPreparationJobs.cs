// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Restoration;
using KM.Core.Concurrency;
using KM.Core.Projects;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed class SwShRestorationPreparationJobs : IDisposable
{
    private static readonly BoundedConcurrencyPolicy Policy = new("Pokemon restoration preparation", BoundedWorkloadKind.Decode,
        768L * 1024 * 1024, maximumDegreeOfParallelism: 1);
    private readonly object gate = new();
    private readonly SwShRestorationDonorCache donors = new();
    private Job? current;
    private readonly Dictionary<(ProjectPaths Paths, string Definition), FileStream> preparedLeases = [];
    private bool disposed;
    private sealed class Job(ProjectPaths paths, string definition) : IProgress<SwShRestorationPreparationProgress>
    {
        internal readonly ProjectPaths Paths = paths;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly System.Diagnostics.Stopwatch Timer = System.Diagnostics.Stopwatch.StartNew();
        internal RestorationPreparationDto Status = new(Guid.NewGuid().ToString("N"), definition, "preparing", "sources", 0,
            SwShRestorationDefinitions.Get(definition).Identities.Count, null, new Dictionary<int, int[]>(), []);
        internal FileStream? Lease;
        public void Report(SwShRestorationPreparationProgress value)
        {
            lock (this) Status = Status with { Phase = value.Phase, CompletedSpecies = value.CompletedSpecies, TotalSpecies = value.TotalSpecies,
                Activity = value.Activity, Species = value.Species ?? Status.Species, CompletedUnits = value.CompletedUnits, TotalUnits = value.TotalUnits };
        }
    }
    public RestorationDonorResponse InspectDonor(string donor, string root, string? support)
    {
        if (!Enum.TryParse<SwShRestorationDonor>(donor, out var game) || !Enum.IsDefined(game)) throw new InvalidDataException("The donor game is not supported.");
        using var lease = donors.Acquire(game, root, support);
        return new(lease.Session.Mode);
    }
    public RestorationPreparationDto Start(ProjectPaths paths, string definition, string donor, string? support)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current is not null && Read(current).Status == "preparing") throw new InvalidDataException("A restoration preparation is already running. Wait for it or cancel it.");
            if (current is { Lease: { } lease } completed)
            {
                var key = (completed.Paths, completed.Status.DefinitionId);
                if (preparedLeases.Remove(key, out var previous)) previous.Dispose();
                if (preparedLeases.Count >= 256)
                {
                    var oldest = preparedLeases.First(); preparedLeases.Remove(oldest.Key); oldest.Value.Dispose();
                }
                preparedLeases.Add(key, lease); completed.Lease = null;
            }
            current?.Cancellation.Dispose();
            var job = current = new(paths, definition);
            _ = Run(job, donor, support);
            return Read(job);
        }
    }
    public RestorationPreparationDto Get(ProjectPaths paths, string id, bool cancel = false)
    {
        lock (gate)
        {
            if (current is null || current.Paths != paths || current.Status.Id != id) throw new InvalidDataException("Restoration preparation is no longer available for this project.");
            if (cancel) current.Cancellation.Cancel();
            return Read(current);
        }
    }
    private static RestorationPreparationDto Read(Job job) { lock (job) return job.Status with {
        ElapsedMilliseconds = job.Timer.ElapsedMilliseconds, CancellationRequested = job.Cancellation.IsCancellationRequested }; }
    private async Task Run(Job job, string donor, string? support)
    {
        try
        {
            await BoundedParallel.RunAsync(Policy, token =>
            {
                using var source = donors.Acquire(SwShRestorationDefinitions.Get(job.Status.DefinitionId).Donor, donor, support);
                lock (job) job.Status = job.Status with { CacheMode = source.Session.Mode };
                var prepared = new SwShRestorationPreparationService().Prepare(job.Paths, donor, support, job.Status.DefinitionId, job, token, source.Session);
                token.ThrowIfCancellationRequested();
                job.Report(new("saving", job.Status.TotalSpecies, job.Status.TotalSpecies));
                var stored = SwShRestorationStateStore.Store(job.Paths, prepared, token);
                lock (job)
                {
                    job.Lease = stored.Lease;
                    job.Status = job.Status with { Status = "prepared", Phase = "complete", PreparedHash = stored.Hash, UnavailableSourceMoves = prepared.UnavailableSourceMoves };
                }
            }, job.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var cause = exception is BoundedWorkItemException { InnerException: { } inner } ? inner : exception;
            var canceled = job.Cancellation.IsCancellationRequested || cause is OperationCanceledException;
            lock (job) job.Status = job.Status with { Status = canceled ? "canceled" : "failed",
                Diagnostics = canceled ? [] : [new(ApiDiagnosticSeverity.Error, cause.Message, Domain: SwShRestorationDefinitions.Domain) { Code = SwShRestorationService.SourceCode }] };
        }
        finally { job.Timer.Stop(); lock (gate) { if (disposed) job.Lease?.Dispose(); } }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true; current?.Cancellation.Cancel(); current?.Lease?.Dispose();
            foreach (var lease in preparedLeases.Values) lease.Dispose(); preparedLeases.Clear();
            donors.Dispose();
        }
    }
}
