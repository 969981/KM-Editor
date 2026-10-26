// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private (IReadOnlyList<SwShRestorationContribution> Contributions, int[] ExcludedMoves) PlaGameplay(SwShPokemonIdentity identity)
        {
            var species = identity.Species; var profile = Profile(identity);
            if (identity.Form != 0 || species is not (46 or 47)) throw new InvalidDataException("PLA gameplay identity is not registered.");
            var personalId = SwShRestorationForms.PersonalRow(identity);
            int Find(ModelBuffer data) => data.Tables(data.Root, 0, 4096).Single(t =>
                data.Field(t, 0) is var at && at != 0 && data.U16(at) == species
                && data.Field(t, 1) is var form && (form == 0 || data.U16(form) == 0));
            var source = new ModelBuffer(Donor("bin/pml/personal/personal_data_total.perbin"));
            var table = Find(source);
            int Scalar(int field, bool wide = false) => source.Field(table, field) is var at && at != 0
                ? wide ? source.U16(at) : source.U8(at) : 0;
            if (Scalar(2) != 1 || Scalar(14) != 127 || Scalar(43, true) != 46)
                throw new InvalidDataException("PLA gameplay identity or breeding data changed.");
            var nativePersonal = Native(SwShRestorationComposer.PersonalPath[6..]);
            var personal = SwShPersonalTable.Parse(nativePersonal, 4096);
            if (personalId >= personal.Records.Count || personal.Records[personalId].IsPresentInGame)
                throw new InvalidDataException("The PLA restoration destination is occupied.");
            var before = nativePersonal.AsSpan(personalId * 176, 176).ToArray(); var row = before.ToArray();
            int[] stats = [8, 9, 10, 13, 11, 12]; int[] yields = [32, 33, 34, 37, 35, 36];
            var ev = 0;
            for (var i = 0; i < 6; i++)
            {
                row[i] = checked((byte)Scalar(stats[i])); var value = Scalar(yields[i]);
                if (value > 3) throw new InvalidDataException("PLA EV yield exceeds native storage.");
                ev |= value << (i * 2);
            }
            row[6] = checked((byte)Scalar(3)); row[7] = checked((byte)Scalar(4));
            // PLA catch values use its action capture system. Restore the conventional rates.
            row[8] = species == 46 ? (byte)190 : (byte)75;
            row[9] = checked((byte)Scalar(16)); Put16(row, 10, ev); row.AsSpan(12, 6).Clear();
            row[18] = 127; row[19] = checked((byte)Scalar(47)); row[20] = checked((byte)Scalar(48));
            row[21] = checked((byte)Scalar(15)); row[22] = checked((byte)Scalar(41)); row[23] = checked((byte)Scalar(42));
            for (var i = 0; i < 3; i++)
            {
                var ability = Scalar(5 + i, true);
                if (ability is < 1 or > 267) throw new InvalidDataException("PLA ability needs another native adapter.");
                Put16(row, 24 + i * 2, ability);
            }
            Put16(row, 30, 0); row[32] = 1; row[33] = 66;
            Put16(row, 34, Scalar(31, true)); Put16(row, 36, Scalar(20, true)); Put16(row, 38, Scalar(21, true));
            var excluded = new HashSet<int>();
            bool Available(int move)
            {
                var path = $"bin/pml/waza/waza{move:0000}.wazabin";
                if (move is < 1 or > 826 || !System.IO.File.Exists(Path.Combine(target.BaseRomFsPath!, path))
                    || !SwShMoveDataFile.Parse(Native(path)).Record.CanUseMove)
                { excluded.Add(move); return false; }
                return true;
            }
            var tutors = new ModelBuffer(Donor("bin/appli/wazaremember/bin/wazashop_table.bin"));
            var machineMoves = new HashSet<int>(); var indices = new HashSet<int>();
            foreach (var tutor in tutors.Tables(tutors.Root, 0, 128))
            {
                var index = checked((int)tutors.Value(tutor, 0)); var move = checked((int)tutors.Value(tutor, 1));
                if (index is < 1 or > 61 || !indices.Add(index)) throw new InvalidDataException("PLA tutor identity is invalid.");
                var bits = source.Value(table, 56 + (index - 1) / 32);
                if (((bits >> ((index - 1) % 32)) & 1) != 0 && Available(move)) machineMoves.Add(move);
            }
            if (indices.Count != 61) throw new InvalidDataException("PLA tutor catalog is incomplete.");
            void Compatibility(int offset, int length, string group)
            {
                var moves = SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(group);
                row.AsSpan(offset, length).Clear();
                if (moves.Count > length * 8) throw new InvalidDataException("Native compatibility field is too small.");
                for (var i = 0; i < moves.Count; i++) if (machineMoves.Contains(moves[i])) row[offset + i / 8] |= (byte)(1 << (i % 8));
            }
            Compatibility(40, 16, SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId);
            Compatibility(56, 4, SwShPokemonWorkflowService.TypeTutorCompatibilityGroupId);
            Compatibility(60, 16, SwShPokemonWorkflowService.TechnicalRecordCompatibilityGroupId);
            Compatibility(168, 4, SwShPokemonWorkflowService.ArmorTutorCompatibilityGroupId);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(76), checked((uint)species));
            Put16(row, 86, 46); row.AsSpan(88, 8).Clear(); row.AsSpan(172, 4).Clear();
            _ = SwShPersonalTable.Parse(row);
            var result = new List<SwShRestorationContribution>
            { new(SwShRestorationComposer.PersonalPath, SwShRestorationWriter.Personal, personalId.ToString(CultureInfo.InvariantCulture), before, row) };
            var learns = new ModelBuffer(Donor("bin/pml/waza_oboe/waza_oboe_total.wazaoboe"));
            var entries = learns.Tables(Find(learns), 3, 65); var learned = new List<(int Move, int Level)>();
            foreach (var entry in entries)
            {
                int Value(int field) => learns.Field(entry, field) is var at && at != 0 ? learns.U16(at) : 0;
                var level = Value(0); var move = Value(2);
                if (level > 100) throw new InvalidDataException("PLA learnset level is unsupported.");
                if (Available(move) && !learned.Contains((move, level))) learned.Add((move, level));
            }
            if (learned.Count >= 65) throw new InvalidDataException("PLA learnset exceeds native storage.");
            var learnset = Enumerable.Repeat((byte)255, 260).ToArray();
            for (var i = 0; i < learned.Count; i++) { Put16(learnset, i * 4, learned[i].Move); Put16(learnset, i * 4 + 2, learned[i].Level); }
            var learnNative = Native(SwShRestorationComposer.LearnsetPath[6..]);
            if (learnNative.Length != personal.Records.Count * 260) throw new InvalidDataException("Native personal and learnset counts disagree.");
            result.Add(new(SwShRestorationComposer.LearnsetPath, SwShRestorationWriter.Learnset, personalId.ToString(CultureInfo.InvariantCulture), learnNative.AsSpan(personalId * 260, 260).ToArray(), learnset));
            var evolutions = new ModelBuffer(Donor("bin/pml/evolution/evolution_data_total.evobin")); var evolution = new byte[72];
            var evolutionRows = evolutions.Tables(Find(evolutions), 2, 8);
            for (var i = 0; i < evolutionRows.Length; i++)
            {
                var entry = evolutionRows[i];
                int Value(int field) => evolutions.Field(entry, field) is var at && at != 0 ? evolutions.U16(at) : 0;
                if (species != 46 || Value(0) != 4 || Value(1) != 0 || Value(2) != 47 || Value(3) != 0 || Value(4) != 24)
                    throw new InvalidDataException("PLA evolution needs another native adapter.");
                Put16(evolution, i * 8, 4); Put16(evolution, i * 8 + 4, 47); evolution[i * 8 + 7] = 24;
            }
            result.Add(File($"bin/pml/evolution/evo_{personalId:000}.bin", evolution));
            // PLA has no egg move lists; retain an explicit empty list, not another species' moves.
            var eggs = new byte[4]; Put16(eggs, 0, personalId);
            result.Add(File($"bin/pml/tamagowaza/tamagowaza_{personalId:0000}.bin", eggs));
            var icon = new byte[8]; Put16(icon, 0, species);
            result.Add(new(SwShRestorationComposer.IconsPath, SwShRestorationWriter.Icons, SwShRestorationComposer.IconKey(species, 0), null, icon));
            var camp = SwShGfPackFile.Parse(Native(SwShRestorationComposer.CampPath[6..]));
            foreach (var group in new[] { "common", "motion", "face", "ball", "turning" })
            {
                string Name(int id) => group == "common" ? $"poke_{id:0000}_XX_X.bin" : $"poke_{group}_{id:0000}_00_0.bin";
                var folder = $"bin/pokemon_data/pokecamp/{group}/";
                var bytes = camp.ReadFileByHash(SwShGfPackFile.HashMemberPath(folder + Name(profile.NativeTemplate)), 1024 * 1024);
                if (group == "common")
                {
                    var metadata = new ModelBuffer(bytes); var at = metadata.Field(metadata.Root, 2);
                    if (metadata.U32(at) != profile.NativeTemplate) throw new InvalidDataException("Native camp template identity is invalid.");
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), checked((uint)species));
                }
                result.Add(new(SwShRestorationComposer.CampPath, SwShRestorationWriter.Camp, folder + Name(species), null, bytes));
            }
            return (result, excluded.Order().ToArray());
        }
    }
}
