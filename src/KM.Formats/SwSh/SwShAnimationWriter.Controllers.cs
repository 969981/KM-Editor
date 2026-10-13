// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    public static byte[] Controller(byte[] nativeController, string donorStem, string targetStem,
        IReadOnlyList<string> materialNames, IReadOnlyDictionary<string, string[]> facialBones)
    {
        if (materialNames.Count is < 1 or > 128 || materialNames.Distinct(StringComparer.Ordinal).Count() != materialNames.Count)
            throw new InvalidDataException("Controller material targets are invalid.");
        var source = new ModelBuffer(nativeController); var writer = new ModelTableBuilder(nativeController);
        var bindings = source.Table(source.Root, 6);
        foreach (var binding in source.Tables(bindings, 0, 2048))
        {
            var path = source.Text(binding, 1) ?? throw new InvalidDataException("Controller animation reference is missing.");
            if (!path.Contains(donorStem, StringComparison.Ordinal)) throw new InvalidDataException("Controller has an undeclared animation dependency.");
            writer.Text(binding, 1, path.Replace(donorStem, targetStem, StringComparison.Ordinal));
        }
        var layers = source.Tables(source.Table(source.Root, 0), 0, 128);
        var vectors = new Dictionary<int, int[]>();
        foreach (var layer in layers)
        {
            var masks = source.Table(source.Table(layer, 2), 1);
            if (masks == 0) throw new InvalidDataException("Controller material mask is missing.");
            vectors.Add(layer, writer.Vector(masks, 1, materialNames.Count));
        }
        for (var i = 0; i < materialNames.Count; i++)
        {
            var copy = writer.Append(nativeController); var name = materialNames[i];
            foreach (var layer in layers)
            {
                var masks = source.Table(source.Table(layer, 2), 1);
                var entries = source.Tables(masks, 1, 128).Where(t => source.Text(t, 0) == (name.Contains("eye", StringComparison.Ordinal) ? "Eye" : "Body01Vco")).ToArray();
                if (entries.Length != 1) throw new InvalidDataException("Native controller material mask cannot be resolved.");
                var target = copy + entries[0]; writer.Pointer(vectors[layer][i], target); writer.Text(target, 0, name);
            }
        }
        foreach (var layer in layers)
        {
            var name = source.Text(layer, 1) ?? "";
            if (!facialBones.TryGetValue(name, out var bones)) continue;
            if (bones.Length == 0 || bones.Length > 128 || bones.Distinct(StringComparer.Ordinal).Count() != bones.Length)
                throw new InvalidDataException("Facial controller mask has invalid bone targets.");
            var mask = source.Table(layer, 2);
            var skeletal = writer.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(1) }); writer.Reference(mask, 0, skeletal);
            var slots = writer.Vector(skeletal, 1, bones.Length);
            for (var i = 0; i < bones.Length; i++) { var item = writer.Table(1); writer.Pointer(slots[i], item); writer.Text(item, 0, bones[i]); }
        }
        return writer.Finish(source.Root);
    }

    public static byte[] GlobalMaterialLoop(byte[] nativeAnimation, IReadOnlyList<string> materialNames)
    {
        if (materialNames.Count is < 1 or > 128) throw new InvalidDataException("Global material animation target count is invalid.");
        var source = new ModelBuffer(nativeAnimation); var writer = new ModelTableBuilder(nativeAnimation);
        var skeleton = source.Table(source.Root, 1); if (skeleton != 0) writer.Vector(skeleton, 0, 0);
        var material = source.Table(source.Root, 2);
        if (material == 0) throw new InvalidDataException("Native global material animation is missing.");
        var originals = source.Tables(material, 0, 128);
        var slots = writer.Vector(material, 0, materialNames.Count);
        for (var i = 0; i < materialNames.Count; i++)
        {
            var name = materialNames[i]; var matches = originals.Where(t => source.Text(t, 0) == (name.Contains("eye", StringComparison.Ordinal) ? "Eye" : "Body01Vco")).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Native global material animation target is ambiguous.");
            var original = matches[0]; var copy = writer.Append(nativeAnimation); var target = copy + original;
            writer.Pointer(slots[i], target); writer.Text(target, 0, name);
            foreach (var scalar in source.Tables(original, 2, 256))
            {
                var parameter = source.Text(scalar, 0);
                var value = parameter switch
                {
                    "ColorUVScaleU" or "ColorUVScaleV" or "NormalMapUVScaleU" or "NormalMapUVScaleV" => 1f,
                    "ColorBaseU" or "ColorBaseV" or "ColorUVTranslateU" or "ColorUVTranslateV" => 0f,
                    _ => (float?)null,
                };
                if (value is null) continue;
                var kindAt = source.Field(scalar, 1);
                if (kindAt == 0 || source.U8(kindAt) != 1) throw new InvalidDataException("Native global UV parameter is not constant.");
                var curve = source.Table(scalar, 2); var field = source.Field(curve, 0);
                if (field == 0)
                {
                    var replacement = writer.Table(1, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Floats(value.Value) });
                    writer.Reference(copy + scalar, 2, replacement);
                }
                else writer.Single(copy + field, value.Value);
            }
        }
        return writer.Finish(source.Root);
    }
}
