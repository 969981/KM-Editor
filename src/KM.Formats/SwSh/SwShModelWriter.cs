// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public sealed record SwShMaterialBinding(string Name, int NativeMaterial, IReadOnlyDictionary<string, string> Textures);
public sealed record SwShEffectLocator(string Name, string Parent, Vector3 Translation)
{
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
}
public sealed record SwShConvertedGeometry(byte[] Model, PreviewBone[] Bones, Vector3 Minimum, Vector3 Maximum);

/// <summary>Encodes skinned geometry against an explicitly selected native shader contract.</summary>
public static class SwShModelWriter
{
    public static SwShConvertedGeometry Convert(PreviewScene scene, byte[] nativeModel,
        IReadOnlyList<SwShMaterialBinding> bindings, IReadOnlyDictionary<string, string> boneAliases,
        IReadOnlyList<SwShEffectLocator> locators, float scale = 100, CancellationToken cancellationToken = default,
        bool preserveMeshNames = false)
    {
        if (!float.IsFinite(scale) || scale <= 0 || scene.Primitives.Count is < 1 or > 128
            || scene.Rig.Bones.Length is < 1 or > 255 || bindings.Count != scene.Primitives.Count)
            throw new InvalidDataException("Model conversion inputs are outside the supported limits.");
        var native = new ModelBuffer(nativeModel);
        var materials = native.Tables(native.Root, 6, 128);
        var names = bindings.SelectMany(b => b.Textures.Values).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var bones = scene.Rig.Bones.Select(b => b with
        {
            Name = boneAliases.GetValueOrDefault(b.Name, b.Name),
            Translation = b.Translation.Select(v => v * scale).ToArray(),
        }).ToList();
        if (bones.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != bones.Count)
            throw new InvalidDataException("Converted bone names are ambiguous.");
        foreach (var locator in locators)
        {
            var parent = bones.FindIndex(b => b.Name == locator.Parent);
            if (parent < 0 || bones.Any(b => b.Name == locator.Name)) throw new InvalidDataException("Effect locator parent or identity is invalid.");
            var rotation = locator.Rotation;
            if (!float.IsFinite(rotation.LengthSquared()) || MathF.Abs(rotation.LengthSquared() - 1) > .001f
                || !float.IsFinite(locator.Translation.X + locator.Translation.Y + locator.Translation.Z))
                throw new InvalidDataException("Effect locator transform is invalid.");
            bones.Add(new(locator.Name, parent, -1, false, [1, 1, 1], [rotation.X, rotation.Y, rotation.Z, rotation.W],
                [locator.Translation.X, locator.Translation.Y, locator.Translation.Z], []));
        }
        var meshBones = new Dictionary<string, int>(StringComparer.Ordinal);
        if (preserveMeshNames)
            foreach (var name in scene.Primitives.Select(p => p.Name).Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(name) || bones.Any(b => b.Name == name))
                    throw new InvalidDataException("Converted mesh node identity is invalid.");
                meshBones.Add(name, bones.Count);
                bones.Add(new(name, 0, -1, false, [1, 1, 1], [0, 0, 0, 1], [0, 0, 0], []));
            }
        if (bones.Count > 255) throw new InvalidDataException("Converted skeleton exceeds the native joint limit.");
        var joints = scene.Rig.Bones.Select((bone, index) => (bone.Joint, Index: index)).Where(b => b.Joint >= 0)
            .ToDictionary(b => b.Joint, b => b.Index);
        var bounds = scene.Primitives.Select(p => Bounds(p, scale)).ToArray();
        var minimum = bounds.Select(b => b.Min).Aggregate(Vector3.Min);
        var maximum = bounds.Select(b => b.Max).Aggregate(Vector3.Max);
        var writer = new ModelTableBuilder();
        var root = writer.Table(12, new Dictionary<int, byte[]>
        {
            [0] = native.Slice(native.Field(native.Root, 0), 4).ToArray(),
            [1] = BoundsBytes(minimum, maximum),
        }, new Dictionary<int, int> { [1] = 24 });
        Strings(writer, root, 2, names);
        foreach (var field in new[] { 3, 5 })
        {
            var (start, count) = native.Vector(native.Root, field, 4, 256);
            Strings(writer, root, field, Enumerable.Range(0, count).Select(i => native.StringAt(start + i * 4)).ToArray());
        }
        Strings(writer, root, 4, []);
        var slots = writer.Vector(root, 6, bindings.Count);
        for (var i = 0; i < slots.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = bindings[i];
            if ((uint)binding.NativeMaterial >= materials.Length || binding.Name != scene.Primitives[i].Material.Name)
                throw new InvalidDataException("Native material binding does not match its mesh.");
            writer.Pointer(slots[i], Material(writer, native, materials[binding.NativeMaterial], binding, names));
        }
        slots = writer.Vector(root, 7, scene.Primitives.Count);
        for (var i = 0; i < slots.Length; i++)
            writer.Pointer(slots[i], writer.Table(4, new Dictionary<int, byte[]>
            {
                [0] = ModelTableBuilder.Integer(preserveMeshNames ? meshBones[scene.Primitives[i].Name] : 0), [1] = ModelTableBuilder.Integer(i),
                [2] = BoundsBytes(bounds[i].Min, bounds[i].Max), [3] = ModelTableBuilder.Integer(0),
            }, new Dictionary<int, int> { [2] = 24 }));
        slots = writer.Vector(root, 8, scene.Primitives.Count);
        for (var i = 0; i < slots.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primitive = scene.Primitives[i];
            var mesh = writer.Table(3); writer.Pointer(slots[i], mesh);
            var polygonSlot = writer.Vector(mesh, 0, 1)[0];
            var polygon = writer.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(i) });
            writer.Pointer(polygonSlot, polygon);
            var indices = new byte[primitive.Indices.Length * 2];
            for (var j = 0; j < primitive.Indices.Length; j++)
                BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(j * 2), checked((ushort)primitive.Indices[j]));
            writer.Blob(polygon, 1, indices, primitive.Indices.Length);
            (int Kind, int Format, int Count)[] attributes = [(0, 0, 3), (1, 1, 4), (2, 1, 4), (3, 0, 2), (7, 3, 4), (8, 3, 4), (11, 3, 4), (12, 8, 4)];
            var attributeSlots = writer.Vector(mesh, 1, attributes.Length);
            for (var j = 0; j < attributes.Length; j++)
                writer.Pointer(attributeSlots[j], writer.Table(3, new Dictionary<int, byte[]>
                {
                    [0] = ModelTableBuilder.Integer(attributes[j].Kind), [1] = ModelTableBuilder.Integer(attributes[j].Format),
                    [2] = ModelTableBuilder.Integer(attributes[j].Count),
                }));
            writer.Blob(mesh, 2, Vertices(primitive, joints, scale, cancellationToken));
        }
        slots = writer.Vector(root, 9, bones.Count);
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i]; var locator = i >= scene.Rig.Bones.Length;
            var meshNode = i >= scene.Rig.Bones.Length + locators.Count;
            if (bone.Parent < -1 || bone.Parent >= i || bone.Scale.Length != 3 || bone.Translation.Length != 3 || bone.Rotation.Length != 4)
                throw new InvalidDataException("Converted skeleton hierarchy is invalid.");
            var table = writer.Table(11, new Dictionary<int, byte[]>
            {
                [1] = ModelTableBuilder.Integer(meshNode ? 0 : locator ? 2 : 1), [2] = ModelTableBuilder.Integer(bone.Parent),
                [3] = ModelTableBuilder.Integer(0), [4] = ModelTableBuilder.Integer(bone.CompensateScale ? 1 : 0),
                [5] = ModelTableBuilder.Floats(bone.Scale), [6] = ModelTableBuilder.Floats(Euler(bone.Rotation)),
                [7] = ModelTableBuilder.Floats(bone.Translation), [8] = new byte[12], [9] = new byte[12],
                [10] = ModelTableBuilder.Integer(locator ? 0 : 1),
            }, Enumerable.Range(5, 5).ToDictionary(f => f, _ => 12));
            writer.Pointer(slots[i], table); writer.Text(table, 0, bone.Name);
        }
        writer.Vector(root, 10, 0); writer.Vector(root, 11, 0);
        return new(writer.Finish(root), bones.ToArray(), minimum, maximum);
    }

    public static byte[] Configuration(byte[] nativeConfiguration, int species, SwShConvertedGeometry geometry,
        IReadOnlyList<string> meshNames, float battleHeight, float fieldHeight)
    {
        if (species is < 1 or > 898 || meshNames.Count is < 1 or > 128 || battleHeight <= 0 || fieldHeight <= 0)
            throw new InvalidDataException("Converted model configuration is invalid.");
        var source = new ModelBuffer(nativeConfiguration); var writer = new ModelTableBuilder();
        var values = Enumerable.Range(0, 30).Where(i => i is not (4 or 5 or 28 or 29) && source.Field(source.Root, i) != 0)
            .ToDictionary(i => i, i => source.Slice(source.Field(source.Root, i), 4).ToArray());
        values[2] = ModelTableBuilder.Integer(species); values[6] = ModelTableBuilder.Integer(3);
        var root = writer.Table(30, values); writer.Text(root, 4, ""); writer.Text(root, 5, "");
        float[] dimensions = [geometry.Maximum.Y - geometry.Minimum.Y, battleHeight, fieldHeight,
            geometry.Minimum.X, geometry.Minimum.Y, geometry.Minimum.Z, geometry.Maximum.X, geometry.Maximum.Y, geometry.Maximum.Z];
        for (var i = 0; i < dimensions.Length; i++) writer.Single(writer.Field(root, 8 + i), dimensions[i]);
        var slots = writer.Vector(root, 28, meshNames.Count);
        for (var i = 0; i < slots.Length; i++) { var t = writer.Table(1); writer.Pointer(slots[i], t); writer.Text(t, 0, meshNames[i]); }
        var property = writer.Table(2); writer.Reference(root, 29, property); writer.Vector(property, 1, 0);
        return writer.Finish(root);
    }

    private static int Material(ModelTableBuilder writer, ModelBuffer source, int original, SwShMaterialBinding binding, string[] names)
    {
        var scalarFields = Enumerable.Range(2, 9).Concat(Enumerable.Range(15, 5)).ToArray();
        int Width(int field) => field is 3 or 4 or >= 15 ? 1 : 4;
        var missing = scalarFields.Where(f => source.Field(original, f) == 0).ToHashSet();
        var values = scalarFields.Where(f => !missing.Contains(f)).ToDictionary(f => f, f => source.Slice(source.Field(original, f), Width(f)).ToArray());
        var table = writer.Table(21, values, absent: missing);
        writer.Text(table, 0, binding.Name); writer.Text(table, 1, source.Text(original, 1) ?? throw new InvalidDataException("Native shader name is missing."));
        void Parameters(int parent, int field, int sourceTable, int sourceField, int width)
        {
            var entries = source.Tables(sourceTable, sourceField, 256); var slots = writer.Vector(parent, field, entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                var at = source.Field(entries[i], 1); var name = source.Text(entries[i], 0) ?? throw new InvalidDataException("Native material parameter is unnamed.");
                var value = at == 0 ? new byte[width] : source.Slice(at, width).ToArray();
                if (width == 4 && name is "ColorUVScaleU" or "ColorUVScaleV" or "NormalMapUVScaleU" or "NormalMapUVScaleV") value = ModelTableBuilder.Floats(1);
                if (width == 4 && name is "ColorBaseU" or "ColorBaseV" or "ColorUVTranslateU" or "ColorUVTranslateV") value = ModelTableBuilder.Floats(0);
                var item = writer.Table(2, new Dictionary<int, byte[]> { [1] = value }, new Dictionary<int, int> { [1] = Math.Max(4, width) });
                writer.Pointer(slots[i], item); writer.Text(item, 0, name);
            }
        }
        Parameters(table, 12, original, 12, 1); Parameters(table, 13, original, 13, 4); Parameters(table, 14, original, 14, 12);
        var common = writer.Table(3); writer.Reference(table, 20, common);
        var sourceCommon = source.Table(original, 20);
        Parameters(common, 0, sourceCommon, 0, 1); Parameters(common, 1, sourceCommon, 1, 4); Parameters(common, 2, sourceCommon, 2, 12);
        var textures = source.Tables(original, 11, 64); var textureSlots = writer.Vector(table, 11, textures.Length);
        for (var i = 0; i < textures.Length; i++)
        {
            var role = source.Text(textures[i], 0) ?? throw new InvalidDataException("Native texture binding is unnamed.");
            if (!binding.Textures.TryGetValue(role, out var name)) throw new InvalidDataException($"Texture binding {role} has no conversion.");
            var texture = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(Array.IndexOf(names, name)) });
            writer.Pointer(textureSlots[i], texture); writer.Text(texture, 0, role);
            var sampler = source.Table(textures[i], 2);
            var absent = Enumerable.Range(0, 9).Where(f => source.Field(sampler, f) == 0).ToHashSet();
            writer.Reference(texture, 2, writer.Table(9, Enumerable.Range(0, 9).Where(f => !absent.Contains(f))
                .ToDictionary(f => f, f => source.Slice(source.Field(sampler, f), 4).ToArray()), absent: absent));
        }
        return table;
    }

    public static (Vector3 Min, Vector3 Max) Bounds(PreviewPrimitive primitive, float scale)
    {
        var vertices = primitive.Vertices;
        if (vertices.Length == 0 || vertices.Length % 16 != 0 || vertices.Length / 16 > ushort.MaxValue
            || vertices.Any(v => !float.IsFinite(v)) || primitive.Indices.Length is < 3 or > 1_500_000
            || primitive.Indices.Length % 3 != 0 || primitive.Indices.Any(i => i >= vertices.Length / 16))
            throw new InvalidDataException("Converted geometry has invalid vertices or triangles.");
        var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < vertices.Length; i += 16)
        { var v = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]) * scale; min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        return (min, max);
    }
    private static byte[] BoundsBytes(Vector3 min, Vector3 max) => ModelTableBuilder.Floats(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    private static void Strings(ModelTableBuilder writer, int table, int field, string[] values)
    { var slots = writer.Vector(table, field, values.Length); for (var i = 0; i < slots.Length; i++) writer.Pointer(slots[i], writer.String(values[i])); }
    private static float[] Euler(float[] q)
    {
        var rotation = Quaternion.Normalize(new(q[0], q[1], q[2], q[3]));
        var m = Matrix4x4.CreateFromQuaternion(rotation);
        var y = MathF.Asin(Math.Clamp(-m.M13, -1, 1));
        return MathF.Abs(MathF.Cos(y)) > 0.000001f ? [MathF.Atan2(m.M23, m.M33), y, MathF.Atan2(m.M12, m.M11)]
            : [MathF.Atan2(-m.M32, m.M22), y, 0];
    }
    private static byte[] Vertices(PreviewPrimitive primitive, IReadOnlyDictionary<int, int> joints, float scale, CancellationToken token)
    {
        var v = primitive.Vertices; var count = v.Length / 16;
        var tangents = new Vector3[count]; var bitangents = new Vector3[count];
        Vector3 Position(int i) => new(v[i * 16], v[i * 16 + 1], v[i * 16 + 2]);
        Vector2 Uv(int i) => new(v[i * 16 + 6], v[i * 16 + 7]);
        for (var i = 0; i < primitive.Indices.Length; i += 3)
        {
            var a = (int)primitive.Indices[i]; var b = (int)primitive.Indices[i + 1]; var c = (int)primitive.Indices[i + 2];
            var e1 = Position(b) - Position(a); var e2 = Position(c) - Position(a); var u1 = Uv(b) - Uv(a); var u2 = Uv(c) - Uv(a);
            var determinant = u1.X * u2.Y - u1.Y * u2.X;
            if (MathF.Abs(determinant) <= 1e-9f) continue;
            var tangent = (e1 * u2.Y - e2 * u1.Y) / determinant; var bitangent = (e2 * u1.X - e1 * u2.X) / determinant;
            tangents[a] += tangent; tangents[b] += tangent; tangents[c] += tangent;
            bitangents[a] += bitangent; bitangents[b] += bitangent; bitangents[c] += bitangent;
        }
        using var stream = new MemoryStream(count * 52); using var output = new BinaryWriter(stream);
        void Half(float value) => output.Write(BitConverter.HalfToUInt16Bits((Half)value));
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var at = i * 16; var normal = new Vector3(v[at + 3], v[at + 4], v[at + 5]);
            if (normal.LengthSquared() < 1e-12) throw new InvalidDataException("Converted geometry contains an empty normal.");
            normal = Vector3.Normalize(normal);
            var tangent = tangents[i] - normal * Vector3.Dot(normal, tangents[i]);
            if (tangent.LengthSquared() < 1e-12) tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX);
            tangent = Vector3.Normalize(tangent);
            var position = Position(i) * scale; output.Write(position.X); output.Write(position.Y); output.Write(position.Z);
            Half(normal.X); Half(normal.Y); Half(normal.Z); Half(1);
            Half(tangent.X); Half(tangent.Y); Half(tangent.Z); Half(Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0 ? -1 : 1);
            output.Write(v[at + 6]); output.Write(v[at + 7]); output.Write(new byte[] { 127, 127, 255, 255 });
            output.Write(new[] { (byte)Math.Clamp((normal.X + 1) * 127.5f, 0, 255), (byte)Math.Clamp((normal.Y + 1) * 127.5f, 0, 255), (byte)Math.Clamp((normal.Z + 1) * 127.5f, 0, 255), (byte)255 });
            var weights = v.AsSpan(at + 12, 4).ToArray(); var sum = weights.Sum();
            if (sum <= 0 || weights.Any(w => w < 0)) throw new InvalidDataException("Skin weights are invalid.");
            var normalized = weights.Select(w => (double)w / sum * 255).ToArray(); var quantized = normalized.Select(w => (int)Math.Floor(w)).ToArray();
            foreach (var j in Enumerable.Range(0, 4).OrderByDescending(j => normalized[j] - quantized[j]).Take(255 - quantized.Sum())) quantized[j]++;
            for (var j = 0; j < 4; j++)
            {
                var joint = v[at + 8 + j];
                if (quantized[j] != 0 && (joint != (int)joint || !joints.ContainsKey((int)joint))) throw new InvalidDataException("Skin joint does not exist.");
                output.Write(quantized[j] == 0 ? (byte)0 : checked((byte)joints[(int)joint]));
            }
            output.Write(quantized.Select(w => checked((byte)w)).ToArray());
        }
        return stream.ToArray();
    }
}
