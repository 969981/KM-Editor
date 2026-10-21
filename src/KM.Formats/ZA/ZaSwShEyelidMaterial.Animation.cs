using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static partial class ZaSwShEyelidMaterial
{
    public static (Dictionary<string, float> Scalars, Dictionary<string, Vector3> Vectors) Parameters(Contract contract, Func<string, int, float?> sample)
    {
        Vector4 Vector(string name)
        {
            var fallback = contract.Vectors.GetValueOrDefault(name, name.StartsWith("UVScaleOffset", StringComparison.Ordinal) ? new(1, 1, 0, 0) : new(.5f, .5f, 0, 0));
            return new(sample(name, 0) ?? fallback.X, sample(name, 1) ?? fallback.Y, sample(name, 2) ?? fallback.Z, sample(name, 3) ?? fallback.W);
        }
        var basis = Vector("UVScaleOffset"); var center = Vector("UVCenter0"); var normal = Vector("UVScaleOffsetNormal");
        var scalars = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["ColorUVScaleU"] = basis.X, ["ColorUVScaleV"] = basis.Y,
            ["ColorUVTranslateU"] = center.X + basis.Z - contract.Origin.X,
            ["ColorUVTranslateV"] = center.Y + basis.W - contract.Origin.Y,
            ["ColorBaseU"] = center.X, ["ColorBaseV"] = center.Y,
            ["Layer1UVScaleU"] = normal.X, ["Layer1UVScaleV"] = -normal.Y,
            ["Layer1UVTranslateU"] = (contract.Origin.X - normal.Z) * normal.X,
            ["Layer1UVTranslateV"] = 1 - (contract.Origin.Y - normal.W) * normal.Y
        };
        var vectors = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        foreach (var (channel, first) in new[] { (3, 0), (4, 2), (2, 4) })
        {
            var role = channel == 3 ? "UpperEyelidColorMap" : channel == 4 ? "LowerEyelidColorMap" : "EyelidShadowMaskMap";
            var wrap = contract.Wraps.GetValueOrDefault(role, Vector2.One);
            for (var component = 0; component < 2; component++)
                scalars.Add(ScalarNames[10 + first + component], wrap[component] switch
                {
                    0 or 1 or 6 => 0, 7 => 1,
                    _ => throw new InvalidDataException("Eye addressing needs another adapter.")
                });
            var transform = Vector("UVScaleOffset" + channel);
            var angle = sample("UVRotation" + channel, 0) ?? contract.Scalars.GetValueOrDefault("UVRotation" + channel);
            var cosine = MathF.Cos(angle); var sine = MathF.Sin(angle);
            var horizontal = contract.Origin.X - .5f; var vertical = contract.Origin.Y - .5f;
            Vector3[] rows =
            [
                new(cosine * transform.X, sine * transform.X,
                    (cosine * horizontal + sine * vertical - transform.Z) * transform.X + .5f),
                new(sine * transform.Y, -cosine * transform.Y,
                    .5f - (cosine * vertical - sine * horizontal - transform.W) * transform.Y)
            ];
            for (var row = 0; row < 2; row++)
                if (channel == 2)
                    for (var component = 0; component < 3; component++)
                        scalars.Add(ScalarNames[16 + row * 3 + component], rows[row][component]);
                else vectors.Add(VectorNames[first + row], rows[row]);
        }
        if (scalars.Values.Any(value => !float.IsFinite(value)) || vectors.Values.Any(value => !float.IsFinite(value.X + value.Y + value.Z)))
            throw new InvalidDataException("Eye animation transform is invalid.");
        return (scalars, vectors);
    }

    public static PreviewMaterialTrack[] ReadTracks(ModelBuffer source, int material, float ratio)
    {
        var name = source.Text(material, 0) ?? throw new InvalidDataException("Eye animation is unnamed.");
        var result = new List<PreviewMaterialTrack>();
        PreviewKey[] Keys(int curve) => source.Tables(curve, 0, 18000).Select(key =>
        {
            float Value(int field) => source.Field(key, field) is var offset && offset != 0 ? source.Float(offset) : 0;
            return new PreviewKey(Value(0) * ratio, [Value(1)]);
        }).ToArray();
        foreach (var vector in source.Tables(material, 2, 256))
        {
            var parameter = source.Text(vector, 0)!;
            if (parameter is not ("UVScaleOffset" or "UVScaleOffset1" or "UVScaleOffset2" or "UVScaleOffset3" or "UVScaleOffset4" or "UVScaleOffsetNormal"))
                throw new InvalidDataException($"Eye vector animation needs another adapter: {parameter}.");
            var channels = source.Table(vector, 1);
            result.Add(new(name, parameter, Enumerable.Range(0, 4).Select(component => Keys(source.Table(channels, component))).ToArray()));
        }
        foreach (var scalar in source.Tables(material, 1, 256))
        {
            var parameter = source.Text(scalar, 0)!;
            if (parameter is not ("UVRotation2" or "UVRotation3" or "UVRotation4" or "LayerMaskScale5"))
                throw new InvalidDataException($"Eye scalar animation needs another adapter: {parameter}.");
            result.Add(new(name, parameter, [Keys(source.Table(scalar, 1))]));
        }
        foreach (var channel in result.SelectMany(track => track.Channels))
            if (channel.Any(key => !float.IsFinite(key.Frame) || key.Frame < 0 || !float.IsFinite(key.Value[0]))
                || channel.Zip(channel.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame))
                throw new InvalidDataException("Eye source animation keys are invalid.");
        return result.ToArray();
    }

    public static PreviewMaterialTrack[] Tracks(Contract contract, IReadOnlyList<PreviewMaterialTrack> source, int frames)
    {
        if (frames is < 1 or > 18000 || source.GroupBy(track => track.Parameter).Any(group => group.Count() != 1))
            throw new InvalidDataException("Eye animation timing or identity is invalid.");
        var raw = source.ToDictionary(track => track.Parameter, StringComparer.Ordinal);
        float? Sample(string parameter, int component, int frame)
        {
            if (!raw.TryGetValue(parameter, out var track) || component >= track.Channels.Length || track.Channels[component].Length == 0) return null;
            var keys = track.Channels[component];
            if (frame <= keys[0].Frame) return keys[0].Value[0];
            for (var index = 1; index < keys.Length; index++)
                if (frame < keys[index].Frame)
                    return keys[index - 1].Value[0] + (keys[index].Value[0] - keys[index - 1].Value[0])
                        * (frame - keys[index - 1].Frame) / (keys[index].Frame - keys[index - 1].Frame);
            return keys[^1].Value[0];
        }
        var states = Enumerable.Range(0, frames).Select(frame => Parameters(contract, (name, component) => Sample(name, component, frame))).ToArray();
        PreviewKey[] Curve(Func<int, float> value)
        {
            var keys = Enumerable.Range(0, frames).Select(frame => new PreviewKey(frame, [value(frame)])).ToArray();
            return keys.All(key => key.Value[0] == keys[0].Value[0]) ? [keys[0]] : keys;
        }
        return ScalarNames.Take(10).Concat(ScalarNames.Skip(16)).Select(name => new PreviewMaterialTrack(contract.Name, "EyeScalar:" + name, [Curve(frame => states[frame].Scalars[name])]))
            .Concat(VectorNames.Select(name => new PreviewMaterialTrack(contract.Name, "EyeVector:" + name,
                Enumerable.Range(0, 3).Select(component => Curve(frame => states[frame].Vectors[name][component])).ToArray())))
            .Concat(source.Where(track => track.Parameter == "LayerMaskScale5")).ToArray();
    }

    public static byte[] KeepAnimationLocal(byte[] animation, IReadOnlySet<string> materials)
    {
        var source = new ModelBuffer(animation); var writer = new ModelTableBuilder(animation);
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128))
        {
            if (!materials.Contains(source.Text(material, 0)!)) continue;
            foreach (var field in new[] { 2, 3 })
            {
                var tracks = source.Tables(material, field, 256);
                var retained = tracks.Where(track => field == 2
                    ? !ScalarNames.Contains(source.Text(track, 0), StringComparer.Ordinal)
                        && source.Text(track, 0) is not ("NormalMapUVScaleU" or "NormalMapUVScaleV" or "Layer1BaseU" or "Layer1BaseV")
                    : !VectorNames.Contains(source.Text(track, 0), StringComparer.Ordinal)).ToArray();
                if (retained.Length == tracks.Length) continue;
                var slots = writer.Vector(material, field, retained.Length); var copy = writer.Append(animation);
                for (var index = 0; index < retained.Length; index++) writer.Pointer(slots[index], copy + retained[index]);
            }
        }
        return writer.Finish(source.Root);
    }
}
