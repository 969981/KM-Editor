// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SV;

public static partial class SvSwShLavaMaterial
{
    private static string[] Controls(Contract c) => (c.Eye ? EyeUv : BodyUv).Concat(c.Eye ? [] : SvSwShLayerEmission.Scalars).Append(ExtraEmission).ToArray();

    public static PreviewClip Read(byte[] bytes, PreviewClip skeleton, PreviewScene scene,
        IReadOnlyDictionary<string, Contract> contracts, IReadOnlyDictionary<string, ZaSwShEyelidMaterial.Contract> eyes, bool visibility)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var retainedReferences = new List<(int[] Slots, int[] Entries)>();
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeleton.Rate);
        if (rate == 0) throw new InvalidDataException("Lava animation rate is invalid.");
        var additional = new List<PreviewMaterialTrack>();
        PreviewKey[] Keys(int curve) => source.Tables(curve, 0, 18000).Select(k =>
        {
            float Value(int f) => source.Field(k, f) is var at && at != 0 ? source.Float(at) : 0;
            return new PreviewKey(Value(0) * skeleton.Rate / rate, [Value(1)]);
        }).ToArray();
        void Add(string material, string parameter, PreviewKey[] keys)
        {
            if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]))
                || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame)) throw new InvalidDataException("Lava animation keys are invalid.");
            additional.Add(new(material, "EyeScalar:" + parameter, [keys]));
        }
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4); if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var name = source.Text(material, 0)!;
                if (!contracts.TryGetValue(name, out var contract) || !scene.Primitives.Any(p => p.Name == source.Text(track, 0) && p.Material.Name == name))
                    throw new InvalidDataException("Lava animation target is not qualified.");
                var retained = new List<int>();
                foreach (var scalar in source.Tables(material, 1, 256))
                {
                    var parameter = source.Text(scalar, 0)!;
                    var index = Array.IndexOf(new[] { "EmissionIntensityLayer1", "EmissionIntensityLayer2", "EmissionIntensityLayer3", "EmissionIntensityLayer4" }, parameter);
                    var native = parameter == (contract.Eye ? "EmissionIntensityLayer9" : "EmissionIntensityLayer5") ? ExtraEmission
                        : !contract.Eye && index >= 0 ? SvSwShLayerEmission.Scalars[index] : null;
                    if (native is null) { retained.Add(scalar); continue; }
                    var keys = Keys(source.Table(scalar, 1));
                    if (keys.Any(k => k.Value[0] is < 0 or > 8)) throw new InvalidDataException("Lava emission is outside its supported range.");
                    Add(name, native, keys);
                }
                var scalarSlots = writer.Vector(material, 1, retained.Count);
                retainedReferences.Add((scalarSlots, retained.ToArray()));
                retained.Clear();
                foreach (var vector in source.Tables(material, 2, 256))
                {
                    if (source.Text(vector, 0) != "UVScaleOffset" + (name == "fire" ? 3 : 5)) { retained.Add(vector); continue; }
                    var channels = source.Table(vector, 1); var parameters = contract.Eye ? EyeUv : BodyUv;
                    for (var c = 0; c < 4; c++)
                    {
                        var curve = source.Table(channels, c);
                        if (curve != 0) Add(name, parameters[c], Keys(curve));
                    }
                }
                var vectorSlots = writer.Vector(material, 2, retained.Count);
                retainedReferences.Add((vectorSlots, retained.ToArray()));
            }
        }
        var retainedCopy = writer.Append(bytes);
        foreach (var (slots, entries) in retainedReferences)
            for (var i = 0; i < entries.Length; i++) writer.Pointer(slots[i], retainedCopy + entries[i]);
        var decoded = ZaSwShAnimationWriter.ReadClip(writer.Finish(source.Root), skeleton, visibility, eyes: eyes);
        var distinct = additional.GroupBy(t => (t.Material, t.Parameter)).Select(g =>
        {
            var first = g.First();
            if (g.Any(t => !t.Channels[0].Select(k => (k.Frame, k.Value[0])).SequenceEqual(first.Channels[0].Select(k => (k.Frame, k.Value[0])))))
                throw new InvalidDataException("Lava material animation is ambiguous.");
            return first;
        }).ToArray();
        return decoded with { Materials = decoded.Materials.Concat(distinct).ToArray() };
    }

    public static byte[] GlobalLoop(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var retained = new List<(int[] Slots, int[] Entries)>();
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128))
        {
            if (!contracts.TryGetValue(source.Text(material, 0)!, out var contract)) continue;
            // These constants belong to Bind; the donor loop must not overwrite
            // the authored lava palette, displacement or texture controls.
            var scalars = Controls(contract).Append("SphereMapColorVal")
                .Concat(contract.Eye ? [] : new[] { "Sin01", "ScaleUV", "DistortionIntensity" }).ToHashSet(StringComparer.Ordinal);
            foreach (var field in new[] { 2, 3 })
            {
                var entries = source.Tables(material, field, 256).Where(p => field == 2
                    ? !scalars.Contains(source.Text(p, 0)!)
                    : source.Text(p, 0) is not ("EffectColor01" or "SphereMapColor")).ToArray();
                retained.Add((writer.Vector(material, field, entries.Length), entries));
            }
        }
        var copy = writer.Append(bytes);
        foreach (var (slots, entries) in retained) for (var i = 0; i < entries.Length; i++) writer.Pointer(slots[i], copy + entries[i]);
        return writer.Finish(source.Root);
    }

    public static byte[] Loop(byte[] bytes, PreviewClip loop, IReadOnlyDictionary<string, Contract> contracts)
    {
        var groups = loop.Materials.Where(t => contracts.TryGetValue(t.Material, out var c)
            && t.Parameter.StartsWith("EyeScalar:", StringComparison.Ordinal) && Controls(c).Contains(t.Parameter[10..], StringComparer.Ordinal))
            .GroupBy(t => t.Material).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        if (groups.Count == 0) return bytes;
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder();
        var root = writer.Table(5, absent: Enumerable.Range(0, 5).Where(f => f != 2 && source.Table(source.Root, f) == 0).ToHashSet());
        var parent = writer.Table(1); writer.Reference(root, 2, parent);
        var existing = source.Tables(source.Table(source.Root, 2), 0, 128);
        var names = existing.Select(m => source.Text(m, 0)!).Concat(groups.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var slots = writer.Vector(parent, 0, names.Length);
        var references = new List<(int Offset, int Target)>();
        foreach (var field in new[] { 1, 3, 4 })
            if (source.Table(source.Root, field) is var table && table != 0) references.Add((writer.Field(root, field), table));
        var frames = loop.Frames; var rate = loop.Rate;
        if (frames is < 1 or > 18000 || rate == 0) throw new InvalidDataException("Lava loop timing is invalid.");
        writer.Reference(root, 0, writer.Table(3, new Dictionary<int, byte[]>
            { [0] = ModelTableBuilder.Integer(1), [1] = ModelTableBuilder.Integer(frames), [2] = ModelTableBuilder.Integer(rate) }));
        for (var i = 0; i < names.Length; i++)
        {
            var original = existing.SingleOrDefault(m => source.Text(m, 0) == names[i]);
            if (!groups.TryGetValue(names[i], out var tracks)) { references.Add((slots[i], original)); continue; }
            var target = writer.Table(4); writer.Pointer(slots[i], target); writer.Text(target, 0, names[i]);
            foreach (var field in new[] { 1, 3 })
                if (original != 0 && source.Table(original, field) is var table && table != 0) references.Add((writer.Field(target, field), table));
                else writer.Vector(target, field, 0);
            var retained = original == 0 ? [] : source.Tables(original, 2, 256).Where(p => !tracks.Any(t => t.Parameter[10..] == source.Text(p, 0))).ToArray();
            var values = writer.Vector(target, 2, retained.Length + tracks.Length);
            for (var j = 0; j < retained.Length; j++) references.Add((values[j], retained[j]));
            for (var j = 0; j < tracks.Length; j++)
            {
                var track = tracks[j]; var keys = track.Channels.Single();
                float Sample(int frame)
                {
                    var at = frame * (float)loop.Rate / rate;
                    if (at <= keys[0].Frame) return keys[0].Value[0];
                    for (var k = 1; k < keys.Length; k++) if (at < keys[k].Frame)
                        return keys[k - 1].Value[0] + (keys[k].Value[0] - keys[k - 1].Value[0]) * (at - keys[k - 1].Frame) / (keys[k].Frame - keys[k - 1].Frame);
                    return keys[^1].Value[0];
                }
                var samples = Enumerable.Range(0, frames).Select(Sample).ToArray(); var constant = samples.All(v => v == samples[0]);
                var scalar = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                writer.Pointer(values[retained.Length + j], scalar); writer.Text(scalar, 0, track.Parameter[10..]);
                var curve = writer.Table(1); writer.Reference(scalar, 2, curve);
                if (constant) writer.Single(writer.Field(curve, 0), samples[0]);
                else writer.Blob(curve, 0, ModelTableBuilder.Floats(samples), frames);
            }
        }
        var copy = writer.Append(bytes);
        foreach (var (offset, target) in references) writer.Pointer(offset, copy + target);
        return writer.Finish(root);
    }

    public static byte[] Controller(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            if (source.Text(layer, 1) is "eye01" or "mouth01") continue;
            foreach (var material in source.Tables(source.Table(source.Table(layer, 2), 1), 1, 128))
            {
                if (!contracts.TryGetValue(source.Text(material, 0)!, out var contract)) continue;
                var floats = source.Table(material, 2);
                var parameters = source.Tables(floats, 1, 256).Select(p => source.Text(p, 0)!).Concat(Controls(contract)).Distinct(StringComparer.Ordinal).ToArray();
                var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [source.Field(floats, 0) is var at && at != 0 ? source.U8(at) : (byte)0] });
                writer.Reference(material, 2, replacement); var slots = writer.Vector(replacement, 1, parameters.Length);
                for (var i = 0; i < parameters.Length; i++) { var item = writer.Table(1); writer.Pointer(slots[i], item); writer.Text(item, 0, parameters[i]); }
            }
        }
        return writer.Finish(source.Root);
    }
}
