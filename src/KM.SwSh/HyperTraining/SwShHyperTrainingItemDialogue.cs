// SPDX-License-Identifier: GPL-3.0-only
using System.Text.RegularExpressions;
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.HyperTraining;

internal static class SwShHyperTrainingItemDialogue
{
    private static readonly IReadOnlyDictionary<string, (string Missing, string Choose)> Templates =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["English"] = ("You need one of these items:", "Choose a training item:"),
            ["Spanish"] = ("Necesitas uno de estos objetos:", "Elige un objeto para entrenar:"),
            ["French"] = ("Il te faut un de ces objets :", "Choisis un objet pour l’entraînement :"),
            ["German"] = ("Du brauchst eines dieser Items:", "Wähle ein Item für das Training:"),
            ["Italian"] = ("Ti serve uno di questi strumenti:", "Scegli uno strumento per allenarti:"),
            ["JPN"] = ("つぎの　どうぐが　ひつようです：", "とっくんに　つかう　どうぐを　えらんでください："),
            ["JPN_KANJI"] = ("次の道具が必要です：", "特訓に使う道具を選んでください："),
            ["Korean"] = ("다음 도구 중 하나가 필요합니다:", "훈련에 사용할 도구를 선택하세요:"),
            ["Simp_Chinese"] = ("需要以下道具之一：", "请选择训练使用的道具："),
            ["Trad_Chinese"] = ("需要以下道具之一：", "請選擇訓練使用的道具："),
        };

    internal static IEnumerable<string> Targets(OpenedProject project) => Templates.Keys
        .Select(language => $"romfs/bin/message/{language}/script/sub_event_007.dat")
        .Where(path => SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(project.Paths, path) is not null);
    internal static string NamesPath(string path) => path.Replace("/script/sub_event_007.dat", "/common/itemname.dat", StringComparison.Ordinal);

    internal static bool HasGeneratedDialogue(OpenedProject project) => Targets(project).Any(path =>
    {
        var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path);
        if (source?.Entry.LayeredFile is null) return false;
        var lines = SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath)).Lines;
        var templates = Templates[path.Split('/')[3]];
        return lines.Count > 5 && (IsGenerated(lines[1].Text, templates.Missing) || IsGenerated(lines[5].Text, templates.Choose));
    });

    internal static byte[] Apply(byte[] source, byte[] baseline, byte[]? names, string path, SwShHyperTrainingItems items)
    {
        var current = SwShGameTextFile.Parse(source);
        var original = SwShGameTextFile.Parse(baseline);
        if (current.Lines.Count != original.Lines.Count || current.Lines.Count <= 5)
            throw new InvalidDataException("Hyper Training payment dialogue is incompatible.");
        var language = path.Split('/')[3];
        var templates = Templates[language];
        var itemNames = names is null ? null : SwShGameTextFile.Parse(names).Lines;
        var lines = current.Lines.ToArray();
        foreach (var (index, prefix) in new[] { (1, templates.Missing), (5, templates.Choose) })
        {
            if (lines[index].Text != original.Lines[index].Text && !IsGenerated(lines[index].Text, prefix))
                throw new InvalidDataException("Hyper Training payment dialogue contains conflicting edits.");
            var text = items == SwShHyperTrainingItemsPatcher.Defaults ? original.Lines[index].Text
                : $"{prefix}\\n{Name(items.BottleCapItemId)}\\n{Name(items.GoldBottleCapItemId)}";
            lines[index] = lines[index] with { Text = text };
        }
        return current.WritePreserving(lines);
        string Name(int item) => itemNames is not null && item < itemNames.Count && !string.IsNullOrWhiteSpace(itemNames[item].Text)
            ? itemNames[item].Text : throw new InvalidDataException("Hyper Training payment item names are unavailable.");
    }

    private static bool IsGenerated(string value, string prefix) => Regex.IsMatch(value,
        "\\A" + Regex.Escape(prefix) + @"\\n[^\r\n]+\\n[^\r\n]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
