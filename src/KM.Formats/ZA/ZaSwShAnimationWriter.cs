// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

/// <summary>Adapts Z-A facial material channels and native expression timing independently of S/V.</summary>
public static class ZaSwShAnimationWriter
{
    public static byte[] Controller(byte[] nativeController, string donorStem, string targetStem,
        IReadOnlyList<string> materialNames, IReadOnlyDictionary<string, string[]> facialBones)
    {
        var bytes = SwShAnimationWriter.Controller(nativeController, donorStem, targetStem, materialNames, facialBones);
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            var masks = source.Table(source.Table(layer, 2), 1);
            foreach (var material in source.Tables(masks, 1, 128))
            {
                if (!(source.Text(material, 0) ?? "").Contains("eye", StringComparison.Ordinal)) continue;
                var floats = source.Table(material, 2);
                var names = source.Tables(floats, 1, 256).Select(t => source.Text(t, 0)!).ToArray();
                if (!names.Contains("ColorUVTranslateU", StringComparer.Ordinal)) continue;
                names = names.Concat(new[] { "ColorUVScaleU", "ColorUVScaleV", "ConstantColorVal" }).Distinct(StringComparer.Ordinal).ToArray();
                var slots = writer.Vector(floats, 1, names.Length);
                for (var i = 0; i < names.Length; i++) { var name = writer.Table(1); writer.Pointer(slots[i], name); writer.Text(name, 0, names[i]); }
            }
        }
        return writer.Finish(source.Root);
    }

    public static PreviewMaterialTrack[] ReadMaterials(byte[] bytes, PreviewClip skeletal)
    {
        var source = new ModelBuffer(bytes); var sanitized = new ModelTableBuilder(bytes);
        var config = source.Table(source.Root, 0);
        var rate = source.Value(config, 2, (uint)skeletal.Rate);
        if (rate == 0) throw new InvalidDataException("Z-A material animation rate is invalid.");
        var ratio = skeletal.Rate / (float)rate;
        var scalars = new List<PreviewMaterialTrack>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4);
            if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var name = source.Text(material, 0) ?? throw new InvalidDataException("Z-A material track is unnamed.");
                var entries = source.Tables(material, 1, 256);
                if (entries.Length == 0) continue;
                foreach (var entry in entries)
                {
                    var parameter = source.Text(entry, 0);
                    if (parameter != "LayerMaskScale5") throw new InvalidDataException($"Z-A scalar material animation needs another adapter: {parameter}");
                    var curve = source.Table(entry, 1);
                    var keys = source.Tables(curve, 0, 18000).Select(key =>
                    {
                        float Value(int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
                        return new PreviewKey(Value(0) * ratio, [Value(1)]);
                    }).ToArray();
                    Validate(keys); scalars.Add(new(name, parameter, [keys]));
                }
                sanitized.Vector(material, 1, 0);
            }
        }
        var warnings = new List<string>();
        var decoded = PreviewMaterialAnimation.Read(new(sanitized.Finish(source.Root)), skeletal, warnings, true);
        if (warnings.Count != 0 || decoded.Visibility.Any(v => v.Keys.Any(k => k.Value.Any(value => value == 0))))
            throw new InvalidDataException("Z-A visibility or morph animation needs another adapter.");
        if (decoded.Materials.Any(t => t.Parameter != "UVScaleOffset1"))
            throw new InvalidDataException("Z-A vector material animation needs another adapter.");
        var result = decoded.Materials.Concat(scalars).ToArray();
        if (result.GroupBy(t => (t.Material, t.Parameter)).Any(g => g.Count() != 1)) throw new InvalidDataException("Z-A material animation identity is ambiguous.");
        return result;
    }

    public static byte[] Convert(byte[] skeleton, PreviewMaterialTrack[] materials, PreviewBone[] bones,
        IReadOnlyDictionary<string, string> aliases, bool completePose, SwShExpressionCurve expression = SwShExpressionCurve.None,
        bool emptyExpression = false)
    {
        var bytes = SwShAnimationWriter.Convert(skeleton, [], bones, aliases, completePose, expression);
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var frames = checked((int)source.Value(source.Table(source.Root, 0), 1));
        if (frames is < 1 or > 18000) throw new InvalidDataException("Converted Z-A animation timing is invalid.");
        if (expression != SwShExpressionCurve.None)
        {
            var skeletal = source.Table(source.Root, 1);
            var tracks = source.Tables(skeletal, 0, 4096).Where(t => !emptyExpression && expression == SwShExpressionCurve.Eyes
                && (source.Text(t, 0) ?? "").Contains("eyelid", StringComparison.OrdinalIgnoreCase)).ToArray();
            var slots = writer.Vector(skeletal, 0, tracks.Length);
            var copy = writer.Append(bytes);
            for (var i = 0; i < tracks.Length; i++) writer.Pointer(slots[i], copy + tracks[i]);
        }
        var nativeMaterials = writer.Table(1); writer.Reference(source.Root, 2, nativeMaterials);
        var groups = (emptyExpression ? [] : materials).GroupBy(t => t.Material, StringComparer.Ordinal).ToArray();
        var materialSlots = writer.Vector(nativeMaterials, 0, groups.Length);
        for (var i = 0; i < groups.Length; i++)
        {
            var native = new List<(string Name, float[] Values)>();
            float Frame(int frame) => expression == SwShExpressionCurve.None ? frame : EyeSample(frame);
            foreach (var track in groups[i])
            {
                if (track.Parameter == "LayerMaskScale5")
                {
                    if (track.Channels.Length != 1) throw new InvalidDataException("Z-A eye override has invalid channels.");
                    Validate(track.Channels[0]);
                    if (track.Channels[0].Any(k => k.Value[0] is < 0 or > 1)) throw new InvalidDataException("Z-A eye override is outside its range.");
                    native.Add(("ConstantColorVal", Enumerable.Range(0, frames).Select(f => 1 - Sample(track.Channels[0], Frame(f))).ToArray()));
                }
                else if (track.Parameter == "UVScaleOffset1")
                {
                    if (track.Channels.Length != 4) throw new InvalidDataException("Z-A highlight UV animation has invalid channels.");
                    foreach (var channel in track.Channels) Validate(channel);
                    string[] names = ["ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV"];
                    for (var c = 0; c < 4; c++)
                    {
                        var component = c;
                        native.Add((names[c], Enumerable.Range(0, frames).Select(f => component == 3
                            ? 1 - Sample(track.Channels[1], Frame(f)) - Sample(track.Channels[3], Frame(f))
                            : Sample(track.Channels[component], Frame(f))).ToArray()));
                    }
                }
                else throw new InvalidDataException("Z-A material animation has no native mapping.");
            }
            var material = writer.Table(4); writer.Pointer(materialSlots[i], material); writer.Text(material, 0, groups[i].Key);
            writer.Vector(material, 1, 0); writer.Vector(material, 3, 0);
            var scalarSlots = writer.Vector(material, 2, native.Count);
            for (var j = 0; j < native.Count; j++)
            {
                var (name, values) = native[j]; var constant = values.All(v => v == values[0]);
                var scalar = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                writer.Pointer(scalarSlots[j], scalar); writer.Text(scalar, 0, name);
                var curve = writer.Table(1); writer.Reference(scalar, 2, curve);
                if (constant) writer.Single(writer.Field(curve, 0), values[0]);
                else writer.Blob(curve, 0, ModelTableBuilder.Floats(values), frames);
            }
        }
        return writer.Finish(source.Root);
    }

    private static void Validate(PreviewKey[] keys)
    {
        if (keys.Length is < 1 or > 18000 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || k.Value.Length != 1 || !float.IsFinite(k.Value[0]))
            || keys.Zip(keys.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame)) throw new InvalidDataException("Z-A material keys are invalid.");
    }
    private static float Sample(PreviewKey[] keys, float frame)
    {
        if (frame <= keys[0].Frame) return keys[0].Value[0];
        for (var i = 1; i < keys.Length; i++) if (frame < keys[i].Frame)
            return keys[i - 1].Value[0] + (keys[i].Value[0] - keys[i - 1].Value[0]) * (frame - keys[i - 1].Frame) / (keys[i].Frame - keys[i - 1].Frame);
        return keys[^1].Value[0];
    }
    private static float EyeSample(int frame)
    {
        int[] frames = [0, 10, 20, 30, 40, 50, 60, 80]; int[] samples = [0, 5, 10, 0, 5, 10, 0, 0];
        for (var i = 1; i < frames.Length; i++) if (frame < frames[i])
            return samples[i - 1] + (samples[i] - samples[i - 1]) * (frame - frames[i - 1]) / (float)(frames[i] - frames[i - 1]);
        return 0;
    }
}
