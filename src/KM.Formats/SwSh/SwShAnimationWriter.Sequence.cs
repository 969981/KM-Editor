// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    /// <summary>Retains all authored segments of a skeletal battle action.</summary>
    public static byte[] SkeletalSequence(IReadOnlyList<byte[]> segments, PreviewBone[] bones)
    {
        if (segments.Count is < 2 or > 8 || bones.Length is < 1 or > 255)
            throw new InvalidDataException("Battle sequence inputs are invalid.");
        var sources = segments.Select(bytes => new ModelBuffer(bytes)).ToArray();
        var clips = sources.Select(source =>
        {
            var warnings = new List<string>();
            var clip = SwShPreviewAnimation.Read(source, PreviewRigReader.Animation(source, "segment", bones), warnings);
            if (warnings.Count > 0 || clip.Materials.Length != 0 || source.Table(source.Root, 4) != 0)
                throw new InvalidDataException("Battle sequence material channels need another adapter.");
            return clip;
        }).ToArray();
        var first = clips[0];
        if (clips.Any(clip => clip.Rate != first.Rate) || first.Loop || clips[^1].Loop)
            throw new InvalidDataException("Battle sequence timing is incompatible.");
        if (first.Visibility.Any(track => track.Keys.Length == 0 || track.Keys[0].Value.Length != 1))
            throw new InvalidDataException("Battle sequence visibility is incomplete.");
        var visibility = first.Visibility.ToDictionary(track => track.Name, track => track.Keys[0].Value[0], StringComparer.Ordinal);
        foreach (var clip in clips)
            if (clip.Visibility.Length != visibility.Count || clip.Visibility.Any(track => !visibility.TryGetValue(track.Name, out var value)
                || track.Keys.Length == 0 || track.Keys.Any(key => key.Value.Length != 1 || key.Value[0] != value)))
                throw new InvalidDataException("Battle sequence visibility needs another adapter.");
        var frames = clips.Sum(clip => clip.Frames);
        if (frames > 18000) throw new InvalidDataException("Battle sequence exceeds its frame budget.");
        var writer = new ModelTableBuilder(segments[0]);
        writer.Int32(sources[0].Field(sources[0].Table(sources[0].Root, 0), 1), frames);
        var skeleton = writer.Table(2, absent: sources[0].Table(sources[0].Table(sources[0].Root, 1), 1) == 0 ? new HashSet<int> { 1 } : null);
        writer.Reference(sources[0].Root, 1, skeleton);
        var slots = writer.Vector(skeleton, 0, bones.Length);
        var targets = slots.Select(_ => writer.Table(7)).ToArray();
        for (var i = 0; i < slots.Length; i++) writer.Pointer(slots[i], targets[i]);
        var copy = writer.Append(segments[0]);
        var initial = sources[0].Table(sources[0].Table(sources[0].Root, 1), 1);
        if (initial != 0) writer.Reference(skeleton, 1, copy + initial);
        var tracks = sources.Select(source => source.Tables(source.Table(source.Root, 1), 0, 4096)
            .ToDictionary(track => source.Text(track, 0) ?? throw new InvalidDataException("Battle sequence track is unnamed."), StringComparer.Ordinal)).ToArray();
        for (var bone = 0; bone < bones.Length; bone++)
        {
            writer.Text(targets[bone], 0, bones[bone].Name);
            foreach (var field in new[] { 1, 3, 5 })
            {
                var stride = field == 3 ? 6 : 12; var values = new List<byte>(); var indices = new List<ushort>(); var offset = 0;
                for (var segment = 0; segment < sources.Length; segment++)
                {
                    var source = sources[segment]; var track = tracks[segment].GetValueOrDefault(bones[bone].Name);
                    var at = track == 0 ? 0 : source.Field(track, field); var kind = at == 0 ? 0 : source.U8(at);
                    if (kind > 4) throw new InvalidDataException("Battle sequence curve encoding is unsupported.");
                    if (kind == 0)
                    {
                        var fallback = field == 1 ? bones[bone].Scale : field == 3 ? bones[bone].Rotation : bones[bone].Translation;
                        indices.Add(checked((ushort)offset)); values.AddRange(field == 3 ? PackRotation(fallback) : ModelTableBuilder.Floats(fallback));
                    }
                    else
                    {
                        var curve = source.Table(track, field + 1);
                        var (start, count) = kind == 1 ? (source.Field(curve, 0), 1) : source.Vector(curve, kind == 2 ? 0 : 1, stride, 18000);
                        var (keys, keyCount) = kind >= 3 ? source.Vector(curve, 0, kind == 3 ? 2 : 1, 18000) : (0, count);
                        if (count < 1 || count != keyCount) throw new InvalidDataException("Battle sequence keys are incomplete.");
                        for (var i = 0; i < count; i++)
                        {
                            var frame = kind == 1 ? 0 : kind == 2 ? i : kind == 3 ? source.U16(keys + i * 2) : source.U8(keys + i);
                            indices.Add(checked((ushort)(offset + frame))); values.AddRange(source.Slice(start + i * stride, stride).ToArray());
                        }
                    }
                    var end = offset + clips[segment].Frames - 1;
                    if (indices[^1] < end) { indices.Add(checked((ushort)end)); values.AddRange(values.TakeLast(stride).ToArray()); }
                    offset = end + 1;
                }
                writer.Int32(writer.Field(targets[bone], field), 3);
                var replacement = writer.Table(2); writer.Reference(targets[bone], field + 1, replacement);
                writer.Blob(replacement, 0, indices.SelectMany(BitConverter.GetBytes).ToArray(), indices.Count);
                writer.Blob(replacement, 1, values.ToArray(), indices.Count);
            }
        }
        var result = writer.Finish(sources[0].Root);
        var verified = PreviewRigReader.Animation(new(result), "sequence", bones);
        if (verified.Frames != frames || verified.Loop) throw new InvalidDataException("Battle sequence failed timing verification.");
        return result;
    }
}
