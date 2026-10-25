// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    /// <summary>Transfers local motion deltas while retaining the destination rig's proportions.</summary>
    public static byte[] Retarget(PreviewClip clip, PreviewBone[] sourceBones, PreviewBone[] targetBones,
        IReadOnlyDictionary<string, string> boneMap)
    {
        if (clip.Frames is < 1 or > 18000 || targetBones.Length is < 1 or > 255)
            throw new InvalidDataException("Retarget animation exceeds its budget.");
        var source = sourceBones.Select((b, i) => (b.Name, Index: i)).ToDictionary(p => p.Name, p => p.Index, StringComparer.Ordinal);
        var target = targetBones.ToDictionary(b => b.Name, StringComparer.Ordinal);
        if (boneMap.Any(p => !target.ContainsKey(p.Key) || !source.ContainsKey(p.Value)))
            throw new InvalidDataException("Retarget mapping contains an unavailable joint.");
        var tracks = clip.Tracks.ToDictionary(t => t.Bone);
        Matrix4x4[] World(int? frame)
        {
            var world = new Matrix4x4[sourceBones.Length];
            for (var i = 0; i < sourceBones.Length; i++)
            {
                var bone = sourceBones[i]; var track = frame is null ? null : tracks.GetValueOrDefault(i);
                var position = track is null ? bone.Translation : Sample(track.Translation, frame!.Value, bone.Translation, false);
                var rotation = track is null ? bone.Rotation : Sample(track.Rotation, frame!.Value, bone.Rotation, true);
                world[i] = Matrix4x4.CreateFromQuaternion(Rotation(rotation)) * Matrix4x4.CreateTranslation(Vector(position))
                    * (bone.Parent < 0 ? Matrix4x4.Identity : world[bone.Parent]);
            }
            return world;
        }
        var rest = World(null);
        var poses = Enumerable.Range(0, clip.Frames).Select(f => World(f)).ToArray();
        var writer = new ModelTableBuilder();
        var root = writer.Table(5, absent: new HashSet<int> { 3, 4 });
        var info = writer.Table(3, new Dictionary<int, byte[]>
        {
            [0] = ModelTableBuilder.Integer(clip.Loop ? 1 : 0),
            [1] = ModelTableBuilder.Integer(clip.Frames), [2] = ModelTableBuilder.Integer(clip.Rate),
        });
        writer.Reference(root, 0, info);
        var skeleton = writer.Table(2, absent: new HashSet<int> { 1 }); writer.Reference(root, 1, skeleton);
        var slots = writer.Vector(skeleton, 0, targetBones.Length);
        for (var i = 0; i < targetBones.Length; i++)
        {
            var bone = targetBones[i]; var node = writer.Table(7); writer.Pointer(slots[i], node); writer.Text(node, 0, bone.Name);
            void Curve(int field, byte[] values, int count)
            {
                var stride = values.Length / count;
                if (count > 1 && Enumerable.Range(1, count - 1).All(frame => values.AsSpan(frame * stride, stride).SequenceEqual(values.AsSpan(0, stride))))
                { values = values.AsSpan(0, stride).ToArray(); count = 1; }
                writer.Int32(writer.Field(node, field), count == 1 ? 1 : 2);
                var curve = writer.Table(1, sizes: new Dictionary<int, int> { [0] = count == 1 ? values.Length : 4 });
                writer.Reference(node, field + 1, curve);
                if (count == 1) writer.Write(writer.Field(curve, 0), values); else writer.Blob(curve, 0, values, count);
            }
            if (!boneMap.TryGetValue(bone.Name, out var name))
            {
                Curve(1, ModelTableBuilder.Floats(bone.Scale), 1);
                Curve(3, PackRotation(bone.Rotation), 1); Curve(5, ModelTableBuilder.Floats(bone.Translation), 1); continue;
            }
            var original = sourceBones[source[name]];
            var track = tracks.GetValueOrDefault(source[name]);
            var targetParent = bone.Parent < 0 ? null : targetBones[bone.Parent].Name;
            var parentIndex = targetParent is null ? -1 : source[boneMap[targetParent]];
            if (parentIndex >= 0)
            {
                var at = original.Parent;
                while (at >= 0 && at != parentIndex) at = sourceBones[at].Parent;
                if (at < 0) throw new InvalidDataException("Retarget mapping changes joint ancestry.");
            }
            Matrix4x4 Relative(Matrix4x4[] world) => world[source[name]] * (parentIndex < 0 ? Matrix4x4.Identity : Inverse(world[parentIndex]));
            var originalRest = Relative(rest);
            var correction = Matrix4x4.CreateFromQuaternion(Rotation(bone.Rotation)) * Inverse(RotationMatrix(originalRest));
            var length = originalRest.Translation.Length();
            var ratio = length < .00001f ? 1 : Vector(bone.Translation).Length() / length;
            if (!float.IsFinite(ratio) || ratio > 10) throw new InvalidDataException("Retarget translation scale is invalid.");
            var rotations = new List<byte>(); var translations = new List<byte>(); var scales = new List<byte>();
            for (var frame = 0; frame < clip.Frames; frame++)
            {
                var animatedScale = track is null ? original.Scale : Sample(track.Scale, frame, original.Scale, false);
                var scale = animatedScale.Select((v, c) => bone.Scale[c] * v / original.Scale[c]).ToArray();
                if (scale.Any(v => !float.IsFinite(v) || v < 0 || v > 10))
                    throw new InvalidDataException("Retarget scale is invalid.");
                scales.AddRange(ModelTableBuilder.Floats(scale));
                var originalPose = Relative(poses[frame]);
                var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(correction * RotationMatrix(originalPose)));
                rotations.AddRange(PackRotation([rotation.X, rotation.Y, rotation.Z, rotation.W]));
                var delta = originalPose.Translation - originalRest.Translation;
                var position = Vector(bone.Translation) + Vector3.TransformNormal(delta, correction) * ratio;
                translations.AddRange(ModelTableBuilder.Floats([position.X, position.Y, position.Z]));
            }
            Curve(1, scales.ToArray(), clip.Frames); Curve(3, rotations.ToArray(), clip.Frames); Curve(5, translations.ToArray(), clip.Frames);
        }
        var materials = writer.Table(1); writer.Reference(root, 2, materials); writer.Vector(materials, 0, 0);
        var result = writer.Finish(root);
        _ = PreviewRigReader.Animation(new(result), "retarget", targetBones);
        return result;

        static Vector3 Vector(float[] v) => new(v[0], v[1], v[2]);
        static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var inverse) ? inverse : throw new InvalidDataException("Retarget transform is singular.");
        static Matrix4x4 RotationMatrix(Matrix4x4 m) => Matrix4x4.Decompose(m, out _, out var q, out _)
            ? Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q)) : throw new InvalidDataException("Retarget transform cannot be decomposed.");
        static Quaternion Rotation(float[] q) => Quaternion.Normalize(new(q[0], q[1], q[2], q[3]));
        static float[] Sample(PreviewKey[] keys, float frame, float[] fallback, bool rotation)
        {
            if (keys.Length == 0) return fallback;
            if (frame <= keys[0].Frame) return keys[0].Value;
            for (var i = 1; i < keys.Length; i++)
            {
                if (frame > keys[i].Frame) continue;
                var a = keys[i - 1]; var b = keys[i]; var t = (frame - a.Frame) / (b.Frame - a.Frame);
                if (rotation) { var q = Quaternion.Slerp(Rotation(a.Value), Rotation(b.Value), t); return [q.X, q.Y, q.Z, q.W]; }
                return a.Value.Select((v, c) => v + (b.Value[c] - v) * t).ToArray();
            }
            return keys[^1].Value;
        }
    }
}
