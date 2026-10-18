// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Preserves independent S/V flame scrolling on native flame materials.</summary>
public static class SvSwShFlameAnimation
{
    public static PreviewClip Read(byte[] bytes, PreviewClip skeleton)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeleton.Rate);
        if (rate == 0) throw new InvalidDataException("S/V flame animation rate is invalid.");
        var additional = new List<PreviewMaterialTrack>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4);
            if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var vectors = source.Tables(material, 2, 256); var retained = new List<int>();
                foreach (var vector in vectors)
                {
                    if (source.Text(vector, 0) != "UVScaleOffset3") { retained.Add(vector); continue; }
                    if (source.Text(material, 0) != "fire") throw new InvalidDataException("S/V displacement animation has an undeclared material.");
                    var channels = source.Table(vector, 1);
                    var keys = Enumerable.Range(0, 4).Select(c => source.Table(channels, c) is var channel && channel != 0
                        ? source.Tables(channel, 0, 18000).Select(key =>
                    {
                        float Value(int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
                        return new PreviewKey(Value(0) * skeleton.Rate / rate, [Value(1)]);
                    }).ToArray() : []).ToArray();
                    additional.Add(new("fire", "UVScaleOffset3", keys));
                }
                if (retained.Count == vectors.Length) continue;
                var slots = writer.Vector(material, 2, retained.Count); var copy = writer.Append(bytes);
                for (var i = 0; i < slots.Length; i++) writer.Pointer(slots[i], copy + retained[i]);
            }
        }
        var warnings = new List<string>();
        var clip = PreviewMaterialAnimation.Read(new(writer.Finish(source.Root)), skeleton, warnings, true);
        if (warnings.Count > 0) throw new InvalidDataException("S/V flame animation needs another material or morph adapter.");
        var materials = clip.Materials.Concat(additional).ToArray();
        if (materials.GroupBy(t => (t.Material, t.Parameter)).Any(g => g.Count() != 1))
            throw new InvalidDataException("S/V flame animation identity is ambiguous.");
        return clip with { Materials = materials };
    }

    public static byte[] Apply(byte[] nativeAnimation, IReadOnlyList<PreviewMaterialTrack> sourceTracks, PreviewClip? globalLoop = null)
    {
        var source = new ModelBuffer(nativeAnimation); var writer = new ModelTableBuilder(nativeAnimation);
        var info = source.Table(source.Root, 0);
        var frames = globalLoop?.Frames ?? checked((int)source.Value(info, 1));
        if (frames is < 1 or > 18000) throw new InvalidDataException("Native flame timing exceeds its limit.");
        if (globalLoop is not null)
            writer.Reference(source.Root, 0, writer.Table(3, new Dictionary<int, byte[]>
            { [0] = ModelTableBuilder.Integer(1), [1] = ModelTableBuilder.Integer(frames), [2] = ModelTableBuilder.Integer(globalLoop.Rate) }));
        var material = source.Table(source.Root, 2); var existing = source.Tables(material, 0, 128);
        if (existing.Any(t => source.Text(t, 0) is "fire" or "fire_mask")) throw new InvalidDataException("Native flame animation is already bound.");
        var tracks = sourceTracks.Where(t => t.Material == "fire").ToArray();
        if (tracks.Length == 0) return writer.Finish(source.Root);
        var slots = writer.Vector(material, 0, existing.Length + 1); var copy = writer.Append(nativeAnimation);
        for (var i = 0; i < existing.Length; i++) writer.Pointer(slots[i], copy + existing[i]);
        var target = writer.Table(4); writer.Pointer(slots[^1], target); writer.Text(target, 0, "fire");
        writer.Vector(target, 1, 0); writer.Vector(target, 3, 0);
        var values = new List<(string Name, PreviewKey[] Keys)>();
        foreach (var track in tracks)
        {
            var prefix = track.Parameter switch { "UVScaleOffset" => "Blend0", "UVScaleOffset3" => "Blend1", _ => throw new InvalidDataException("S/V flame channel has no native mapping.") };
            if (track.Channels.Length != 4) throw new InvalidDataException("S/V flame UV channel count is invalid.");
            string[] suffixes = ["UVScaleU", "UVScaleV", "UVTranslateU", "UVTranslateV"];
            for (var c = 0; c < 4; c++)
            {
                var keys = track.Channels[c];
                if (keys.Length == 0) continue;
                if (keys.Length > 18000 || keys.Any(k => k.Value.Length != 1 || !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]))
                    || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame)) throw new InvalidDataException("S/V flame UV samples are invalid.");
                values.Add((prefix + suffixes[c], keys));
            }
        }
        if (values.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count() != values.Count) throw new InvalidDataException("S/V flame UV channels are duplicated.");
        var scalars = writer.Vector(target, 2, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var (name, keys) = values[i]; var constant = keys.All(k => k.Value[0] == keys[0].Value[0]);
            var scalar = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
            writer.Pointer(scalars[i], scalar); writer.Text(scalar, 0, name);
            var curve = writer.Table(1); writer.Reference(scalar, 2, curve);
            if (constant) writer.Single(writer.Field(curve, 0), keys[0].Value[0]);
            else writer.Blob(curve, 0, ModelTableBuilder.Floats(Enumerable.Range(0, frames).Select(f => Sample(keys, f)).ToArray()), frames);
        }
        return writer.Finish(source.Root);
    }

    public static byte[] Controller(byte[] controller)
    {
        var source = new ModelBuffer(controller); var writer = new ModelTableBuilder(controller);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            var masks = source.Table(source.Table(layer, 2), 1);
            foreach (var material in source.Tables(masks, 1, 128))
            {
                if (source.Text(material, 0) != "fire") continue;
                var floats = source.Table(material, 2);
                var entries = source.Tables(floats, 1, 256);
                // Global and ordinary motion layers accept the native flame channels.
                // Facial layers keep their original exclusion behavior.
                var layerName = source.Text(layer, 1);
                if (layerName is "eye01" or "mouth01") continue;
                var names = entries.Select(t => source.Text(t, 0)!).Concat(new[] { "Blend0", "Blend1" }
                    .SelectMany(p => new[] { "UVScaleU", "UVScaleV", "UVTranslateU", "UVTranslateV" }.Select(s => p + s))).Distinct(StringComparer.Ordinal).ToArray();
                var flag = source.Field(floats, 0);
                var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [flag == 0 ? (byte)0 : source.U8(flag)] });
                writer.Reference(material, 2, replacement);
                var slots = writer.Vector(replacement, 1, names.Length);
                for (var i = 0; i < names.Length; i++) { var item = writer.Table(1); writer.Pointer(slots[i], item); writer.Text(item, 0, names[i]); }
            }
        }
        return writer.Finish(source.Root);
    }

    private static float Sample(PreviewKey[] keys, int frame)
    {
        if (frame <= keys[0].Frame) return keys[0].Value[0];
        for (var i = 1; i < keys.Length; i++) if (frame < keys[i].Frame)
            return keys[i - 1].Value[0] + (keys[i].Value[0] - keys[i - 1].Value[0]) * (frame - keys[i - 1].Frame) / (keys[i].Frame - keys[i - 1].Frame);
        return keys[^1].Value[0];
    }
}
