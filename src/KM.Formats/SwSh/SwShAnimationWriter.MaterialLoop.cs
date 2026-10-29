// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    /// <summary>Copies the reachable material-loop graph without resampling its encoded curves.</summary>
    public static byte[] CompactMaterialLoop(byte[] animation)
    {
        var source = new ModelBuffer(animation);
        var writer = new ModelTableBuilder();
        HashSet<int> Absent(int table, int count)
        {
            var fields = (source.U16(table - source.I32(table)) - 4) / 2;
            if (Enumerable.Range(count, Math.Max(0, fields - count)).Any(f => source.Field(table, f) != 0))
                throw new InvalidDataException("Material loop contains an unsupported table field.");
            return Enumerable.Range(0, count).Where(f => source.Field(table, f) == 0).ToHashSet();
        }
        var rootAbsent = Absent(source.Root, 5);
        if (!rootAbsent.Contains(1) || !rootAbsent.Contains(3) || !rootAbsent.Contains(4)
            || rootAbsent.Contains(0) || rootAbsent.Contains(2))
            throw new InvalidDataException("Material loop compaction requires timing and material tracks only.");
        var root = writer.Table(5, absent: rootAbsent);
        var info = source.Table(source.Root, 0);
        var infoAbsent = Absent(info, 3);
        writer.Reference(root, 0, writer.Table(3, Enumerable.Range(0, 3).Where(f => !infoAbsent.Contains(f))
            .ToDictionary(f => f, f => source.Slice(source.Field(info, f), 4).ToArray()), absent: infoAbsent));
        var parent = source.Table(source.Root, 2);
        var parentAbsent = Absent(parent, 1);
        var targetParent = writer.Table(1, absent: parentAbsent);
        writer.Reference(root, 2, targetParent);
        if (!parentAbsent.Contains(0))
        {
            var materials = source.Tables(parent, 0, 128);
            var slots = writer.Vector(targetParent, 0, materials.Length);
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i]; var absent = Absent(material, 4);
                var target = writer.Table(4, absent: absent); writer.Pointer(slots[i], target);
                if (!absent.Contains(0)) writer.Text(target, 0, source.Text(material, 0)!);
                for (var field = 1; field <= 3; field++)
                {
                    if (absent.Contains(field)) continue;
                    var tracks = source.Tables(material, field, 256);
                    var trackSlots = writer.Vector(target, field, tracks.Length);
                    for (var j = 0; j < tracks.Length; j++)
                    {
                        var track = tracks[j]; var trackAbsent = Absent(track, 3);
                        var kind = trackAbsent.Contains(1) ? (byte)0 : source.U8(source.Field(track, 1));
                        if (kind > 4 || kind == 0 && !trackAbsent.Contains(2) || kind != 0 && trackAbsent.Contains(2))
                            throw new InvalidDataException("Material loop curve encoding is unsupported.");
                        var targetTrack = writer.Table(3, trackAbsent.Contains(1) ? null : new Dictionary<int, byte[]> { [1] = [kind] }, absent: trackAbsent);
                        writer.Pointer(trackSlots[j], targetTrack);
                        if (!trackAbsent.Contains(0)) writer.Text(targetTrack, 0, source.Text(track, 0)!);
                        if (kind == 0) continue;
                        var curve = source.Table(track, 2); var count = kind < 3 ? 1 : 2;
                        var curveAbsent = Absent(curve, count);
                        var stride = field == 1 ? 1 : field == 2 ? 4 : 12;
                        var targetCurve = writer.Table(count, sizes: kind == 1 ? new Dictionary<int, int> { [0] = Math.Max(4, stride) } : null, absent: curveAbsent);
                        writer.Reference(targetTrack, 2, targetCurve);
                        for (var c = 0; c < count; c++)
                        {
                            if (curveAbsent.Contains(c)) continue;
                            if (kind == 1) writer.Write(writer.Field(targetCurve, c), source.Slice(source.Field(curve, c), stride));
                            else
                            {
                                var width = kind >= 3 && c == 0 ? kind == 3 ? 2 : 1 : stride;
                                var (at, length) = source.Vector(curve, c, width, 18000);
                                writer.Blob(targetCurve, c, source.Slice(at, checked(length * width)), length);
                            }
                        }
                    }
                }
            }
        }
        var result = writer.Finish(root);
        return result.Length < animation.Length ? result : animation;
    }
}
