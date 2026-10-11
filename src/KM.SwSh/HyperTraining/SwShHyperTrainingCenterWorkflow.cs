// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.HyperTraining;

internal static class SwShHyperTrainingCenterWorkflow
{
    internal sealed record Output(string Path, byte[] Bytes, byte[] Baseline);
    private const string EnglishKeys = "romfs/bin/message/English/script/sub_event_007.tbl";
    internal static string CenterText(string hyperPath) => hyperPath.Replace("sub_event_007.dat", "sub_event_129.dat", StringComparison.Ordinal);
    private static byte[] Base(OpenedProject project, string path) => File.ReadAllBytes(
        SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(project.Paths, path)
        ?? throw new InvalidDataException($"Pokemon Center Hyper Training needs the original {path}."));
    private static byte[] Source(OpenedProject project, string path) => File.ReadAllBytes(
        SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path)?.AbsolutePath
        ?? throw new InvalidDataException($"Pokemon Center Hyper Training needs {path}."));

    internal static bool IsEnabled(OpenedProject project)
    {
        var script = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingCenterPatcher.ScriptPath);
        if (script?.Entry.LayeredFile is not null)
        {
            try
            {
                var document = SwShAmxDocument.Parse(File.ReadAllBytes(script.AbsolutePath));
                if (document.Instructions.SelectMany(instruction => instruction.Operands).Any(operand =>
                    operand.Kind == SwShAmxOperandKind.Literal
                    && unchecked((ulong)operand.LiteralValue) == SwShHyperTrainingCenterPatcher.Keys[1].Hash)) return true;
            }
            catch (InvalidDataException) { /* An unrelated Center mod does not enable this service. */ }
        }
        foreach (var path in SwShHyperTrainingItemDialogue.Targets(project).Select(CenterText).Select(path => Path.ChangeExtension(path, ".tbl")))
        {
            var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path);
            if (source?.Entry.LayeredFile is not null && File.ReadAllBytes(source.AbsolutePath).AsSpan()
                .IndexOf("km_center_hyper_training_"u8) >= 0) return true;
        }
        return false;
    }

    internal static SwShHyperTrainingWorkflow Enrich(OpenedProject project, SwShHyperTrainingWorkflow workflow)
    {
        if (workflow.InstallStatus == "blocked") return workflow;
        try
        {
            var enabled = IsEnabled(project);
            var sources = workflow.Sources.ToList();
            if (enabled)
            {
                foreach (var path in Dependencies(project).Where(path => path == SwShHyperTrainingCenterPatcher.ScriptPath || path.Contains("sub_event_129", StringComparison.Ordinal)))
                {
                    var entry = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path)?.Entry;
                    if (entry is null) continue;
                    sources.Add(new(path, path, path, "available", new(path,
                        entry.LayeredFile is null ? ProjectFileLayer.Base : ProjectFileLayer.Layered, entry.State)));
                }
            }
            return workflow with
            {
                PokemonCenterEnabled = enabled,
                InstallStatus = enabled && workflow.InstallStatus != "readOnly" ? "installed" : workflow.InstallStatus,
                Sources = sources,
                Stats = new(sources.Count(source => source.Status == "available"),
                    workflow.Stats.OutputFileCount + sources.Count - workflow.Sources.Count),
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An optional service must not prevent editing the ordinary Tower settings.
            return workflow with { PokemonCenterMessage = exception.Message };
        }
    }

    internal static IEnumerable<string> Dependencies(OpenedProject project)
    {
        yield return SwShHyperTrainingCenterPatcher.ScriptPath;
        yield return SwShHyperTrainingWorkflowService.ScriptPath;
        yield return EnglishKeys;
        foreach (var hyperPath in SwShHyperTrainingItemDialogue.Targets(project))
        {
            yield return hyperPath;
            yield return Path.ChangeExtension(hyperPath, ".tbl");
            yield return SwShHyperTrainingItemDialogue.NamesPath(hyperPath);
            yield return CenterText(hyperPath);
            yield return Path.ChangeExtension(CenterText(hyperPath), ".tbl");
        }
    }

    internal static IReadOnlyList<Output> Prepare(OpenedProject project, SwShHyperTrainingSelection selection)
    {
        if (!selection.PokemonCenterEnabled && !IsEnabled(project)) return [];
        var scriptPath = SwShHyperTrainingCenterPatcher.ScriptPath;
        var centerBase = Base(project, scriptPath);
        var hyperBase = Base(project, SwShHyperTrainingWorkflowService.ScriptPath);
        var keys = SwShAhtbFile.Parse(Base(project, EnglishKeys));
        SwShHyperTrainingCenterPatcher.VerifyCurrent(Source(project, scriptPath), centerBase, hyperBase, keys);
        var outputs = new List<Output>
        {
            new(scriptPath, selection.PokemonCenterEnabled
                ? SwShHyperTrainingCenterPatcher.Build(centerBase, hyperBase, keys, selection.MinimumLevel, selection.Items).Bytes
                : centerBase, centerBase),
        };
        foreach (var hyperPath in SwShHyperTrainingItemDialogue.Targets(project))
        {
            var centerPath = CenterText(hyperPath);
            var keyPath = Path.ChangeExtension(centerPath, ".tbl");
            var baselineText = Base(project, centerPath);
            var baselineKeys = Base(project, keyPath);
            var text = SwShGameTextFile.Parse(Source(project, centerPath));
            var currentKeys = SwShAhtbFile.Parse(Source(project, keyPath)).Entries;
            ValidateTextKeys(text, currentKeys);
            var ownedKeys = SwShHyperTrainingCenterPatcher.Keys;
            var owned = currentKeys.Where(key => key.Name.StartsWith("km_center_hyper_training_", StringComparison.Ordinal)).ToArray();
            if (owned.Any(key => !ownedKeys.Contains(key)) || owned.Length is not (0 or 17))
                throw new InvalidDataException("The Pokemon Center Hyper Training message rows are incomplete or unrecognized.");
            var rows = currentKeys.Take(text.Lines.Count).Select((key, index) => (Key: key, Line: text.Lines[index])).ToList();
            if (selection.PokemonCenterEnabled)
            {
                var hyperKeys = SwShAhtbFile.Parse(Source(project, Path.ChangeExtension(hyperPath, ".tbl"))).Entries;
                if (!hyperKeys.SequenceEqual(keys.Entries)) throw new InvalidDataException("Hyper Training message keys differ between languages or have other edits.");
                var namesPath = SwShHyperTrainingItemDialogue.NamesPath(hyperPath);
                var configured = SwShHyperTrainingItemDialogue.Apply(Source(project, hyperPath), Base(project, hyperPath),
                    Source(project, namesPath), hyperPath, selection.Items);
                if (hyperPath == SwShHyperTrainingWorkflowService.EnglishDialoguePath)
                    configured = SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(configured, selection.MinimumLevel);
                var added = SwShGameTextFile.Parse(configured).Lines.ToArray();
                if (added.Length != 17) throw new InvalidDataException("Hyper Training dialogue does not contain the supported 17 rows.");
                added[0] = added[2];
                if (hyperPath != SwShHyperTrainingWorkflowService.EnglishDialoguePath)
                {
                    var level = selection.MinimumLevel.ToString(CultureInfo.InvariantCulture);
                    if (!added[3].Text.Contains("100", StringComparison.Ordinal) && !added[3].Text.Contains("１００", StringComparison.Ordinal))
                        throw new InvalidDataException("The localized Hyper Training level message has other edits.");
                    added[3] = added[3] with { Text = added[3].Text.Replace("100", level, StringComparison.Ordinal).Replace("１００", level, StringComparison.Ordinal) };
                }
                for (var index = 0; index < ownedKeys.Length; index++)
                {
                    var existing = rows.FindIndex(row => row.Key == ownedKeys[index]);
                    if (existing < 0) rows.Add((ownedKeys[index], added[index]));
                    // Keep edits to unrelated copied dialogue, but always synchronize level and payment prompts.
                    else if (index is 1 or 3 or 5) rows[existing] = (ownedKeys[index], added[index]);
                }
            }
            else rows.RemoveAll(row => ownedKeys.Contains(row.Key));
            var resultLines = rows.Select(row => row.Line).ToArray();
            var resultKeys = rows.Select(row => row.Key).Append(currentKeys.Last()).ToArray();
            var originalText = SwShGameTextFile.Parse(baselineText);
            var textBytes = resultLines.SequenceEqual(originalText.Lines) ? baselineText
                : resultLines.SequenceEqual(text.Lines) ? Source(project, centerPath)
                : text.WriteStructural(resultLines, GameTextNullLineEncoding.PayloadCountTwo);
            var keyBytes = resultKeys.SequenceEqual(SwShAhtbFile.Parse(baselineKeys).Entries) ? baselineKeys
                : new SwShAhtbFile(resultKeys).Write();
            ValidateTextKeys(SwShGameTextFile.Parse(textBytes), SwShAhtbFile.Parse(keyBytes).Entries);
            outputs.Add(new(centerPath, textBytes, baselineText));
            outputs.Add(new(keyPath, keyBytes, baselineKeys));
        }
        if (outputs.Count == 1) throw new InvalidDataException("Pokemon Center Hyper Training requires its localized dialogue files.");
        return outputs;
    }

    private static void ValidateTextKeys(SwShGameTextFile text, IReadOnlyList<SwShAhtbEntry> keys)
    {
        if (keys.Count != text.Lines.Count + 1 || !keys.Last().Name.EndsWith("_max", StringComparison.Ordinal)
            || keys.Any(key => SwShGfPackFile.HashFnv1a64(key.Name) != key.Hash)
            || keys.Select(key => key.Hash).Distinct().Count() != keys.Count)
            throw new InvalidDataException("Pokemon Center message text and keys do not match.");
    }
}
