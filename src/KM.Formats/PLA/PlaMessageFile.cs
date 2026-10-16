// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Formats.PLA;

/// <summary>PLA admission around the shared encrypted message primitive.</summary>
public sealed class PlaMessageFile
{
    private readonly SwShGameTextFile file;
    public IReadOnlyList<SwShGameTextLine> Lines => file.Lines;
    public IReadOnlyList<SwShAhtbEntry> Keys { get; }

    public PlaMessageFile(ReadOnlySpan<byte> data, ReadOnlySpan<byte> keys)
    {
        file = SwShGameTextFile.Parse(data, 65535);
        var table = SwShAhtbFile.Parse(keys);
        Keys = table.Entries;
        if (Keys.Count != Lines.Count && !(Keys.Count == Lines.Count + 1 && Keys[^1].Name.EndsWith("_MAX", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The PLA message keys do not match the text lines.");
    }

    public byte[] Write(IReadOnlyDictionary<int, string> changes)
    {
        var lines = Lines.ToArray();
        foreach (var (index, text) in changes)
        {
            if (index < 0 || index >= lines.Length || text.Length > 32768)
                throw new InvalidDataException("The PLA text edit is outside the supported bounds.");
            lines[index] = lines[index] with { Text = text };
        }
        return file.WritePreserving(lines);
    }
}
