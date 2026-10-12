// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;

namespace KM.SwSh.Trades;

internal sealed class SwShTradePresentation(OpenedProject project)
{
    internal const ulong EmptyHash = 0xCBF29CE484222645;
    internal const string SourceInvalidCode = "KM-SWSH-TRADE-PRESENTATION-SOURCE-INVALID";
    internal const string TextPreservedCode = "KM-SWSH-TRADE-PRESENTATION-TEXT-PRESERVED";
    internal const string NicknameInvalidCode = "KM-SWSH-TRADE-NICKNAME-INVALID";
    internal SwShSceneSources Files { get; } = new(project);
    internal List<ValidationDiagnostic> Diagnostics { get; } = [];
    private static string TextPath(string language) => SwShGameTextLanguage.ScriptMessagePath(language, "field_trade.dat");
    private static string NicknameKey(ulong identity) => $"km_trade_{identity:x16}_nickname";
    private static string OfferKey(ulong identity) => $"km_trade_{identity:x16}_offer";

    internal static bool ValidNickname(string? value) => value is not null && value.Length <= 12
        && (value.Length == 0 || !string.IsNullOrWhiteSpace(value))
        && !value.Any(character => char.IsControl(character) || character is '[' or ']' or '\\' || char.IsSurrogate(character));

    internal static IReadOnlyDictionary<ulong, string> ReadNicknames(OpenedProject project)
    {
        var files = new SwShSceneSources(project);
        var path = TextPath(SwShGameTextLanguage.Resolve(project.Paths));
        if (!files.Contains(path)) path = TextPath("English");
        if (!files.Contains(path)) return new Dictionary<ulong, string>();
        var text = SwShGameTextFile.Parse(files.Read(path));
        var keys = SwShAhtbFile.Parse(files.Read(Path.ChangeExtension(path, ".tbl"))).Entries;
        return keys.Take(text.Lines.Count).Select((key, index) => (key.Hash, text.Lines[index].Text))
            .GroupBy(row => row.Hash).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().Text);
    }

    internal byte[] Build(string tablePath, byte[] numericTable, IReadOnlyList<PendingEdit> edits)
    {
        var current = SwShTradePokemonArchive.Parse(Files.Read(tablePath));
        var vanilla = SwShTradePokemonArchive.Parse(Files.Read(tablePath, true));
        var archive = SwShTradePokemonArchive.Parse(numericTable);
        var touched = edits.Where(edit => edit.Field is "species" or "form" or "requiredSpecies" or "requiredForm" or "nickname")
            .GroupBy(edit => SwShTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var index) ? index
                : throw new InvalidDataException("Trade presentation record is unavailable.")).ToArray();
        var languages = SwShGameTextLanguage.SupportedMessageLanguages.Where(language => Files.Contains(TextPath(language))).ToArray();
        if (languages.Length == 0) throw new InvalidDataException("Trade dialogue is unavailable.");
        var nicknameHashes = new Dictionary<int, ulong>();
        foreach (var group in touched)
        {
            var row = archive.Trades.Single(row => row.Index == group.Key);
            var previous = current.Trades.Single(record => record.Hash0 == row.Hash0);
            var original = vanilla.Trades.Single(record => record.Hash0 == row.Hash0);
            var nicknameEdit = group.LastOrDefault(edit => edit.Field == "nickname");
            if (nicknameEdit is not null)
            {
                var nickname = nicknameEdit.NewValue!;
                if (!ValidNickname(nickname)) throw new InvalidDataException("Trade nickname must contain at most 12 supported characters.");
                nicknameHashes[row.Index] = nickname.Length == 0 ? EmptyHash : SwShGfPackFile.HashFnv1a64(NicknameKey(row.Hash0));
                if (nickname.Length > 0) SetMessage(languages, NicknameKey(row.Hash0), languages.ToDictionary(language => language, _ => nickname));
            }

            // Fixed NPC trades have six messages followed by a nickname and trainer name.
            var baseKeys = SwShAhtbFile.Parse(Files.Read(Path.ChangeExtension(TextPath(languages[0]), ".tbl"), true)).Entries;
            var nicknameIndex = baseKeys.ToList().FindIndex(key => key.Hash == original.Hash1);
            if (original.Hash1 != EmptyHash && nicknameIndex >= 6)
            {
                foreach (var language in languages)
                {
                    var path = TextPath(language);
                    var (source, baseText) = Files.ReadTextPair(path);
                    var lines = source.Lines.ToArray();
                    foreach (var offset in new[] { 0, 1, 2, 3, 4 })
                    {
                        var index = nicknameIndex - 6 + offset;
                        string Generated(SwShTradePokemonRecord value, bool nicknameChanged)
                            => SameIdentity(value, original) && !nicknameChanged ? baseText.Lines[index].Text
                                : CreateText(language, Name(language, value.Species, value.Form),
                                    Name(language, value.RequiredSpecies, value.RequiredForm), offset);
                        var changedNickname = nicknameEdit is not null || row.Hash1 != original.Hash1;
                        var wanted = Generated(row, changedNickname);
                        var old = Generated(previous, previous.Hash1 != original.Hash1);
                        if (lines[index].Text != wanted && lines[index].Text != baseText.Lines[index].Text && lines[index].Text != old)
                        {
                            Diagnostics.Add(new(DiagnosticSeverity.Warning,
                                "Custom trade dialogue was preserved. Review this line in Text if it names the previous Pokemon.",
                                path, SwShTradePokemonWorkflowService.TradePokemonEditDomain, $"line:{index}") { Code = TextPreservedCode });
                            continue;
                        }
                        lines[index] = lines[index] with { Text = wanted };
                    }
                    Files.Outputs[path] = source.WritePreserving(lines);
                }
            }
            else if (original.Hash1 == EmptyHash)
            {
                // Regina's offer is selected by trade identity in the script's initialized table.
                // Give each trade its own message, including the two entries sharing one stock offer.
                const string scriptPath = "romfs/bin/script/amx/field_trade.amx";
                var document = SwShAmxDocument.Parse(Files.Read(scriptPath));
                var baseDocument = SwShAmxDocument.Parse(Files.Read(scriptPath, true));
                var matches = Enumerable.Range(0, 12).Select(index => 3328 + 24 * index)
                    .Where(address => document.DataCells.Count > address / 8 + 1 && unchecked((ulong)document.DataCells[address / 8]) == row.Hash0).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Regina's trade offer table has an unsupported layout.");
                var address = matches[0] + 8;
                var originalHash = unchecked((ulong)baseDocument.DataCells[address / 8]);
                var sourceHash = unchecked((ulong)document.DataCells[address / 8]);
                var key = OfferKey(row.Hash0);
                var targetHash = SwShGfPackFile.HashFnv1a64(key);
                if (sourceHash != originalHash && sourceHash != targetHash)
                    throw new InvalidDataException("Regina's trade offer reference was changed by another script edit.");
                var values = new Dictionary<string, string>();
                foreach (var language in languages)
                {
                    var (source, baseText) = Files.ReadTextPair(TextPath(language));
                    var index = baseKeys.ToList().FindIndex(entry => entry.Hash == originalHash);
                    if (index < 0 || index >= baseText.Lines.Count) throw new InvalidDataException("Regina's offer message is missing.");
                    var wanted = CreateText(language, Name(language, row.Species, row.Form), Name(language, row.RequiredSpecies, row.RequiredForm), 0);
                    var old = CreateText(language, Name(language, previous.Species, previous.Form), Name(language, previous.RequiredSpecies, previous.RequiredForm), 0);
                    var keys = SwShAhtbFile.Parse(Files.Read(Path.ChangeExtension(TextPath(language), ".tbl"))).Entries;
                    var activeIndex = keys.ToList().FindIndex(entry => entry.Hash == sourceHash);
                    if (activeIndex < 0 || activeIndex >= source.Lines.Count) throw new InvalidDataException("Regina's current offer message is missing.");
                    var active = source.Lines[activeIndex].Text;
                    if (active != baseText.Lines[index].Text && active != old && active != wanted)
                    {
                        wanted = active;
                        Diagnostics.Add(new(DiagnosticSeverity.Warning, "Custom trade dialogue was preserved. Review this offer in Text.",
                            TextPath(language), SwShTradePokemonWorkflowService.TradePokemonEditDomain, $"line:{activeIndex}") { Code = TextPreservedCode });
                    }
                    values.Add(language, wanted);
                }
                SetMessage(languages, key, values);
                foreach (var language in languages)
                {
                    var path = TextPath(language);
                    var (source, baseText) = Files.ReadTextPair(path);
                    var keys = SwShAhtbFile.Parse(Files.Read(Path.ChangeExtension(path, ".tbl"))).Entries;
                    var index = keys.ToList().FindIndex(entry => entry.Name == "msg_field_trade_wr02_04");
                    if (index < 0 || index >= baseText.Lines.Count) throw new InvalidDataException("Regina's completion message is missing.");
                    var wanted = CreateText(language, "", "", 6);
                    if (source.Lines[index].Text == baseText.Lines[index].Text || source.Lines[index].Text == wanted)
                    {
                        var lines = source.Lines.ToArray();
                        lines[index] = lines[index] with { Text = wanted };
                        Files.Outputs[path] = source.WritePreserving(lines);
                    }
                }
                var assembler = document.CreateAssembler();
                assembler.ReplaceDataCell(address, sourceHash, targetHash);
                Files.Outputs[scriptPath] = assembler.Assemble();
            }
            else throw new InvalidDataException("This trade has no supported dialogue mapping.");
        }
        return archive.WriteEdits([], nicknameHashes);
    }

    private void SetMessage(string[] languages, string key, IReadOnlyDictionary<string, string> values)
    {
        var hash = SwShGfPackFile.HashFnv1a64(key);
        var pairs = languages.Select(language => new CoordinatedGameTextSource(language, Files.Read(TextPath(language)),
            Files.Read(Path.ChangeExtension(TextPath(language), ".tbl")))).ToArray();
        var present = pairs.Select(pair => SwShAhtbFile.Parse(pair.Keys).Entries.Where(entry => entry.Hash == hash || entry.Name == key).ToArray()).ToArray();
        if (present.All(entries => entries.Length == 0))
        {
            foreach (var output in CoordinatedGameTextAuthoring.Insert(pairs, languages, key, values, GameTextNullLineEncoding.LegacyCountOne))
            {
                Files.Outputs[TextPath(output.Language)] = output.Data;
                Files.Outputs[Path.ChangeExtension(TextPath(output.Language), ".tbl")] = output.Keys;
            }
            return;
        }
        if (present.Any(entries => entries.Length != 1 || entries[0].Hash != hash || entries[0].Name != key))
            throw new InvalidDataException("Trade message identity is missing or ambiguous in one of the game languages.");
        foreach (var pair in pairs)
        {
            var source = SwShGameTextFile.Parse(pair.Data);
            var keys = SwShAhtbFile.Parse(pair.Keys).Entries;
            var index = keys.ToList().FindIndex(entry => entry.Hash == hash);
            if (index >= source.Lines.Count) throw new InvalidDataException("Trade message has no text row.");
            var lines = source.Lines.ToArray();
            lines[index] = lines[index] with { Text = values[pair.Language] };
            Files.Outputs[TextPath(pair.Language)] = source.WritePreserving(lines);
        }
    }

    private string Name(string language, int species, int form)
    {
        if (species == 0) return language switch
        {
            "JPN" or "JPN_KANJI" => "ポケモン", "Korean" => "포켓몬", "Simp_Chinese" => "宝可梦", "Trad_Chinese" => "寶可夢", _ => "Pokémon",
        };
        var names = SwShGameTextFile.Parse(Files.Read(SwShGameTextLanguage.CommonMessagePath(language, "monsname.dat"))).Lines;
        if (species < 1 || species >= names.Count) throw new InvalidDataException("Trade Pokemon has no localized species name.");
        var path = SwShGameTextLanguage.CommonMessagePath(language, "zkn_form.dat");
        var forms = SwShGameTextFile.Parse(Files.Read(path)).Lines;
        var keys = SwShAhtbFile.Parse(Files.Read(Path.ChangeExtension(path, ".tbl"))).Entries;
        var index = keys.ToList().FindIndex(entry => entry.Name == $"ZKN_FORM_{species:000}_{form:000}");
        var label = index >= 0 && index < forms.Count ? forms[index].Text : "";
        if (form > 0 && string.IsNullOrWhiteSpace(label)) label = $"{form}";
        return names[species].Text + (string.IsNullOrWhiteSpace(label) ? "" : $" ({label})");
    }

    private static bool SameIdentity(SwShTradePokemonRecord left, SwShTradePokemonRecord right) =>
        (left.Species, left.Form, left.RequiredSpecies, left.RequiredForm) == (right.Species, right.Form, right.RequiredSpecies, right.RequiredForm);

    private static string CreateText(string language, string received, string requested, int kind)
    {
        if (kind is 2 or 6) return (language, kind) switch
        {
            ("English", 2) => "Come back if you change your mind.", ("English", _) => "Thank you! Let's trade again sometime!",
            ("Spanish", 2) => "Vuelve si cambias de opinión.", ("Spanish", _) => "¡Gracias! ¡Volvamos a intercambiar otro día!",
            ("French", 2) => "Reviens si tu changes d’avis.", ("French", _) => "Merci ! On refera un échange une autre fois !",
            ("German", 2) => "Komm wieder, wenn du es dir anders überlegst.", ("German", _) => "Danke! Lass uns bald wieder tauschen!",
            ("Italian", 2) => "Torna se cambi idea.", ("Italian", _) => "Grazie! Scambiamo ancora un’altra volta!",
            ("JPN" or "JPN_KANJI", 2) => "きが　かわったら　またきてね。", ("JPN" or "JPN_KANJI", _) => "ありがとう！　また　こうかんしよう！",
            ("Korean", 2) => "마음이 바뀌면 다시 와.", ("Korean", _) => "고마워! 다음에 또 교환하자!",
            ("Simp_Chinese", 2) => "改变主意的话就再来吧。", ("Simp_Chinese", _) => "谢谢！下次再来交换吧！",
            ("Trad_Chinese", 2) => "改變主意的話就再來吧。", ("Trad_Chinese", _) => "謝謝！下次再來交換吧！",
            _ => throw new InvalidDataException("Trade dialogue language is unsupported."),
        };
        var phrases = language switch
        {
            "English" => new[] { $"Would you trade your {requested}\\nfor my {received}?", $"Trade {requested} for {received}?", $"Thank you for the trade!\\nPlease take care of {received}.", $"How is {received} doing?" },
            "Spanish" => new[] { $"¿Cambias tu {requested}\\npor mi {received}?", $"¿Cambiar {requested} por {received}?", $"¡Gracias por el intercambio!\\nCuida mucho de {received}.", $"¿Cómo está {received}?" },
            "French" => new[] { $"Veux-tu échanger ton {requested}\\ncontre mon {received} ?", $"Échanger {requested} contre {received} ?", $"Merci pour l’échange !\\nPrends bien soin de {received}.", $"Comment va {received} ?" },
            "German" => new[] { $"Tauschst du dein {requested}\\ngegen mein {received}?", $"{requested} gegen {received} tauschen?", $"Danke für den Tausch!\\nKümmere dich gut um {received}.", $"Wie geht es {received}?" },
            "Italian" => new[] { $"Scambi il tuo {requested}\\ncon il mio {received}?", $"Scambiare {requested} con {received}?", $"Grazie per lo scambio!\\nPrenditi cura di {received}.", $"Come sta {received}?" },
            "JPN" or "JPN_KANJI" => new[] { $"きみの　{requested}と\\nわたしの　{received}を　こうかんしよう！", $"{requested}と　{received}を　こうかんする？", $"こうかんしてくれて　ありがとう！\\n{received}を　よろしくね。", $"{received}は　げんき？" },
            "Korean" => new[] { $"너의 {requested}, 나의 {received},\\n서로 교환할래?", $"{requested}, {received}, 교환할까?", $"교환해 줘서 고마워!\\n{received}, 잘 부탁해.", $"{received}, 잘 지내고 있어?" },
            "Simp_Chinese" => new[] { $"要用你的{requested}\\n交换我的{received}吗？", $"用{requested}交换{received}吗？", $"谢谢你和我交换！\\n请好好照顾{received}。", $"{received}还好吗？" },
            "Trad_Chinese" => new[] { $"要用你的{requested}\\n交換我的{received}嗎？", $"用{requested}交換{received}嗎？", $"謝謝你和我交換！\\n請好好照顧{received}。", $"{received}還好嗎？" },
            _ => throw new InvalidDataException("Trade dialogue language is unsupported."),
        };
        return phrases[kind switch { 0 => 0, 1 => 1, 3 => 2, _ => 3 }];
    }
}
