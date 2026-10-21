using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static partial class ZaSwShEyelidMaterial
{
    public const string ProgramName = "LayeredEye";
    public const string NativeProgramName = "LEye";
    public static readonly string[] ScalarNames =
    [
        "ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV", "ColorBaseU", "ColorBaseV",
        "Layer1UVScaleU", "Layer1UVScaleV", "Layer1UVTranslateU", "Layer1UVTranslateV",
        "L1ConstantColor0Val", "L1AddColor0Val", "L1ConstantColor1Val", "L1AddColor1Val", "L1ConstantColorSd0Val", "L1ConstantColorSd1Val",
        "Layer1BaseU", "Layer1BaseV", "NormalMapUVScaleU", "NormalMapUVScaleV", "EmissionMaskVal", "Layer1OverLerpValue"
    ];
    public static readonly string[] VectorNames =
    [
        "L1ConstantColor0", "L1AddColor0", "L1ConstantColor1", "L1AddColor1"
    ];

    public sealed record Contract(string Name, Vector2 Origin, IReadOnlyDictionary<string, Vector4> Vectors,
        IReadOnlyDictionary<string, float> Scalars, IReadOnlyDictionary<string, string> Textures,
        IReadOnlyDictionary<string, Vector2> Wraps);

    public static IReadOnlyDictionary<string, Contract> Materials(byte[] sourceMaterials, PreviewScene scene,
        IReadOnlyDictionary<string, Vector2>? coordinateOffsets = null)
    {
        var source = new ModelBuffer(sourceMaterials);
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            var name = source.Text(material, 0) ?? throw new InvalidDataException("Eye material is unnamed.");
            if (!name.Contains("eye", StringComparison.Ordinal)) continue;
            var primitives = scene.Primitives.Where(primitive => primitive.Material.Name == name).ToArray();
            if (primitives.Length != 1) throw new InvalidDataException("Eye material geometry is ambiguous.");
            var primitive = primitives[0];
            var coordinates = primitive.Indices.Distinct().Select(index => new Vector2(primitive.Vertices[index * 16 + 6], primitive.Vertices[index * 16 + 7])).ToArray();
            var origin = new Vector2(MathF.Floor(coordinates.Min(point => point.X)), MathF.Floor(coordinates.Min(point => point.Y)));
            if (coordinates.Any(point => !float.IsFinite(point.X + point.Y) || point.X - origin.X >= 1 || point.Y - origin.Y >= 1))
                throw new InvalidDataException("Eye UV tile needs another adapter.");
            if (coordinateOffsets?.TryGetValue(name, out var coordinateOffset) == true)
            {
                if (!float.IsFinite(coordinateOffset.X + coordinateOffset.Y) || coordinates.Any(point =>
                        point.X + coordinateOffset.X is < 0 or > 1 || point.Y + coordinateOffset.Y is < 0 or > 1))
                    throw new InvalidDataException("Eye atlas coordinates need another adapter.");
                origin += coordinateOffset;
            }
            var vectors = source.Tables(material, 7, 256).ToDictionary(parameter => source.Text(parameter, 0)!, parameter =>
            {
                var offset = source.Field(parameter, 1);
                return new Vector4(source.Float(offset), source.Float(offset + 4), source.Float(offset + 8), source.Float(offset + 12));
            }, StringComparer.Ordinal);
            var scalars = source.Tables(material, 4, 256).ToDictionary(parameter => source.Text(parameter, 0)!,
                parameter => source.Field(parameter, 1) is var offset && offset != 0 ? source.Float(offset) : 0, StringComparer.Ordinal);
            var samplers = source.Tables(material, 3, 32);
            var textures = new Dictionary<string, string>(StringComparer.Ordinal);
            var wraps = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (var texture in source.Tables(material, 2, 64))
            {
                var role = source.Text(texture, 0)!;
                textures.Add(role, source.Text(texture, 1) ?? throw new InvalidDataException("Eye image is unnamed."));
                var slot = checked((int)source.Value(texture, 2));
                if ((uint)slot >= samplers.Length) throw new InvalidDataException("Eye sampler is missing.");
                wraps.Add(role, new(source.Value(samplers[slot], 9), source.Value(samplers[slot], 10)));
            }
            if (vectors.Values.Any(value => !float.IsFinite(value.X + value.Y + value.Z + value.W))
                || scalars.Values.Any(value => !float.IsFinite(value)) || scalars.GetValueOrDefault("NormalHeight") != 1)
                throw new InvalidDataException("Eye parameters are unsupported.");
            var maskIndex = source.Tables(material, 9, 256).SingleOrDefault(parameter => source.Text(parameter, 0) == "UVIndexLayerMask");
            if (maskIndex != 0 && source.Value(maskIndex, 1) != uint.MaxValue)
                throw new InvalidDataException("Eye color mask coordinates need another adapter.");
            result.Add(name, new(name, origin, vectors, scalars, textures, wraps));
        }
        if (result.Count == 0) throw new InvalidDataException("Layered eye materials are missing.");
        if (coordinateOffsets is not null && coordinateOffsets.Keys.Any(name => !result.ContainsKey(name)))
            throw new InvalidDataException("Eye atlas material is missing.");
        return result;
    }

    public static byte[] Template(byte[] bodyModel, byte[] layeredModel)
    {
        var body = new ModelBuffer(bodyModel); var layered = new ModelBuffer(layeredModel);
        var eye = body.Tables(body.Root, 6, 128).Single(material => body.Text(material, 0) == "Eye");
        var donor = layered.Tables(layered.Root, 6, 128).Single(material => layered.Text(material, 0) == NativeProgramName);
        if (body.Text(eye, 1) != "PokeDefaultShader" || layered.Text(donor, 1) != "PokeDefaultShader")
            throw new InvalidDataException("Layered eyes require the native opaque material contract.");
        byte Switch(ModelBuffer source, int parameter) => source.Field(parameter, 1) is var offset && offset != 0 ? source.U8(offset) : (byte)0;
        var originalSwitches = body.Tables(eye, 12, 128).ToDictionary(parameter => body.Text(parameter, 0)!, parameter => Switch(body, parameter));
        var switches = layered.Tables(donor, 12, 128);
        if (switches.Length != originalSwitches.Count) throw new InvalidDataException("Native eye switches are incomplete.");
        foreach (var parameter in switches)
        {
            var name = layered.Text(parameter, 0)!;
            var expected = name is "Layer1Enable" or "SwitchPriority" or "NormalEdgeEnable" ? 1 : originalSwitches[name];
            if (Switch(layered, parameter) != expected) throw new InvalidDataException("Native eye switches need another adapter.");
        }
        var writer = new ModelTableBuilder(bodyModel);
        var entries = writer.Vector(eye, 12, switches.Length);
        for (var index = 0; index < switches.Length; index++)
        {
            var name = layered.Text(switches[index], 0)!;
            var value = name == "Layer1Enable" ? 0 : Switch(layered, switches[index]);
            var parameter = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(value) });
            writer.Pointer(entries[index], parameter); writer.Text(parameter, 0, name);
        }
        foreach (var field in new[] { 3, 5 })
        {
            var programField = field == 3 ? 8 : 10;
            var (start, count) = body.Vector(body.Root, field, 4, 256);
            var names = Enumerable.Range(0, count).Select(index => body.StringAt(start + index * 4)).ToArray();
            if (names.Contains(ProgramName, StringComparer.Ordinal)) throw new InvalidDataException("Eye program identity is already allocated.");
            var slots = writer.Vector(body.Root, field, count + 1);
            for (var index = 0; index < count; index++) writer.Pointer(slots[index], writer.String(names[index]));
            writer.Pointer(slots[count], writer.String(ProgramName));
            writer.Int32(writer.Field(eye, programField), count);
        }
        return writer.Finish(body.Root);
    }

    public static byte[] Bind(byte[] nativeModel, IReadOnlyDictionary<string, Contract> contracts,
        IReadOnlyDictionary<string, string> names)
    {
        var native = new ModelBuffer(nativeModel); var writer = new ModelTableBuilder(nativeModel);
        foreach (var material in native.Tables(native.Root, 6, 128))
        {
            var name = native.Text(material, 0)!;
            if (!names.TryGetValue(name, out var originalName) || !contracts.TryGetValue(originalName, out var contract)) continue;
            var (values, vectors) = Parameters(contract, (_, _) => null);
            var scalars = native.Tables(material, 13, 256);
            var scalarSlots = writer.Vector(material, 13, scalars.Length);
            for (var index = 0; index < scalars.Length; index++)
            {
                var parameterName = native.Text(scalars[index], 0)!;
                var value = values.TryGetValue(parameterName, out var mapped) ? mapped
                    : native.Field(scalars[index], 1) is var offset && offset != 0 ? native.Float(offset) : 0;
                var parameter = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Floats(value) });
                writer.Pointer(scalarSlots[index], parameter); writer.Text(parameter, 0, parameterName);
            }
            foreach (var parameter in native.Tables(material, 14, 256))
                if (vectors.TryGetValue(native.Text(parameter, 0)!, out var value))
                {
                    var offset = native.Field(parameter, 1);
                    if (offset == 0) throw new InvalidDataException("Eye matrix storage is missing.");
                    for (var component = 0; component < 3; component++) writer.Single(offset + component * 4, value[component]);
                }
            foreach (var binding in native.Tables(material, 11, 64))
            {
                var role = native.Text(binding, 0) switch
                {
                    "Col0Tex" => "BaseColorMap", "NormalMapTex" => "NormalMap", "EmissionMaskTex" => "UpperEyelidColorMap",
                    "LyCol0Tex" => "LowerEyelidColorMap", "AmbientTex" => "EyelidShadowMaskMap",
                    _ => null
                };
                if (role is null) continue;
                var wrap = contract.Wraps.GetValueOrDefault(role, Vector2.One);
                var sampler = native.Table(binding, 2);
                var fields = new Dictionary<int, byte[]>();
                for (var field = 0; field < 9; field++)
                    fields[field] = native.Field(sampler, field) is var offset && offset != 0 ? native.Slice(offset, 4).ToArray() : ModelTableBuilder.Integer(0);
                for (var component = 0; component < 2; component++)
                    fields[1 + component] = ModelTableBuilder.Integer(wrap[component] switch
                    {
                        0 => 0, 1 => 1, 6 => 2, 7 when role is "UpperEyelidColorMap" or "LowerEyelidColorMap" or "EyelidShadowMaskMap" => 1,
                        _ => throw new InvalidDataException("Eye addressing needs another adapter.")
                    });
                writer.Reference(binding, 2, writer.Table(9, fields));
            }
        }
        return writer.Finish(native.Root);
    }
}
