// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.Models;

namespace KM.SwSh.FpsPatch;

internal static partial class SwShFpsStadiumAudiencePatcher
{
    private static int ExpandMonitorTrack(ModelBuffer data, int track, int kind,
        int sourceFrames, int targetFrames, List<byte> output)
    {
        var value = data.Table(track, 2);
        Require(value != 0);
        var width = kind == 3 ? 2 : 1;
        var (keys, count) = data.Vector(value, 0, width, sourceFrames);
        var (values, valueCount) = data.Vector(value, 1, 4, sourceFrames);
        Require(count > 0 && count == valueCount);
        var frames = new int[count];
        for (var index = 0; index < count; index++)
        {
            frames[index] = width == 2 ? data.U16(keys + index * 2) : data.U8(keys + index);
            var sample = data.Float(values + index * 4);
            Require(frames[index] < sourceFrames && float.IsFinite(sample));
            if (index == 0)
                Require(frames[index] == 0);
            else
            {
                var gap = frames[index] - frames[index - 1];
                // Atlas changes occupy one original frame. Equal keys and holds
                // are valid; a wider changing segment would require interpolation.
                Require(gap >= 0 && (sample == data.Float(values + (index - 1) * 4) || gap == 1));
            }
        }

        // Append a new dense scalar table, leaving shared source tables intact.
        while (output.Count % 4 != 0) output.Add(0);
        output.AddRange([6, 0, 8, 0, 4, 0, 0, 0]);
        var table = output.Count;
        output.AddRange([8, 0, 0, 0, 4, 0, 0, 0]);
        BeginVector(output, targetFrames);
        var key = 0;
        for (var frame = 0; frame < sourceFrames; frame++)
        {
            while (key + 1 < count && frames[key + 1] <= frame) key++;
            var sample = data.Slice(values + key * 4, 4).ToArray();
            output.AddRange(sample);
            if (frame + 1 < sourceFrames) output.AddRange(sample);
        }
        return table;
    }
}
