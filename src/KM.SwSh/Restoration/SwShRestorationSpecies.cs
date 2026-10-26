// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Explicit logical species identities whose base records use reserved storage.</summary>
public static class SwShRestorationSpecies
{
    public const int Annihilape = 979;
    public const int AnnihilapeRow = 1081;
    public static IEnumerable<int> BaseSpecies => Enumerable.Range(1, SwShEffectivePokemonCatalog.MaximumSpecies).Append(Annihilape);
    public static bool IsSpecies(int species) => species is >= 1 and <= SwShEffectivePokemonCatalog.MaximumSpecies or Annihilape;
    public static int BaseRow(int species) => species == Annihilape ? AnnihilapeRow
        : species is >= 1 and <= SwShEffectivePokemonCatalog.MaximumSpecies ? species
        : throw new InvalidDataException("The logical Pokemon species is not registered.");
    public static int? SpeciesAtReservedRow(int row) => row == AnnihilapeRow ? Annihilape : null;

    public static void ValidateReservation(byte[] native, byte[] current)
    {
        if (native.Length != 1192 * SwShPersonalTable.RecordSize || current.Length != native.Length)
            throw new InvalidDataException("The new species reservation requires the supported native personal layout.");
        foreach (var data in new[] { native, current })
        {
            var records = SwShPersonalTable.Parse(data, 1192).Records;
            for (var species = 1; species <= SwShEffectivePokemonCatalog.MaximumSpecies; species++)
            {
                var record = records[species];
                if (record.FormStatsIndex != 0 && record.FormCount > 1 && record.FormStatsIndex <= AnnihilapeRow
                    && record.FormStatsIndex + record.FormCount - 2 >= AnnihilapeRow)
                    throw new InvalidDataException("Another Pokemon claims the reserved new species row.");
            }
        }
        var before = native.AsSpan(AnnihilapeRow * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize);
        if (Convert.ToHexString(SHA256.HashData(before)) != "E26B3853F98CF2778D4A9C63AD53CDA32C92DA679A5E4666E5D13E3991A051BF")
            throw new InvalidDataException("The reserved new species row is occupied in Base RomFS.");
        var row = current.AsSpan(AnnihilapeRow * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize);
        if (!row.SequenceEqual(before) && (BinaryPrimitives.ReadUInt32LittleEndian(row[76..]) != Annihilape
            || (row[33] & 64) == 0 || row[32] != 1 || BinaryPrimitives.ReadUInt16LittleEndian(row[30..]) != 0))
            throw new InvalidDataException("The reserved new species row contains foreign data.");
    }
}
