// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Formats.PLA;

namespace KM.PLA;

internal sealed class PlaBattleCatalog(PlaCatalog catalog)
{
    private const string GiftPath = "bin/pokemon/data/poke_add.bin";
    private const string StaticPath = "bin/pokemon/data/poke_event_encount.bin";
    private static readonly int[] TrainerWidths = Enumerable.Range(0, 17).Select(i => i >= 15 ? 1 : 4).ToArray();
    private static readonly int[] GiftWidths = Enumerable.Range(0, 34).Select(i => i is 10 or 12 or 14 or 16 or 18 ? 1 : 4).ToArray();
    private static readonly int[] StaticWidths = Enumerable.Range(0, 39).Select(i => i is 9 or 10 or 11 or 17 or 19 or 21 or 23 or 25 ? 1 : 4).ToArray();
    private static readonly IReadOnlyDictionary<int, int[]> MoveChildren = new Dictionary<int, int[]>
    { [2] = [4, 1], [3] = [4, 1], [4] = [4, 1], [5] = [4, 1] };
    private static readonly string[] EffortKeys = ["effortHp", "effortAttack", "effortDefense", "effortSpecialAttack", "effortSpecialDefense", "effortSpeed"];
    private static readonly HashSet<int> LegendarySpecies = [480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, 641, 642, 645, 905];
    private static readonly HashSet<string> BossForms = ["900:1", "101:2", "59:2", "549:2", "713:2"];
    private static readonly HashSet<string> QuestBattles = ["hikozaru_01", "skunpuu01", "yukikaburi01", "yukimenoko01", "aipom01", "yukinooh01", "kabaldon01", "perap01", "paras01", "onigohri01", "lucario01", "usokkie01", "ringuma02", "beequen01", "iwaak01"];
    private sealed record Entry(string Path, int Reference, string Key, int Slot, int Count, string Identity, bool Protected, bool FixedIdentity);
    private readonly Dictionary<string, Dictionary<string, Entry>> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlaFlatBuffer> buffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlaField[]> fields = new(StringComparer.Ordinal);

    internal static bool Supports(string kind) => kind is "trainers" or "gifts" or "static";
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static bool Special(string identity) => LegendarySpecies.Contains(int.Parse(identity.Split(':')[0], CultureInfo.InvariantCulture)) || BossForms.Contains(identity);
    private static string Identity(PlaFlatBuffer buffer, int reference, string kind)
    {
        var table = buffer.TableAtReference(reference);
        return $"{buffer.Scalar(table, kind == "gifts" ? 1 : 0, 4)}:{buffer.Scalar(table, kind == "gifts" ? 2 : 1, 4)}";
    }

    internal PlaField[] Fields(string kind) => fields.TryGetValue(kind, out var value) ? value : fields[kind] = BuildFields(kind);
    private PlaField[] BuildFields(string kind) => [
        new("species", "species", Choices: catalog.SpeciesChoices.Where(choice => !LegendarySpecies.Contains(int.Parse(choice.Value, CultureInfo.InvariantCulture))).ToArray()),
        catalog.FormField(id => !Special(id)),
        new("battleLevel", "battleLevel", Minimum: 1, Maximum: 100),
        .. kind == "trainers" ? new PlaField[] { new("gender", "gender", Choices: [new("0", "Male / genderless"), new("1", "Female")]) } : [],
        .. EffortKeys.Select(key => new PlaField(key, key, Minimum: 0, Maximum: 10)),
        .. Enumerable.Range(1, 4).SelectMany(slot => new PlaField[] {
            new($"move{slot}", $"move{slot}", Choices: [new("0", "0"), .. catalog.MoveChoices]),
            new($"mastered{slot}", $"mastered{slot}", "boolean", 0, 1),
        }),
    ];

    private Dictionary<string, Entry> Entries(string kind)
    {
        if (entries.TryGetValue(kind, out var result)) return result;
        result = new(StringComparer.Ordinal);
        var paths = kind == "trainers" ? catalog.Scope.TrainerFiles() : [kind == "gifts" ? GiftPath : StaticPath];
        foreach (var path in paths)
        {
            var original = new PlaFlatBuffer(catalog.Scope.Base(path));
            var current = new PlaFlatBuffer(catalog.Scope.Read(path));
            buffers.Add(path, current);
            if (kind == "trainers")
            {
                var key = Path.GetFileNameWithoutExtension(path);
                AddParty(key, original.VectorReferences(original.Root, 22, 6), current.VectorReferences(current.Root, 22, 6));
            }
            else
            {
                var originalRows = NamedRows(original);
                var currentRows = NamedRows(current);
                if (!originalRows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(currentRows.Keys))
                    throw new InvalidDataException("The PLA event record identities changed. Existing record membership must be preserved.");
                foreach (var (key, reference) in originalRows)
                {
                    var now = currentRows[key];
                    AddParty(key, kind == "gifts" ? [reference] : original.VectorReferences(original.TableAtReference(reference), 3, 6),
                        kind == "gifts" ? [now] : current.VectorReferences(current.TableAtReference(now), 3, 6));
                }
            }

            void AddParty(string key, int[] baseParty, int[] party)
            {
                if (baseParty.Length == 0 || baseParty.Length != party.Length)
                    throw new InvalidDataException("PLA battle party sizes and slot order must be preserved.");
                for (var slot = 0; slot < party.Length; slot++)
                {
                    var identity = Identity(original, baseParty[slot], kind);
                    var isProtected = Special(identity) || !catalog.Pokemon.ContainsKey(identity) || kind switch
                    {
                        "trainers" => !key.StartsWith("trdata_trainer", StringComparison.Ordinal) && !key.StartsWith("trdata_single_", StringComparison.Ordinal),
                        "gifts" => key != "event_092",
                        _ => !key.StartsWith("mut_", StringComparison.Ordinal) && !QuestBattles.Contains(key),
                    };
                    var fixedIdentity = kind != "trainers" || !key.StartsWith("trdata_trainer", StringComparison.Ordinal);
                    var id = key + ":" + slot.ToString(CultureInfo.InvariantCulture);
                    if (!result.TryAdd(id, new(path, party[slot], key, slot, party.Length, identity, isProtected, fixedIdentity)))
                        throw new InvalidDataException("The PLA battle identity is ambiguous.");
                }
            }
        }
        entries.Add(kind, result);
        return result;
    }

    private static Dictionary<string, int> NamedRows(PlaFlatBuffer buffer)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var reference in buffer.VectorReferences(buffer.Root, 0, 1024))
        {
            var key = buffer.String(buffer.TableAtReference(reference), 0, 256);
            if (string.IsNullOrWhiteSpace(key) || key.Contains(':') || !result.TryAdd(key, reference))
                throw new InvalidDataException("The PLA event key is invalid or ambiguous.");
        }
        return result;
    }

    private Entry Find(string kind, string id) => Entries(kind).GetValueOrDefault(id) ?? throw new InvalidDataException("The PLA battle record is unavailable.");
    internal IEnumerable<string> Ids(string kind) => Entries(kind).Keys;
    internal (string Path, PlaFlatBuffer Buffer, int Reference) Locate(string kind, string id)
    {
        var entry = Find(kind, id);
        return (entry.Path, buffers[entry.Path], entry.Reference);
    }

    private static int Reference(PlaFlatBuffer buffer, string kind, Entry entry)
    {
        var party = kind == "trainers" ? buffer.VectorReferences(buffer.Root, 22, 6) : Array.Empty<int>();
        if (kind != "trainers")
        {
            if (!NamedRows(buffer).TryGetValue(entry.Key, out var parent)) throw new InvalidDataException("The PLA event key is missing.");
            party = kind == "gifts" ? [parent] : buffer.VectorReferences(buffer.TableAtReference(parent), 3, 6);
        }
        if (party.Length != entry.Count) throw new InvalidDataException("The PLA party membership changed.");
        return party[entry.Slot];
    }

    private static Dictionary<string, int> Indices(string kind)
    {
        var trainer = kind == "trainers";
        var gift = kind == "gifts";
        var result = new Dictionary<string, int>(StringComparer.Ordinal) {
            ["species"] = gift ? 1 : 0, ["form"] = gift ? 2 : 1, ["battleLevel"] = trainer ? 6 : gift ? 5 : 4,
        };
        if (trainer) result.Add("gender", 8);
        var effort = trainer ? new[] { 9, 10, 11, 13, 14, 12 } : Enumerable.Range(gift ? 26 : 33, 6).ToArray();
        for (var i = 0; i < EffortKeys.Length; i++) result.Add(EffortKeys[i], effort[i]);
        if (!trainer)
            for (var i = 1; i <= 4; i++)
            {
                result.Add($"move{i}", (gift ? 11 : 18) + (i - 1) * 2);
                result.Add($"mastered{i}", (gift ? 12 : 19) + (i - 1) * 2);
            }
        return result;
    }

    internal PlaRecord Record(string kind, string id, byte[]? edited = null)
    {
        var entry = Find(kind, id);
        var buffer = edited is null ? buffers[entry.Path] : new PlaFlatBuffer(edited);
        var reference = edited is null ? entry.Reference : Reference(buffer, kind, entry);
        var table = buffer.TableAtReference(reference);
        var widths = kind == "trainers" ? TrainerWidths : kind == "gifts" ? GiftWidths : StaticWidths;
        var values = Indices(kind).ToDictionary(pair => pair.Key, pair => Number(buffer.Scalar(table, pair.Value, widths[pair.Value], widths[pair.Value] == 4)), StringComparer.Ordinal);
        if (kind == "trainers")
            for (var slot = 1; slot <= 4; slot++)
            {
                var pointer = buffer.Field(table, slot + 1, 4);
                if (pointer == 0) throw new InvalidDataException("The PLA trainer move slot is missing.");
                var move = buffer.TableAtReference(pointer);
                values.Add($"move{slot}", Number(buffer.Scalar(move, 0, 4, true)));
                values.Add($"mastered{slot}", Number(buffer.Scalar(move, 1, 1)));
            }
        return new(id, $"{entry.Key} [{entry.Slot + 1}/{entry.Count}]: {catalog.PokemonLabel(values["species"] + ":" + values["form"])}", values)
        { ReadOnlyFields = entry.Protected ? values.Keys.ToArray() : entry.FixedIdentity ? ["species", "form"] : [] };
    }

    internal byte[] Edit(string kind, string id, byte[] bytes, IReadOnlyDictionary<string, string> values)
    {
        var entry = Find(kind, id);
        var record = Record(kind, id, bytes);
        if (values.Keys.Any(record.ReadOnlyFields.Contains)) throw new InvalidDataException("This PLA event or battle field is protected.");
        var combined = record.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in values) combined[pair.Key] = pair.Value;
        foreach (var field in Fields(kind).Where(field => field.Minimum is not null || field.Maximum is not null))
            if (!long.TryParse(combined[field.Key], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
                || value < field.Minimum || value > field.Maximum)
                throw new InvalidDataException("The PLA battle level or effort level is outside its supported range.");
        var identity = combined["species"] + ":" + combined["form"];
        if (!catalog.Pokemon.ContainsKey(identity) || Special(identity) || entry.FixedIdentity && identity != entry.Identity)
            throw new InvalidDataException("This PLA battle requires a supported Pokemon form and its protected event identity.");
        if (kind == "trainers")
        {
            var gender = combined["gender"];
            var ratio = catalog.Personal.Scalar(catalog.Personal.TableAtReference(catalog.Pokemon[identity]), 14, 1);
            if (gender is not ("0" or "1") || ratio is 0 or 255 && gender != "0" || ratio == 254 && gender != "1")
                throw new InvalidDataException("Select a gender supported by this PLA Pokemon form.");
        }
        var selectedMoves = new HashSet<int>();
        for (var slot = 1; slot <= 4; slot++)
        {
            var move = int.Parse(combined[$"move{slot}"], CultureInfo.InvariantCulture);
            if (combined[$"mastered{slot}"] is not ("0" or "1") || (move == 0 ? combined[$"mastered{slot}"] != "0" : !catalog.Moves.ContainsKey(move) || !selectedMoves.Add(move)))
                throw new InvalidDataException("PLA battle moves must be usable and distinct, and an empty move cannot be mastered.");
        }
        if (values.All(pair => record.Values[pair.Key] == pair.Value)) return bytes;
        var buffer = new PlaFlatBuffer(bytes);
        var reference = Reference(buffer, kind, entry);
        var indices = Indices(kind);
        var scalars = values.Where(pair => indices.ContainsKey(pair.Key)).ToDictionary(pair => indices[pair.Key], pair => long.Parse(pair.Value, CultureInfo.InvariantCulture));
        buffer.SetObjectScalars(reference, kind == "trainers" ? TrainerWidths : kind == "gifts" ? GiftWidths : StaticWidths, scalars,
            kind == "gifts" ? [0] : kind == "static" ? [13, 14, 15] : [], kind == "trainers" ? MoveChildren : new Dictionary<int, int[]>());
        if (kind == "trainers")
            for (var slot = 1; slot <= 4; slot++)
            {
                var moves = new Dictionary<int, long>();
                if (values.TryGetValue($"move{slot}", out var move)) moves[0] = long.Parse(move, CultureInfo.InvariantCulture);
                if (values.TryGetValue($"mastered{slot}", out var mastered)) moves[1] = long.Parse(mastered, CultureInfo.InvariantCulture);
                if (moves.Count != 0) buffer.SetScalars(buffer.Field(buffer.TableAtReference(reference), slot + 1, 4), [4, 1], moves);
            }
        return buffer.ToArray();
    }
}
