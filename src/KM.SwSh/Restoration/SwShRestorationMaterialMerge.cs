// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationMaterialMerge
{
    internal static byte[]? TryMerge(string path, byte[] previous, byte[] next, byte[] current)
    {
        if (!path.EndsWith(".gfbmdl", StringComparison.Ordinal)) return null;
        try
        {
            var baseline = new ModelMaterialDocument(previous, true);
            var customized = new ModelMaterialDocument(current, true);
            var edits = customized.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
            if (baseline.Fields.Count != edits.Count) return null;
            var changes = new List<(ModelMaterialField Before, ModelMaterialField After)>();
            foreach (var field in baseline.Fields)
            {
                if (!edits.TryGetValue(field.Key, out var edited)) return null;
                if (!Equal(field, edited)) changes.Add((field, edited));
            }
            if (changes.Count is 0 or > 512) return null;
            // Prove that the override contains only supported material edits.
            // Geometry, skeleton, and unknown metadata edits cannot hide in this merge.
            if (!baseline.Apply(changes.Select(change => new ModelMaterialChange(change.Before.Key,
                change.After.Values, change.After.Text)).ToArray()).AsSpan().SequenceEqual(current)) return null;
            var updated = new ModelMaterialDocument(next, true);
            var merged = new List<ModelMaterialChange>();
            foreach (var (before, after) in changes)
            {
                var matches = updated.Fields.Where(field => field.Material == before.Material && field.Group == before.Group
                    && field.Name == before.Name && field.Kind == before.Kind).ToArray();
                if (matches.Length != 1 || !matches[0].Editable || !Equal(matches[0], before) && !Equal(matches[0], after)) return null;
                merged.Add(new(matches[0].Key, after.Values, after.Text));
            }
            return updated.Apply(merged);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static bool Equal(ModelMaterialField left, ModelMaterialField right) => left.Kind == right.Kind
        && left.Text == right.Text && left.Values.AsSpan().SequenceEqual(right.Values);
}
