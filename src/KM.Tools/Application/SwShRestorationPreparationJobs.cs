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
    private Job? current;
    private bool disposed;
    private sealed class Job(ProjectPaths paths, string definition) : IProgress<SwShRestorationPreparationProgress>
    {
        internal readonly ProjectPaths Paths = paths;
        internal readonly CancellationTokenSource Cancellation = new();
        internal RestorationPreparationDto Status = new(Guid.NewGuid().ToString("N"), definition, "preparing", "sources", 0, 3, null, new Dictionary<int, int[]>(), []);
        internal FileStream? Lease;
        public void Report(SwShRestorationPreparationProgress value)
        {
            lock (this) Status = Status with { Phase = value.Phase, CompletedSpecies = value.CompletedSpecies, TotalSpecies = value.TotalSpecies };
        }
    }
    public RestorationPreparationDto Start(ProjectPaths paths, string definition, string donor, string? support)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current is not null && Read(current).Status == "preparing") throw new InvalidDataException("A restoration preparation is already running. Wait for it or cancel it.");
            current?.Lease?.Dispose(); current?.Cancellation.Dispose();
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
    private static RestorationPreparationDto Read(Job job) { lock (job) return job.Status; }
    private async Task Run(Job job, string donor, string? support)
    {
        try
        {
            await BoundedParallel.RunAsync(Policy, token =>
            {
                var prepared = new SwShRestorationPreparationService().Prepare(job.Paths, donor, support, job.Status.DefinitionId, job, token);
                token.ThrowIfCancellationRequested();
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
        finally { lock (gate) { if (disposed) job.Lease?.Dispose(); } }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; current?.Cancellation.Cancel(); current?.Lease?.Dispose(); }
    }
}
