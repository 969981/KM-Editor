// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal (IReadOnlyList<SwShRestorationContribution> Contributions, int[] ExcludedMoves) Gameplay(int species)
        {
            var source = new ModelBuffer(Donor("avalon/data/personal_array.bin"));
            var candidates = source.Tables(source.Root, 0, 4096).Where(t => source.Field(t, 0) is var at && at != 0
                && source.U16(at + 4) == species && source.U16(at + 2) == 0).ToArray();
            if (candidates.Length != 1) throw new InvalidDataException("Source Pokemon gameplay identity is missing or ambiguous.");
            var table = candidates[0]; var detail = source.Field(table, 0);
            int Scalar(int field, bool wide = false) => source.Field(table, field) is var at && at != 0 ? wide ? source.U16(at) : source.U8(at) : 0;
            int[] Moves(int field)
            {
                var (start, count) = source.Vector(table, field, 2, 1024);
                return Enumerable.Range(0, count).Select(i => (int)source.U16(start + i * 2)).ToArray();
            }
            var nativePersonal = Native(SwShRestorationComposer.PersonalPath[6..]);
            var personalTable = SwShPersonalTable.Parse(nativePersonal, 4096);
            if (species >= personalTable.Records.Count || personalTable.Records[species].IsPresentInGame)
                throw new InvalidDataException("Restoration target is already native or has no personal slot.");
            var before = nativePersonal.AsSpan(species * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize).ToArray(); var row = before.ToArray();
            var stats = source.Field(table, 22); var yields = source.Field(table, 21); int[] order = [0, 1, 2, 5, 3, 4];
            for (var i = 0; i < order.Length; i++) row[i] = source.U8(stats + order[i]);
            row[6] = checked((byte)Scalar(5)); row[7] = checked((byte)Scalar(6)); row[8] = checked((byte)Scalar(11)); row[9] = checked((byte)Scalar(19));
            var ev = 0;
            for (var i = 0; i < order.Length; i++) { var value = source.U8(yields + order[i]); if (value > 3) throw new InvalidDataException("Source EV yield is unsupported."); ev |= value << (i * 2); }
            Put16(row, 10, ev); row.AsSpan(12, 6).Clear(); row[18] = 31; row[19] = checked((byte)Scalar(16)); row[20] = checked((byte)Scalar(17));
            row[21] = checked((byte)Scalar(10)); row[22] = checked((byte)Scalar(13)); row[23] = checked((byte)Scalar(14));
            for (var i = 0; i < 3; i++) { var ability = Scalar(7 + i, true); if (ability is < 1 or > 267) throw new InvalidDataException("Source ability needs a target mechanics adapter."); Put16(row, 24 + i * 2, ability); }
            Put16(row, 30, 0); row[32] = 1; row[33] = (byte)((source.U8(detail + 6) & 63) | 64);
            Put16(row, 34, new[] { 63, 142, 265 }[species - 393]); Put16(row, 36, source.U16(detail + 8)); Put16(row, 38, source.U16(detail + 10));
            var machineMoves = Moves(24).ToHashSet();
            void Compatibility(int offset, int length, IEnumerable<int> moves)
            {
                row.AsSpan(offset, length).Clear(); var index = 0;
                foreach (var move in moves) { if (machineMoves.Contains(move)) row[offset + index / 8] |= (byte)(1 << (index % 8)); index++; }
                if (index > length * 8) throw new InvalidDataException("Compatibility field exceeds native storage.");
            }
            Compatibility(40, 16, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId));
            Compatibility(56, 4, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.TypeTutorCompatibilityGroupId));
            Compatibility(60, 16, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.TechnicalRecordCompatibilityGroupId));
            Compatibility(168, 4, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.ArmorTutorCompatibilityGroupId));
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(76), checked((uint)species)); Put16(row, 86, 393); row.AsSpan(88, 8).Clear(); row.AsSpan(172, 4).Clear();
            _ = SwShPersonalTable.Parse(row);
            var result = new List<SwShRestorationContribution>
            { new(SwShRestorationComposer.PersonalPath, SwShRestorationWriter.Personal, species.ToString(CultureInfo.InvariantCulture), before, row) };
            var excluded = new HashSet<int>();
            bool MoveAvailable(int move)
            {
                if (move is < 1 or > 826 || !System.IO.File.Exists(Path.Combine(target.BaseRomFsPath!, $"bin/pml/waza/waza{move:0000}.wazabin")))
                { excluded.Add(move); return false; }
                return true;
            }
            var learned = Moves(26).Where(MoveAvailable).Select(m => (Move: m, Level: 1)).ToList();
            var (learnStart, learnCount) = source.Vector(table, 27, 4, 1024);
            for (var i = 0; i < learnCount; i++)
            {
                var move = source.U16(learnStart + i * 4); var level = Math.Max(0, (int)unchecked((sbyte)source.U8(learnStart + i * 4 + 2)));
                if (MoveAvailable(move) && !learned.Contains((move, level))) learned.Add((move, level));
            }
            if (learned.Count >= 65) throw new InvalidDataException("Converted learnset exceeds native storage.");
            var learnset = Enumerable.Repeat((byte)255, 260).ToArray();
            for (var i = 0; i < learned.Count; i++) { Put16(learnset, i * 4, learned[i].Move); Put16(learnset, i * 4 + 2, learned[i].Level); }
            var learnNative = Native(SwShRestorationComposer.LearnsetPath[6..]);
            if (learnNative.Length / 260 != personalTable.Records.Count || learnNative.Length % 260 != 0) throw new InvalidDataException("Target learnset and personal identities disagree.");
            result.Add(new(SwShRestorationComposer.LearnsetPath, SwShRestorationWriter.Learnset, species.ToString(CultureInfo.InvariantCulture), learnNative.AsSpan(species * 260, 260).ToArray(), learnset));
            var (evolutionStart, evolutionCount) = source.Vector(table, 23, 16, 9); var evolution = new byte[72];
            for (var i = 0; i < evolutionCount; i++)
            {
                var at = evolutionStart + i * 16; var destination = source.U16(at + 12); var form = source.U16(at + 14);
                if (destination is < 393 or > 395 || form != 0) throw new InvalidDataException("Evolution needs an undeclared restoration dependency.");
                Put16(evolution, i * 8, source.U16(at + 2)); Put16(evolution, i * 8 + 2, source.U16(at + 4)); Put16(evolution, i * 8 + 4, destination);
                evolution[i * 8 + 6] = checked((byte)form); evolution[i * 8 + 7] = checked((byte)source.U16(at));
            }
            result.Add(File($"bin/pml/evolution/evo_{species:000}.bin", evolution));
            var eggMoves = Moves(25).Where(MoveAvailable).ToArray(); var eggs = new byte[4 + eggMoves.Length * 2]; Put16(eggs, 0, species); Put16(eggs, 2, eggMoves.Length);
            for (var i = 0; i < eggMoves.Length; i++) Put16(eggs, 4 + i * 2, eggMoves[i]);
            result.Add(File($"bin/pml/tamagowaza/tamagowaza_{species:0000}.bin", eggs));
            var icon = new byte[8]; Put16(icon, 0, species);
            result.Add(new(SwShRestorationComposer.IconsPath, SwShRestorationWriter.Icons, species.ToString(CultureInfo.InvariantCulture), null, icon));
            var camp = SwShGfPackFile.Parse(Native(SwShRestorationComposer.CampPath[6..]));
            foreach (var group in new[] { "common", "motion", "face", "ball", "turning" })
            {
                string Name(int id) => group == "common" ? $"poke_{id:0000}_XX_X.bin" : $"poke_{group}_{id:0000}_00_0.bin";
                var folder = $"bin/pokemon_data/pokecamp/{group}/";
                var data = camp.ReadFileByHash(SwShGfPackFile.HashMemberPath(folder + Name(species - 386)), 1024 * 1024);
                if (group == "common")
                {
                    var metadata = new ModelBuffer(data); var field = metadata.Field(metadata.Root, 2);
                    if (metadata.U32(field) != species - 386) throw new InvalidDataException("Native camp donor identity is invalid.");
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field), checked((uint)species));
                }
                result.Add(new(SwShRestorationComposer.CampPath, SwShRestorationWriter.Camp, folder + Name(species), null, data));
            }
            return (result, excluded.Order().ToArray());
        }
        private static void Put16(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), checked((ushort)value));
    }
}
