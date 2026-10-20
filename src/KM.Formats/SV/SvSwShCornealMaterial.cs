using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static class SvSwShCornealMaterial
{
    public const string ProgramName = "Cornea";
    public const string NativeProgramName = "Body00";
    public const string EnvironmentName = "sphere_0330a_env";

    public static byte[] Template(byte[] bodyModel, byte[] reflectionModel)
    {
        var body = new ModelBuffer(bodyModel); var reflection = new ModelBuffer(reflectionModel);
        var eye = body.Tables(body.Root, 6, 128).Single(material => body.Text(material, 0) == "Eye");
        var donor = reflection.Tables(reflection.Root, 6, 128).Single(material => reflection.Text(material, 0) == NativeProgramName);
        if (body.Text(eye, 1) != "PokeDefaultShader" || reflection.Text(donor, 1) != "PokeDefaultShader")
            throw new InvalidDataException("Corneal reflection requires the native opaque material contract.");
        var switches = reflection.Tables(donor, 12, 128);
        byte Switch(ModelBuffer source, int parameter) => source.Field(parameter, 1) is var offset && offset != 0 ? source.U8(offset) : (byte)0;
        var originalSwitches = body.Tables(eye, 12, 128).ToDictionary(parameter => body.Text(parameter, 0)!, parameter => Switch(body, parameter));
        foreach (var parameter in switches)
        {
            var name = reflection.Text(parameter, 0)!;
            var expected = name is "SphereMapEnable" or "NormalEdgeEnable" ? 1 : originalSwitches[name];
            if (Switch(reflection, parameter) != expected)
                throw new InvalidDataException("Native corneal shader switches require another adapter.");
        }
        if (switches.Length != originalSwitches.Count) throw new InvalidDataException("Native corneal shader switches are incomplete.");
        var writer = new ModelTableBuilder(bodyModel);
        var entries = writer.Vector(eye, 12, switches.Length); var donorCopy = writer.Append(reflectionModel);
        for (var index = 0; index < switches.Length; index++) writer.Pointer(entries[index], donorCopy + switches[index]);
        foreach (var field in new[] { 3, 5 })
        {
            var programField = field == 3 ? 8 : 10;
            var (donorStart, donorCount) = reflection.Vector(reflection.Root, field, 4, 256);
            var donorIndex = checked((int)reflection.Value(donor, programField));
            if ((uint)donorIndex >= donorCount || reflection.StringAt(donorStart + donorIndex * 4) != NativeProgramName)
                throw new InvalidDataException("Native corneal program identity is unsupported.");
            var (start, count) = body.Vector(body.Root, field, 4, 256);
            var names = Enumerable.Range(0, count).Select(index => body.StringAt(start + index * 4)).ToArray();
            if (names.Contains(ProgramName, StringComparer.Ordinal)) throw new InvalidDataException("Corneal program identity is already allocated.");
            var slots = writer.Vector(body.Root, field, count + 1);
            for (var index = 0; index < count; index++) writer.Pointer(slots[index], writer.String(names[index]));
            writer.Pointer(slots[count], writer.String(ProgramName));
            writer.Int32(writer.Field(eye, programField), count);
        }
        return writer.Finish(body.Root);
    }

    public static PreviewPrimitive Normals(PreviewPrimitive primitive, byte[] objectNormals)
    {
        if (!primitive.Material.Name.Contains("eye", StringComparison.Ordinal))
            throw new InvalidDataException("Corneal normals require an eye surface.");
        var texture = new ModelTextureDocument(objectNormals); var pixels = texture.Pixels();
        if (texture.Srgb) throw new InvalidDataException("Corneal normals must be linear.");
        var vertices = primitive.Vertices.ToArray();
        foreach (var vertex in primitive.Indices.Distinct())
        {
            var offset = checked((int)vertex * 16);
            var horizontal = Math.Clamp((int)(vertices[offset + 6] * texture.Width), 0, texture.Width - 1);
            var vertical = Math.Clamp((int)((1 - vertices[offset + 7]) * texture.Height), 0, texture.Height - 1);
            var pixel = (vertical * texture.Width + horizontal) * 4;
            var normal = new Vector3(pixels[pixel] / 127.5f - 1, pixels[pixel + 1] / 127.5f - 1, pixels[pixel + 2] / 127.5f - 1);
            if (normal.LengthSquared() < .5f) throw new InvalidDataException("Corneal normal sample is degenerate.");
            normal = Vector3.Normalize(normal);
            vertices[offset + 3] = normal.X; vertices[offset + 4] = normal.Y; vertices[offset + 5] = normal.Z;
        }
        return primitive with { Vertices = vertices };
    }

    public static byte[] GlobalLoop(byte[] animation)
    {
        var source = new ModelBuffer(animation); var writer = new ModelTableBuilder(animation);
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128)
            .Where(material => source.Text(material, 0)!.Contains("eye", StringComparison.Ordinal)))
            foreach (var field in new[] { 2, 3 })
            {
                var parameters = source.Tables(material, field, 256)
                    .Where(parameter => source.Text(parameter, 0) is not ("SphereMapColorVal" or "SphereMapColor")).ToArray();
                var slots = writer.Vector(material, field, parameters.Length); var copy = writer.Append(animation);
                for (var index = 0; index < parameters.Length; index++) writer.Pointer(slots[index], copy + parameters[index]);
            }
        return writer.Finish(source.Root);
    }
}
