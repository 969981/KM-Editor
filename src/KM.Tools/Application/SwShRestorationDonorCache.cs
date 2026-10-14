// SPDX-License-Identifier: GPL-3.0-only
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

/// <summary>Shares each selected donor while keeping an in flight conversion's source alive.</summary>
public sealed class SwShRestorationDonorCache : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<SwShRestorationDonor, Entry> entries = [];
    private bool disposed;
    private sealed class Entry(SwShRestorationDonorSession session)
    {
        internal readonly SwShRestorationDonorSession Session = session;
        internal int Users;
        internal bool Retired;
    }
    public sealed class Lease(SwShRestorationDonorSession session, Action release) : IDisposable
    {
        private Action? release = release;
        public SwShRestorationDonorSession Session { get; } = session;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
    public Lease Acquire(SwShRestorationDonor game, string root, string? support)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!entries.TryGetValue(game, out var entry) || !entry.Session.Matches(game, root, support))
            {
                var replacement = new Entry(new(game, root, support));
                if (entry is not null) { entry.Retired = true; if (entry.Users == 0) entry.Session.Dispose(); }
                entries[game] = entry = replacement;
            }
            entry.Users++;
            return new(entry.Session, () => { lock (gate) { if (--entry.Users == 0 && entry.Retired) entry.Session.Dispose(); } });
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var entry in entries.Values) { entry.Retired = true; if (entry.Users == 0) entry.Session.Dispose(); }
            entries.Clear();
        }
    }
}
