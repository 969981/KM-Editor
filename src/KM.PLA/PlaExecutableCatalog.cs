// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text.Json;
using KM.Core.Projects;
using KM.Formats.Executable;

namespace KM.PLA;

internal sealed class PlaExecutableCatalog(PlaCatalog catalog)
{
    private const int ChartOffset = 0x6DE908;
    private const int TypeCount = 18;
    private const int ChartLength = TypeCount * TypeCount;
    private (NsoFile Base, NsoFile Current)? resource;
    private sealed record Guard(bool Text, int Offset, int Length, string Hash);
    private static readonly Guard[] Guards =
    [
        new(true, 0x283E1F0, 0x9D4, "4DD642B26F18443FF8EDAAAA63F6952354AE4808598539F121866D6ECD2FA753"),
        new(true, 0x286F000, 0xDA4, "6DDCB64DE6AEF03044C3C4A17CFDF46C358160A9CC8EBAF82243AB84EA8C86B6"),
        new(true, 0x2939300, 0x2D8, "3E5C33ECCD3FA70C408E57AFD5D3D5FD63C20F3982DC32EAD29C4FEB06EDC423"),
        new(false, 0x6D89F0, 0xD8, "124DC4B8C5EC1A810AC20C36CEA708835404225EAF007560FB56F0FDDD2C28BE"),
        new(false, 0x6DA2D8, 0x18, "8A4223448DA221FCFE92F195E873A31843334CA3387B96C3C10A292ACB30D25E"),
        new(false, ChartOffset - 64, 64, "27424230D26B6ED3F6238F7BF5D0407E73F6D18A77B245E6751E370637875B99"),
        new(false, ChartOffset + ChartLength, 64, "06186BFC467C0E5E00BE834E5475503F7F55743029F34FF1FC369AEB31447D0F"),
    ];

    internal static bool Supports(string kind) => kind == "typeChart";
    internal static PlaField[] Fields => [new("entries", "Type Chart", "rows")];
    internal static string PathFor(string kind, string id) => Supports(kind) && id == kind
        ? PlaSourceScope.ExecutablePath : throw new InvalidDataException("The PLA executable editor identity is invalid.");

    private static NsoFile Parse(byte[] bytes, bool inspectionOnly = false)
    {
        if (!PlaProjectProfile.Matches(bytes) || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 0)
            throw new InvalidDataException("PLA Type Chart requires the supported version 1.1.1 executable.");
        // Bound decompression before parsing. File positions may change after recompression;
        // the mapped addresses and segment sizes must retain this build's layout.
        var expected = new[] { (0, 53106320), (53108736, 14104576), (67215360, 2568224) };
        for (var index = 0; index < expected.Length; index++)
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x14 + 16 * index)) != expected[index].Item1
                || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x18 + 16 * index)) != expected[index].Item2)
                throw new InvalidDataException("The PLA executable segment layout is unsupported.");
        var nso = inspectionOnly ? NsoFile.Inspect(bytes) : NsoFile.Parse(bytes);
        foreach (var segment in nso.Segments)
            if (!NsoFile.ComputeHash(segment.DecompressedData).AsSpan().SequenceEqual(segment.Hash))
                throw new InvalidDataException("The PLA executable segment checksum is invalid.");
        // Preserve the table consumers, their dispatch tables, and the combined damage rules.
        foreach (var guard in Guards)
            if (PlaSourceScope.Hash((guard.Text ? nso.Text : nso.Ro).DecompressedData.AsSpan(guard.Offset, guard.Length)) != guard.Hash)
                throw new InvalidDataException("PLA Type Chart conflicts with modified matchup code or protected table data.");
        foreach (var value in nso.Ro.DecompressedData.AsSpan(ChartOffset, ChartLength))
            if (value is not (0 or 2 or 4 or 8))
                throw new InvalidDataException("The PLA type table contains an unsupported effectiveness value.");
        return nso;
    }

    private (NsoFile Base, NsoFile Current) Load()
    {
        if (resource is { } cached) return cached;
        var original = catalog.Scope.Base(PlaSourceScope.ExecutablePath);
        var effective = catalog.Scope.Read(PlaSourceScope.ExecutablePath);
        var baseline = Parse(original, inspectionOnly: true);
        var current = ReferenceEquals(original, effective) ? baseline : Parse(effective, inspectionOnly: true);
        NsoExecutableCompatibility.EnsureCompatibleBaseLayout(baseline, current, "PLA Type Chart");
        resource = (baseline, current);
        return resource.Value;
    }

    private static ushort[][] Rows(NsoFile nso) => Enumerable.Range(0, TypeCount)
        .Select(row => nso.Ro.DecompressedData.AsSpan(ChartOffset + row * TypeCount, TypeCount).ToArray().Select(value => (ushort)value).ToArray()).ToArray();

    internal PlaRecord Record(string kind, string id, byte[]? edited = null)
    {
        _ = PathFor(kind, id);
        var source = Load();
        var current = edited is null ? source.Current : Parse(edited, inspectionOnly: true);
        return new(id, "Type Chart", new Dictionary<string, string> { ["entries"] = JsonSerializer.Serialize(Rows(current)) })
        {
            Metadata = new Dictionary<string, string>
            {
                ["buildId"] = Convert.ToHexString(current.BuildId), ["offset"] = ".ro+0x6DE908",
                ["typeNames"] = JsonSerializer.Serialize(catalog.TypeChoices.Select(choice => choice.Label)),
            },
        };
    }

    internal IReadOnlyList<PlaRecord> Records(string kind, IReadOnlyDictionary<string, byte[]> files) =>
        [Record(kind, kind, files.GetValueOrDefault(PathFor(kind, kind)))];

    internal byte[] Edit(string kind, string id, byte[] bytes, IReadOnlyDictionary<string, string> values)
    {
        _ = PathFor(kind, id);
        _ = Load();
        if (values.Count != 1 || !values.TryGetValue("entries", out var value))
            throw new InvalidDataException("The PLA type chart edit is invalid.");
        var rows = JsonSerializer.Deserialize<ushort[][]>(value);
        if (rows is null || rows.Length != TypeCount || rows.Any(row => row is null || row.Length != TypeCount || row.Any(v => v is not (0 or 2 or 4 or 8))))
            throw new InvalidDataException("PLA Type Chart requires 18 rows of 18 immune, resisted, neutral, or super effective values.");
        var current = Parse(bytes);
        var chart = rows.SelectMany(row => row).Select(v => (byte)v).ToArray();
        if (current.Ro.DecompressedData.AsSpan(ChartOffset, ChartLength).SequenceEqual(chart)) return bytes;
        var ro = current.Ro.DecompressedData.ToArray();
        chart.CopyTo(ro, ChartOffset);
        var result = current.Write(roDecompressedData: ro);
        var written = Parse(result, inspectionOnly: true);
        NsoExecutableCompatibility.EnsureCompatibleBaseLayout(current, written, "PLA Type Chart");
        if (!current.Text.CompressedData.AsSpan().SequenceEqual(written.Text.CompressedData)
            || !current.Data.CompressedData.AsSpan().SequenceEqual(written.Data.CompressedData)
            || !ro.AsSpan().SequenceEqual(written.Ro.DecompressedData))
            throw new InvalidDataException("The PLA executable did not preserve its unedited data.");
        return result;
    }
}
