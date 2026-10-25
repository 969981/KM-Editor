// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Editing;

namespace KM.SwSh.Placement;

public sealed partial class SwShPlacementEditSessionService
{
    private sealed class PlacementPendingIndex
    {
        private readonly SwShPlacementWorkflow workflow;
        private readonly EditSession original;
        private readonly IReadOnlyDictionary<string, int> objects;
        private readonly LinkedList<PendingEdit> pending = new();
        private readonly Dictionary<(string?, string?, string?), HashSet<LinkedListNode<PendingEdit>>> exact = new();
        private readonly Dictionary<PlacementStorageIdentity, HashSet<LinkedListNode<PendingEdit>>> storage = new();

        internal PlacementPendingIndex(SwShPlacementWorkflow workflow, EditSession original, IReadOnlyDictionary<string, int> objects)
        {
            this.workflow = workflow; this.original = original; this.objects = objects;
            foreach (var edit in original.PendingEdits) Add(edit);
        }
        internal EditSession Session => original with { PendingEdits = pending.ToArray() };

        internal void Replace(PendingEdit edit)
        {
            var remove = new HashSet<LinkedListNode<PendingEdit>>();
            if (exact.TryGetValue((edit.Domain, edit.RecordId, edit.Field), out var matches)) remove.UnionWith(matches);
            if (TryCreateStorageIdentity(workflow, edit, out var identity, objects) && storage.TryGetValue(identity, out matches)) remove.UnionWith(matches);
            foreach (var node in remove)
            {
                Remove(exact, (node.Value.Domain, node.Value.RecordId, node.Value.Field), node);
                if (TryCreateStorageIdentity(workflow, node.Value, out var previous, objects)) Remove(storage, previous, node);
                pending.Remove(node);
            }
            Add(edit);
        }
        private void Add(PendingEdit edit)
        {
            var node = pending.AddLast(edit);
            Add(exact, (edit.Domain, edit.RecordId, edit.Field), node);
            if (TryCreateStorageIdentity(workflow, edit, out var identity, objects)) Add(storage, identity, node);
        }
        private static void Add<TKey>(Dictionary<TKey, HashSet<LinkedListNode<PendingEdit>>> index, TKey key, LinkedListNode<PendingEdit> node) where TKey : notnull
        {
            if (!index.TryGetValue(key, out var nodes)) index[key] = nodes = [];
            nodes.Add(node);
        }
        private static void Remove<TKey>(Dictionary<TKey, HashSet<LinkedListNode<PendingEdit>>> index, TKey key, LinkedListNode<PendingEdit> node) where TKey : notnull
        {
            if (!index.TryGetValue(key, out var nodes)) return;
            nodes.Remove(node);
            if (nodes.Count == 0) index.Remove(key);
        }
    }
}
