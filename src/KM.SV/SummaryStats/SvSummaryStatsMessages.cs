// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.SV.Data;

namespace KM.SV.SummaryStats;

internal sealed record SvSummaryStatsMessageResult(byte[] Data, byte[] Keys, bool PreservedCustomText);

internal static class SvSummaryStatsMessages
{
    internal static readonly string[] Keys = ["km_ui_summary_stats", "km_ui_summary_raw_ivs", "km_ui_summary_evs", "km_ui_summary_ev_graph"];
    internal static readonly IReadOnlyDictionary<string, string[]> Captions = new Dictionary<string, string[]>
    {
        ["English"] = ["Stats", "Raw IVs", "EVs", "EV Graph"],
        ["Spanish"] = ["Estadísticas", "IV originales", "PE", "Gráfico de PE"],
        ["French"] = ["Statistiques", "IV bruts", "EV", "Graphe des EV"],
        ["German"] = ["Werte", "Ursprüngliche DV", "FP", "FP Grafik"],
        ["Italian"] = ["Statistiche", "IV originali", "EV", "Grafico EV"],
        ["JPN"] = ["のうりょく", "こたいち", "どりょくち", "どりょくちグラフ"],
        ["JPN_KANJI"] = ["能力", "個体値", "努力値", "努力値グラフ"],
        ["Korean"] = ["능력치", "개체값", "노력치", "노력치 그래프"],
        ["Simp_Chinese"] = ["能力值", "原始个体值", "努力值", "努力值图表"],
        ["Trad_Chinese"] = ["能力值", "原始個體值", "努力值", "努力值圖表"],
    };

    internal static IReadOnlyList<string> VirtualPaths { get; } = SvGameTextLanguage.SupportedMessageLanguages
        .SelectMany(language => new[] { $"message/dat/{language}/common/status.dat", $"message/dat/{language}/common/status.tbl" }).ToArray();

    internal static void ValidateBaseKeys(byte[] keys)
    {
        if (SwShAhtbFile.Parse(keys).Entries.Any(entry => Keys.Contains(entry.Name, StringComparer.Ordinal)))
            throw new InvalidDataException("Base Summary messages already contain reserved mode keys.");
    }

    internal static (byte[] Data, byte[] Keys) RepairMissingCompanion(string language,
        byte[] data, byte[] keys, byte[] baseKeys, bool dataIsBase, bool keysAreBase)
    {
        var rows = SwShGameTextFile.Parse(data);
        var names = SwShAhtbFile.Parse(keys);
        var namedRows = names.Entries.Count - (names.Entries.Count > 0 && names.Entries[^1].Name.EndsWith("_max", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        if (rows.Lines.Count == names.Entries.Count || rows.Lines.Count == namedRows) return (data, keys);
        if (dataIsBase && !keysAreBase)
        {
            var originals = SwShAhtbFile.Parse(baseKeys);
            if (originals.Entries.Count < rows.Lines.Count) throw new InvalidDataException("Base Summary messages are not paired.");
            var values = originals.Entries.Take(rows.Lines.Count).Select((entry, index) => (entry.Name, Row: rows.Lines[index]))
                .ToDictionary(pair => pair.Name, pair => pair.Row, StringComparer.Ordinal);
            for (var mode = 0; mode < Keys.Length; mode++) values.Add(Keys[mode], new(Captions[language][mode], 0));
            if (names.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != names.Entries.Count
                || names.Entries.Take(namedRows).Any(entry => !values.ContainsKey(entry.Name)))
                throw new InvalidDataException("Missing Summary text includes unrecognized keys.");
            return (SwShGameTextFile.Write(names.Entries.Take(namedRows).Select(entry => values[entry.Name]).ToArray(), GameTextNullLineEncoding.PayloadCountTwo), keys);
        }
        if (keysAreBase && !dataIsBase && rows.Lines.Count == namedRows + Keys.Length)
        {
            // Only the exact appended captions prove the identity of a deleted key table.
            for (var mode = 0; mode < Keys.Length; mode++)
                if (rows.Lines[namedRows + mode] != new SwShGameTextLine(Captions[language][mode], 0))
                    throw new InvalidDataException("Missing Summary keys cannot be recovered from customized captions.");
            var originalData = SwShGameTextFile.Write(rows.Lines.Take(namedRows).ToArray(), GameTextNullLineEncoding.PayloadCountTwo);
            return (data, Transform(language, originalData, keys, true).Keys);
        }
        throw new InvalidDataException("Summary messages are not paired.");
    }

    internal static SvSummaryStatsMessageResult Transform(string language, byte[] dataBytes, byte[] keyBytes, bool enabled)
    {
        if (!Captions.TryGetValue(language, out var captions)) throw new InvalidDataException("Unsupported Summary message language.");
        var preserved = false;
        // Reverse removal retains ordinary rows and any later independent appended keys.
        foreach (var mode in enabled ? Enumerable.Range(0, Keys.Length) : Enumerable.Range(0, Keys.Length).Reverse())
        {
            var data = SwShGameTextFile.Parse(dataBytes);
            var keys = SwShAhtbFile.Parse(keyBytes);
            var matches = keys.Entries.Select((entry, index) => (entry, index)).Where(pair => pair.entry.Name == Keys[mode]).ToArray();
            if (matches.Length > 1 || matches.Length == 1 && matches[0].index >= data.Lines.Count)
                throw new InvalidDataException("A Summary caption key is ambiguous or has no message.");
            if (matches.Length == 1)
            {
                var row = data.Lines[matches[0].index];
                if (row.Text != captions[mode] || row.Flags != 0) { preserved = true; continue; }
                if (enabled) continue;
            }
            else if (!enabled) continue;
            CoordinatedGameTextSource[] sources = [new(language, dataBytes, keyBytes)];
            var output = enabled
                ? CoordinatedGameTextAuthoring.Insert(sources, [language], Keys[mode],
                    new Dictionary<string, string> { [language] = captions[mode] }, GameTextNullLineEncoding.PayloadCountTwo)[0]
                : CoordinatedGameTextAuthoring.Delete(sources, [language], Keys[mode], GameTextNullLineEncoding.PayloadCountTwo)[0];
            dataBytes = output.Data;
            keyBytes = output.Keys;
        }
        return new(dataBytes, keyBytes, preserved);
    }
}
