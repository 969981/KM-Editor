// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    public static byte[] Visibility(byte[] animation, IReadOnlyList<PreviewVisibility> tracks, IReadOnlySet<string> meshNames)
    {
        var source = new ModelBuffer(animation);
        var frames = checked((int)source.Value(source.Table(source.Root, 0), 1));
        if (frames is < 1 or > 18000 || tracks.Count > 128 || tracks.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
            throw new InvalidDataException("Mesh visibility exceeds native limits.");
        var writer = new ModelTableBuilder(); var root = writer.Table(5,
            absent: source.Table(source.Root, 4) == 0 ? new HashSet<int> { 4 } : null);
        var copy = writer.Append(animation);
        for (var field = 0; field < 3; field++) writer.Reference(root, field, copy + source.Table(source.Root, field));
        if (source.Table(source.Root, 4) is var metadata && metadata != 0)
            writer.Reference(root, 4, copy + metadata);
        var visibility = writer.Table(1); writer.Reference(root, 3, visibility);
        var slots = writer.Vector(visibility, 0, tracks.Count);
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i]; ValidateKeys(track.Keys);
            if (track.Keys.Length == 0 || !meshNames.Contains(track.Name) || track.Keys[0].Frame != 0
                || track.Keys.Any(k => k.Frame >= frames || k.Value[0] is not (0 or 1)))
                throw new InvalidDataException("Mesh visibility has invalid targets or samples.");
            var constant = track.Keys.All(k => k.Value[0] == track.Keys[0].Value[0]);
            var target = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
            writer.Pointer(slots[i], target); writer.Text(target, 0, track.Name);
            var curve = writer.Table(1); writer.Reference(target, 2, curve);
            if (constant) writer.Int32(writer.Field(curve, 0), (int)track.Keys[0].Value[0]);
            else
            {
                var packed = new byte[(frames + 7) / 8]; var key = 0;
                for (var frame = 0; frame < frames; frame++)
                {
                    while (key + 1 < track.Keys.Length && track.Keys[key + 1].Frame <= frame) key++;
                    if (track.Keys[key].Value[0] != 0) packed[frame / 8] |= (byte)(1 << (frame % 8));
                }
                writer.Blob(curve, 0, packed, packed.Length);
            }
        }
        return writer.Finish(root);
    }
}
