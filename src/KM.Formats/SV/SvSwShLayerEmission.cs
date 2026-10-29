// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.ZA;

namespace KM.Formats.SV;

/// <summary>Retains independently animated source emission layers on the native body surface.</summary>
public static partial class SvSwShLayerEmission
{
    public const string ProgramName = "LayerEmission";
    public const string MapProgramName = "MapEmission";
    public const string FresnelProgramName = "FresnelEmission";
    public const string LavaProgramName = "LavaEmission";
    public static readonly string[] TransparentPrograms = ["LayerEmissionFront", "LayerEmissionBack"];
    public static readonly string[] Scalars = ["L1ConstantColor0Val", "L1AddColor0Val", "L1ConstantColor1Val", "L1AddColor1Val"];
    public static readonly string[] Vectors = ["L1ConstantColor0", "L1AddColor0", "L1ConstantColor1", "L1AddColor1"];
    public sealed record Contract(string Name, string Mask, Vector3[] Colors)
    {
        public Vector4 MaskUv { get; init; } = new(1, 1, 0, 0);
        public Vector2 MaskWrap { get; init; }
        public float[] Initial { get; init; } = new float[4];
        public bool Transparent { get; init; }
        public bool ColorMap { get; init; }
        public bool BaseColorMap { get; init; }
        public bool MaskUnused { get; init; }
        public string? FresnelNormal { get; init; }
        public string? FresnelNormal1 { get; init; }
        public bool Lava { get; init; }
        public string Program(bool inner = false) => Lava ? LavaProgramName : FresnelNormal is not null ? FresnelProgramName : ColorMap ? MapProgramName : Transparent ? TransparentPrograms[inner ? 1 : 0] : ProgramName;
    }

    public static IReadOnlyDictionary<string, Contract> Contracts(byte[] bytes, PreviewScene scene)
    {
        if (SvSwShLavaMaterial.IsLava(scene)) return SvSwShLavaMaterial.EmissionContracts(bytes, scene);
        if (scene.Primitives.Any(p => p.Name.StartsWith("pm0179_", StringComparison.Ordinal)
            || p.Name.StartsWith("pm0180_", StringComparison.Ordinal) || p.Name.StartsWith("pm0181_", StringComparison.Ordinal)))
            return FresnelContracts(bytes);
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        var spherical = scene.Primitives.Any(p => p.Name.StartsWith("pm0100_00_41_", StringComparison.Ordinal)
            || p.Name.StartsWith("pm0101_00_41_", StringComparison.Ordinal));
        var tynamo = scene.Primitives.Any(p => p.Name.StartsWith("pm0602_00_00_", StringComparison.Ordinal));
        var expected = scene.Primitives.Any(p => p.Name.StartsWith("pm0075_00_11_", StringComparison.Ordinal))
            ? new[] { "body_b" } : scene.Primitives.Any(p => p.Name.StartsWith("pm0076_00_11_", StringComparison.Ordinal))
                ? new[] { "body_a_01", "body_a_02", "body_b_01" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0100_00_41_", StringComparison.Ordinal)) ? new[] { "eye", "body_01" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0101_00_41_", StringComparison.Ordinal)) ? new[] { "eye", "body_a" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0386_11_00_", StringComparison.Ordinal)) ? new[] { "body_b_01", "body_b_02", "body_b_00", "body_a" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0386_", StringComparison.Ordinal)) ? new[] { "body_a_01", "body_a_02", "body_a_00", "body_b" }
                : tynamo ? new[] { "body_00", "body_01" }
                : throw new InvalidDataException("Layer emission requires a qualified source model.");
        foreach (var name in expected)
        {
            var sections = scene.Primitives.Count(p => p.Material.Name == name);
            var deoxys = scene.Primitives.Any(p => p.Name.StartsWith("pm0386_", StringComparison.Ordinal));
            var deoxysSections = name switch
            {
                "body_b_00" => 3, "body_a" => 2,
                "body_b" => 2, "body_a_00" => scene.Primitives.Any(p => p.Name.StartsWith("pm0386_14_", StringComparison.Ordinal)) ? 1 : 2,
                _ => 1,
            };
            if (deoxys ? sections != deoxysSections : spherical ? sections is < 1 or > 2 : sections != 1)
                throw new InvalidDataException("Layer emission geometry is ambiguous.");
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            { var at = source.Field(p, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); });
            var transparent = name.EndsWith("_02", StringComparison.Ordinal) && scene.Primitives.Any(p => p.Name.StartsWith("pm0386_", StringComparison.Ordinal));
            var colorMap = source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "EmissionColorMap");
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(transparent
                    ? ["Transparent", "TransparentInner"] : [tynamo ? "IkCharacter" : spherical ? "SSS" : "Standard"])
                || vectors["UVScaleOffset"] != new Vector4(tynamo ? 2 : 1, 1, 0, 0)
                || (colorMap ? scalars["EmissionIntensity"] is not (.1f or .2f) : scalars["EmissionIntensity"] != 0)
                || !colorMap && Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1 || scalars["EmissionIntensityLayer" + layer] != 0))
                throw new InvalidDataException("Source emission surface requires another adapter.");
            var colors = Enumerable.Range(1, 4).Select(layer => colorMap ? new Vector4(0, 0, 0, 1) : vectors["EmissionColorLayer" + layer]).ToArray();
            if (colors.Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X is < 0 or > 2 || c.Y is < 0 or > 2 || c.Z is < 0 or > 2 || c.W != 1))
                throw new InvalidDataException("Source emission color exceeds its supported range.");
            var texture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == (colorMap ? "EmissionColorMap" : "LayerMaskMap"));
            var selector = source.Tables(material, 9, 256).SingleOrDefault(p => source.Text(p, 0) == (colorMap ? "UVIndexEmission" : "UVIndexLayerMask"));
            var uvIndex = selector == 0 ? uint.MaxValue : source.Value(selector, 1);
            if (uvIndex is not (0 or uint.MaxValue)) throw new InvalidDataException("Source emission mask coordinates need another adapter.");
            var samplers = source.Tables(material, 3, 32); var sampler = checked((int)source.Value(texture, 2));
            if ((uint)sampler >= samplers.Length) throw new InvalidDataException("Source emission sampler is missing.");
            result.Add(name, new(name, source.Text(texture, 1)!, colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray())
                { ColorMap = colorMap, BaseColorMap = colorMap
                    && source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "BaseColorMap" && source.Text(t, 1) == source.Text(texture, 1))
                    && !source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "LayerMaskMap")
                    && (!vectors.TryGetValue("BaseColor", out var baseColor) || baseColor == Vector4.One),
                    Initial = colorMap ? [scalars["EmissionIntensity"], 0, 0, 0] : new float[4],
                    Transparent = transparent, MaskUv = uvIndex == 0 ? new(1, 1, 0, 0) : vectors["UVScaleOffset"],
                    MaskWrap = new(source.Value(samplers[sampler], 9), source.Value(samplers[sampler], 10)) });
        }
        return result;
    }

    public static (string Name, byte[] Bytes) Mask(Contract contract, Func<string, byte[]> read, CancellationToken token,
        ModelTextureMipCache? encodingCache = null)
    {
        var bytes = read(contract.Mask); var name = Path.GetFileNameWithoutExtension(contract.Mask);
        if (contract.MaskUv == new Vector4(1, 1, 0, 0)) return (name, bytes);
        var image = ZaSwShMaterialBaker.Read(bytes); var pixels = new byte[image.Pixels.Length];
        for (var y = 0; y < image.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                var value = ZaSwShMaterialBaker.Sample(image, new((x + .5f) / image.Width, (y + .5f) / image.Height), contract.MaskUv, contract.MaskWrap);
                for (var c = 0; c < 4; c++) pixels[(y * image.Width + x) * 4 + c] = (byte)Math.Clamp((int)MathF.Round(value[c] * 255), 0, 255);
            }
        }
        var target = "e" + name[1..];
        var document = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(bytes, image.Width, image.Height, false, compressed: false), name, target));
        return (target, document.EncodeMipImages(SwShTextureFiltering.Mips(pixels, image.Width, image.Height, document.MipCount), token, encodingCache));
    }

    public static byte[] Template(byte[] bytes, string programName = ProgramName, string? sourceProgram = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var field in new[] { 3, 5 })
        {
            var (start, count) = source.Vector(source.Root, field, 4, 256);
            var names = Enumerable.Range(0, count).Select(i => source.StringAt(start + i * 4)).ToArray();
            if (names.Contains(programName, StringComparer.Ordinal)) throw new InvalidDataException("Layer emission program is already allocated.");
            var slots = writer.Vector(source.Root, field, count + 1);
            for (var i = 0; i < count; i++) writer.Pointer(slots[i], writer.String(names[i]));
            writer.Pointer(slots[count], writer.String(programName));
        }
        var originals = source.Tables(source.Root, 6, 128);
        var (programStart, _) = source.Vector(source.Root, 3, 4, 256);
        var original = sourceProgram is null ? originals[0] : originals.Single(m => source.StringAt(programStart + checked((int)source.Value(m, 8)) * 4) == sourceProgram);
        var slotsMaterials = writer.Vector(source.Root, 6, originals.Length + 1);
        var fields = Enumerable.Range(2, 9).Concat(Enumerable.Range(15, 5));
        var absent = fields.Where(f => f is not (8 or 10) && source.Field(original, f) == 0).ToHashSet();
        var values = fields.Where(f => !absent.Contains(f)).ToDictionary(f => f, f => f is 8 or 10
            ? ModelTableBuilder.Integer(source.Vector(source.Root, f == 8 ? 3 : 5, 4, 256).Count)
            : source.Slice(source.Field(original, f), f is 3 or 4 or >= 15 ? 1 : 4).ToArray());
        var target = writer.Table(21, values, absent: absent); writer.Pointer(slotsMaterials[^1], target);
        var copy = writer.Append(bytes);
        for (var i = 0; i < originals.Length; i++) writer.Pointer(slotsMaterials[i], copy + originals[i]);
        foreach (var field in new[] { 0, 1, 11, 12, 13, 14, 20 }) writer.Reference(target, field, copy + source.Table(original, field));
        return writer.Finish(source.Root);
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts, IReadOnlyDictionary<string, string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        int Program(int field, string name)
        {
            var (start, count) = source.Vector(source.Root, field, 4, 256);
            return Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == name);
        }
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            if (!names.TryGetValue(source.Text(material, 0)!, out var original) || !contracts.TryGetValue(original, out var contract)) continue;
            var program = contract.Program(source.Text(material, 0)!.EndsWith("_inner", StringComparison.Ordinal));
            writer.Int32(writer.Field(material, 8), Program(3, program)); writer.Int32(writer.Field(material, 10), Program(5, program));
            foreach (var p in source.Tables(material, 12, 256))
                if (source.Text(p, 0) is "Layer1Enable" or "EmissionMaskUse" or "SwitchEmissionMaskTexUV")
                { var at = source.Field(p, 1); if (at != 0) writer.Write(at, [0]); }
            foreach (var p in source.Tables(material, 13, 256))
                if (Scalars.Contains(source.Text(p, 0)!, StringComparer.Ordinal) || source.Text(p, 0) == "EmissionMaskVal")
                {
                    var channel = Array.IndexOf(Scalars, source.Text(p, 0));
                    writer.Single(source.Field(p, 1), channel < 0 ? 0 : contract.Initial[channel]);
                }
            foreach (var p in source.Tables(material, 14, 256))
            {
                var index = Array.IndexOf(Vectors, source.Text(p, 0)); if (index < 0) continue;
                for (var c = 0; c < 3; c++) writer.Single(source.Field(p, 1) + c * 4, contract.Colors[index][c]);
            }
        }
        return writer.Finish(source.Root);
    }

    public static PreviewClip Read(byte[] bytes, PreviewClip skeleton, PreviewScene scene, IReadOnlyDictionary<string, Contract> contracts,
        Func<byte[], PreviewClip>? decode = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeleton.Rate);
        if (rate == 0) throw new InvalidDataException("Layer emission animation rate is invalid.");
        var additional = new List<PreviewMaterialTrack>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4); if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var scalars = source.Tables(material, 1, 256); if (scalars.Length == 0) continue;
                var name = source.Text(material, 0)!; var mesh = source.Text(track, 0)!;
                var bound = scene.Primitives.Any(p => p.Name == mesh && p.Material.Name == name);
                var stale = name == "body_b" && (mesh == "pm0075_00_11_body_a_mesh_shape"
                    || mesh is "pm0076_00_11_bodybinc_mesh_shape" or "pm0076_00_11_body_c_mesh_shape" or "pm0076_00_11_body_d_mesh_shape");
                if (!bound && !stale || bound && !contracts.ContainsKey(name)) throw new InvalidDataException("Source emission target is not qualified.");
                foreach (var scalar in scalars)
                {
                    var parameter = source.Text(scalar, 0)!;
                    var fresnel = bound && contracts[name].FresnelNormal is not null;
                    var layer = fresnel ? parameter switch { "EmissionIntensity" => 0, "EmissionIntensityLayer1" => 1, _ => -1 }
                        : Array.IndexOf(new[] { "EmissionIntensityLayer1", "EmissionIntensityLayer2", "EmissionIntensityLayer3", "EmissionIntensityLayer4" }, parameter);
                    if (layer < 0) throw new InvalidDataException("Source scalar animation requires another adapter.");
                    var keys = source.Tables(source.Table(scalar, 1), 0, 18000).Select(k =>
                    {
                        float Value(int f) => source.Field(k, f) is var at && at != 0 ? source.Float(at) : 0;
                        return new PreviewKey(Value(0) * skeleton.Rate / rate, [Value(1)]);
                    }).ToArray();
                    if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]) || k.Value[0] < 0 || k.Value[0] > (fresnel ? 8 : 1))
                        || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame)) throw new InvalidDataException("Source emission keys are invalid.");
                    if (bound) additional.Add(new(name, fresnel ? "EmissionIntensityLayer" + (layer + 1) : parameter, [keys]));
                }
                writer.Vector(material, 1, 0);
            }
        }
        var warnings = new List<string>(); var sanitized = writer.Finish(source.Root);
        var decoded = decode is null ? PreviewMaterialAnimation.Read(new(sanitized), skeleton, warnings, true) : decode(sanitized);
        if (warnings.Count != 0) throw new InvalidDataException("Layer emission source needs another animation adapter.");
        additional = additional.GroupBy(t => (t.Material, t.Parameter)).Select(group =>
        {
            var first = group.First();
            if (group.Any(track => track.Channels.Length != first.Channels.Length
                || !track.Channels.Zip(first.Channels).All(channels => channels.First.Length == channels.Second.Length
                    && channels.First.Zip(channels.Second).All(keys => keys.First.Frame == keys.Second.Frame
                        && keys.First.Value.SequenceEqual(keys.Second.Value)))))
                throw new InvalidDataException("Source emission binding is ambiguous.");
            return first;
        }).ToList();
        foreach (var name in contracts.Keys) for (var layer = 1; layer <= 4; layer++)
            if (!additional.Any(t => t.Material == name && t.Parameter == "EmissionIntensityLayer" + layer))
                additional.Add(new(name, "EmissionIntensityLayer" + layer, [[new(0, [contracts[name].Initial[layer - 1]])]]));
        return decoded with { Materials = decoded.Materials.Concat(additional).ToArray() };
    }

    public static byte[] Animation(byte[] bytes, IReadOnlyList<PreviewMaterialTrack> tracks)
    {
        var groups = tracks.Where(t => t.Parameter.StartsWith("EmissionIntensityLayer", StringComparison.Ordinal)).GroupBy(t => t.Material).ToArray();
        if (groups.Length == 0) return bytes;
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder();
        var root = writer.Table(5, absent: Enumerable.Range(0, 5).Where(f => f != 2 && source.Table(source.Root, f) == 0).ToHashSet());
        var parent = writer.Table(1); writer.Reference(root, 2, parent);
        var existing = source.Tables(source.Table(source.Root, 2), 0, 128); var frames = checked((int)source.Value(source.Table(source.Root, 0), 1));
        if (frames is < 1 or > 18000) throw new InvalidDataException("Native emission timeline is invalid.");
        var retained = existing.Where(t => !groups.Any(g => g.Key == source.Text(t, 0))).ToArray();
        var slots = writer.Vector(parent, 0, retained.Length + groups.Length); var copy = writer.Append(bytes);
        foreach (var field in new[] { 0, 1, 3, 4 })
            if (source.Table(source.Root, field) is var table && table != 0) writer.Reference(root, field, copy + table);
        for (var i = 0; i < retained.Length; i++) writer.Pointer(slots[i], copy + retained[i]);
        for (var i = 0; i < groups.Length; i++)
        {
            var material = writer.Table(4); writer.Pointer(slots[retained.Length + i], material); writer.Text(material, 0, groups[i].Key);
            var prior = existing.SingleOrDefault(t => source.Text(t, 0) == groups[i].Key);
            foreach (var field in new[] { 1, 3 })
                if (prior != 0 && source.Table(prior, field) is var table && table != 0) writer.Reference(material, field, copy + table);
                else writer.Vector(material, field, 0);
            var priorScalars = prior == 0 ? [] : source.Tables(prior, 2, 256);
            if (priorScalars.Any(p => Scalars.Contains(source.Text(p, 0)!, StringComparer.Ordinal)))
                throw new InvalidDataException("Native emission scalar is already animated.");
            var values = writer.Vector(material, 2, 4 + priorScalars.Length);
            for (var p = 0; p < priorScalars.Length; p++) writer.Pointer(values[4 + p], copy + priorScalars[p]);
            for (var layer = 0; layer < 4; layer++)
            {
                var track = groups[i].Single(t => t.Parameter == "EmissionIntensityLayer" + (layer + 1));
                if (track.Channels.Length != 1 || track.Channels[0].Length == 0) throw new InvalidDataException("Native emission channel is invalid.");
                var keys = track.Channels[0]; var constant = keys.All(k => k.Value[0] == keys[0].Value[0]);
                var scalar = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                writer.Pointer(values[layer], scalar); writer.Text(scalar, 0, Scalars[layer]); var curve = writer.Table(1); writer.Reference(scalar, 2, curve);
                float Sample(int f)
                {
                    if (f <= keys[0].Frame) return keys[0].Value[0];
                    for (var k = 1; k < keys.Length; k++) if (f < keys[k].Frame)
                        return keys[k - 1].Value[0] + (keys[k].Value[0] - keys[k - 1].Value[0]) * (f - keys[k - 1].Frame) / (keys[k].Frame - keys[k - 1].Frame);
                    return keys[^1].Value[0];
                }
                if (constant) writer.Single(writer.Field(curve, 0), keys[0].Value[0]);
                else writer.Blob(curve, 0, ModelTableBuilder.Floats(Enumerable.Range(0, frames).Select(Sample).ToArray()), frames);
            }
        }
        return writer.Finish(root);
    }

    public static byte[] GlobalLoop(byte[] bytes, IReadOnlySet<string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var retained = new List<(int[] Slots, int[] Entries)>();
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128).Where(m => names.Contains(source.Text(m, 0)!)))
            foreach (var (field, parameters) in new[] { (2, Scalars), (3, Vectors) })
            {
                var entries = source.Tables(material, field, 256).Where(p => !parameters.Contains(source.Text(p, 0)!, StringComparer.Ordinal)).ToArray();
                retained.Add((writer.Vector(material, field, entries.Length), entries));
            }
        // Retained curves are immutable. All replacement vectors can reference
        // one source graph instead of copying it for every material parameter kind.
        var copy = writer.Append(bytes);
        foreach (var (slots, entries) in retained)
            for (var i = 0; i < entries.Length; i++) writer.Pointer(slots[i], copy + entries[i]);
        return writer.Finish(source.Root);
    }

    public static byte[] Controller(byte[] bytes, IReadOnlySet<string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            if (source.Text(layer, 1) is "eye01" or "mouth01") continue;
            foreach (var material in source.Tables(source.Table(source.Table(layer, 2), 1), 1, 128))
            {
                if (!names.Contains(source.Text(material, 0)!)) continue;
                var floats = source.Table(material, 2); var entries = source.Tables(floats, 1, 256).Select(t => source.Text(t, 0)!).Concat(Scalars).Distinct(StringComparer.Ordinal).ToArray();
                var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [source.Field(floats, 0) is var at && at != 0 ? source.U8(at) : (byte)0] });
                writer.Reference(material, 2, replacement); var slots = writer.Vector(replacement, 1, entries.Length);
                for (var i = 0; i < entries.Length; i++) { var item = writer.Table(1); writer.Pointer(slots[i], item); writer.Text(item, 0, entries[i]); }
            }
        }
        return writer.Finish(source.Root);
    }
}
