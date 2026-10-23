// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Tools.Application;

internal static class SwShRestorationFraming
{
    internal static void SetGiantVerticalOffset(byte[] giantConfiguration, float offset)
    {
        var giant = new ModelBuffer(giantConfiguration);
        var offsetAt = giant.Field(giant.Root, 8);
        if (!float.IsFinite(offset) || (offsetAt == 0 && offset != 0))
            throw new InvalidDataException("Giant vertical placement configuration is invalid.");
        // Floating native actors can carry an offset that is inappropriate for the imported rig.
        if (offsetAt != 0)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(offsetAt, 4), offset);
    }

    internal static void SetGiantScale(byte[] giantConfiguration, float scale)
    {
        var giant = new ModelBuffer(giantConfiguration);
        var scaleAt = giant.Field(giant.Root, 4);
        if (scaleAt == 0 || !float.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException("Giant sizing configuration is invalid.");
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(scaleAt, 4), scale);
    }

    internal static void MatchGiantHeight(byte[] giantConfiguration, byte[] nativeConfiguration, float battleHeight)
    {
        var giant = new ModelBuffer(giantConfiguration); var native = new ModelBuffer(nativeConfiguration);
        var scaleAt = giant.Field(giant.Root, 4); var heightAt = native.Field(native.Root, 9);
        if (scaleAt == 0 || heightAt == 0)
            throw new InvalidDataException("Native giant sizing configuration is incomplete.");
        var scale = giant.Float(scaleAt); var nativeHeight = native.Float(heightAt);
        if (!float.IsFinite(scale) || !float.IsFinite(nativeHeight) || !float.IsFinite(battleHeight)
            || scale <= 0 || nativeHeight <= 0 || battleHeight <= 0)
            throw new InvalidDataException("Giant sizing configuration is invalid.");
        // Giant scale multiplies ordinary battle height. Preserve the native camera's
        // actor height without changing the imported model's ordinary dimensions.
        var fitted = scale * nativeHeight / battleHeight;
        if (!float.IsFinite(fitted) || fitted <= 0)
            throw new InvalidDataException("Converted giant sizing is invalid.");
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(scaleAt, 4), fitted);
    }

    internal static SwShConvertedGeometry IdleBounds(IReadOnlyDictionary<string, byte[]> members, string modelFolder,
        string animationFolder, string stem, SwShConvertedGeometry geometry)
    {
        // SwSh scales the actor using its posed idle height, including in the party screen.
        var scene = new SwShPreviewReader(path => members[modelFolder + Path.GetFileName(path)]).Load(stem + ".gfbmdl");
        var bones = scene.Rig.Bones;
        var clip = PreviewRigReader.Animation(new(members[animationFolder + stem + "_ba10_waitA01.gfbanm"]), stem, bones);
        var world = new Matrix4x4[bones.Length]; var skin = new Matrix4x4[bones.Length];
        var scales = new Vector3[bones.Length];
        static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);
        static float[] First(PreviewKey[]? keys, float[] fallback) => keys is { Length: > 0 } ? keys[0].Value : fallback;
        for (var i = 0; i < bones.Length; i++)
        {
            var bone = bones[i]; var track = clip.Tracks.SingleOrDefault(t => t.Bone == i);
            var scale = First(track?.Scale, bone.Scale); var rotation = First(track?.Rotation, bone.Rotation);
            if (scale.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Source idle scale is invalid.");
            scales[i] = Vector(scale);
            var local = Matrix4x4.CreateScale(scales[i])
                * Matrix4x4.CreateFromQuaternion(new(rotation[0], rotation[1], rotation[2], rotation[3]));
            if (bone.CompensateScale && bone.Parent >= 0)
            {
                float Inverse(float value) => MathF.Abs(value) > .000001f ? 1 / value : 0;
                var parent = scales[bone.Parent];
                local *= Matrix4x4.CreateScale(Inverse(parent.X), Inverse(parent.Y), Inverse(parent.Z));
            }
            local *= Matrix4x4.CreateTranslation(Vector(First(track?.Translation, bone.Translation)));
            world[i] = bone.Parent < 0 ? local : local * world[bone.Parent];
            var b = bone.InverseBind;
            var inverse = new Matrix4x4(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7],
                b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
            skin[i] = inverse * world[i];
        }
        var minimum = new Vector3(float.PositiveInfinity); var maximum = new Vector3(float.NegativeInfinity);
        foreach (var primitive in scene.Primitives)
            for (var vertex = 0; vertex < primitive.Vertices.Length; vertex += 16)
            {
                var values = primitive.Vertices; var position = new Vector3(values[vertex], values[vertex + 1], values[vertex + 2]);
                var posed = Vector3.Zero;
                for (var influence = 0; influence < 4; influence++)
                    if (values[vertex + 12 + influence] > 0)
                        posed += Vector3.Transform(position, skin[(int)values[vertex + 8 + influence]]) * values[vertex + 12 + influence];
                minimum = Vector3.Min(minimum, posed); maximum = Vector3.Max(maximum, posed);
            }
        if (!float.IsFinite(minimum.X + minimum.Y + minimum.Z + maximum.X + maximum.Y + maximum.Z) || maximum.Y <= minimum.Y)
            throw new InvalidDataException("Source idle framing is invalid.");
        return geometry with { Minimum = minimum, Maximum = maximum };
    }
}
