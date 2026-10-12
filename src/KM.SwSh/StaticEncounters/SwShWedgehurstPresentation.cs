// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using System.Text.RegularExpressions;

namespace KM.SwSh.StaticEncounters;

internal sealed class SwShWedgehurstPresentation(OpenedProject project)
{
    internal const ulong EncounterId = 17733440623446737355UL;
    internal const string SourceInvalidCode = "KM-SWSH-STATIC-PRESENTATION-SOURCE-INVALID";
    internal const string TextPreservedCode = "KM-SWSH-STATIC-PRESENTATION-TEXT-PRESERVED";
    internal SwShSceneSources Files { get; } = new(project);
    internal List<ValidationDiagnostic> Diagnostics { get; } = [];
    private static readonly int[][] VoiceCalls = [[5141], [4483, 4693, 4752, 4812, 5046, 5866]];

    internal void Build(byte[] finalTable)
    {
        var selected = SwShStaticEncounterArchive.Parse(finalTable).Encounters.Single(row => row.EncounterId == EncounterId);
        var previous = new HashSet<(int Species, int Form)> { (79, 1) };
        var current = SwShStaticEncounterArchive.Parse(Files.Read(SwShStaticEncountersWorkflowService.StaticEncounterDataPath))
            .Encounters.Single(row => row.EncounterId == EncounterId);
        previous.Add((current.Species, current.Form));
        Files.VerifyModel(selected.Species, selected.Form);
        for (var scene = 0; scene < 2; scene++)
        {
            var path = $"romfs/bin/script/amx/rigel_other_jizen_ev00{scene + 2}0.amx";
            var document = SwShAmxDocument.Parse(Files.Read(path));
            var native = document.NativeHashes.ToList().IndexOf(SwShAmxNativeNameHash.Compute("SoundPlayPokeVoice"));
            var assembler = document.CreateAssembler();
            foreach (var cell in VoiceCalls[scene])
            {
                var call = assembler.GetInstructionAtOriginalCell(cell);
                var species = assembler.GetInstructionAtOriginalCell(cell - 1);
                var form = assembler.GetInstructionAtOriginalCell(cell - 2);
                if (native < 0 || call.Mnemonic != "sysreq.n" || call.Operands[0].LiteralValue != native
                    || call.Operands[1].LiteralValue != 32 || species.Mnemonic != "push.p.c" || form.Mnemonic != "push.p.c"
                    || species.Operands[0].LiteralValue is < 1 or > ushort.MaxValue || form.Operands[0].LiteralValue is < 0 or > byte.MaxValue)
                    throw new InvalidDataException("Wedgehurst Pokemon cry calls have an unsupported layout.");
                previous.Add((checked((int)species.Operands[0].LiteralValue), checked((int)form.Operands[0].LiteralValue)));
                assembler.ReplaceLiteralOperand(species, 0, species.Operands[0].LiteralValue, selected.Species);
                assembler.ReplaceLiteralOperand(form, 0, form.Operands[0].LiteralValue, selected.Form);
            }
            Files.Outputs[path] = assembler.Assemble();
        }
        const string placement = "romfs/bin/archive/field/resident/placement.gfpak";
        var pack = SwShGfPackFile.Parse(Files.Read(placement));
        const string member = "a_t0201_s0101.bin";
        pack.SetFileByName(member, SwShPlacementSpeciesWriter.Write(pack.GetFileByName(member),
            0xF8630F0518188E0C, (uint)selected.Species, (uint)selected.Form));
        Files.Outputs[placement] = pack.Write();
        foreach (var language in SwShGameTextLanguage.SupportedMessageLanguages)
        for (var scene = 0; scene < 2; scene++)
        {
            var path = SwShGameTextLanguage.ScriptMessagePath(language, $"rigel_other_jizen_00{scene + 2}0.dat");
            if (!Files.Contains(path)) continue;
            var names = SwShGameTextFile.Parse(Files.Read(SwShGameTextLanguage.CommonMessagePath(language, "monsname.dat"))).Lines;
            var (source, vanilla) = Files.ReadTextPair(path);
            var positions = scene == 0 ? new[] { 4, 5, 6, 8, 9, 10 } : [0, 2, 3, 4, 5, 6, 8, 9, 12, 13, 16];
            if (vanilla.Lines.Count <= positions.Max()) throw new InvalidDataException("Wedgehurst dialogue is incomplete.");
            var lines = source.Lines.ToArray();
            foreach (var index in positions)
            {
                string Generated((int Species, int Form) value)
                {
                    if (value == (79, 1)) return vanilla.Lines[index].Text;
                    if (value.Species <= 0 || value.Species >= names.Count) throw new InvalidDataException("Pokemon has no localized name.");
                    return CreateText(language, names[value.Species].Text, scene, index, vanilla.Lines[index].Text);
                }
                var wanted = Generated((selected.Species, selected.Form));
                if (lines[index].Text == wanted) continue;
                if (!previous.Any(value => Generated(value) == lines[index].Text))
                {
                    Diagnostics.Add(new(DiagnosticSeverity.Warning,
                        "Custom Wedgehurst dialogue was preserved. Review this line in Text if it names the previous Pokemon.",
                        path, SwShStaticEncountersWorkflowService.StaticEncountersEditDomain, $"line:{index}") { Code = TextPreservedCode });
                    continue;
                }
                lines[index] = lines[index] with { Text = wanted };
            }
            Files.Outputs[path] = source.WritePreserving(lines);
        }
    }

    private static string CreateText(string language, string name, int scene, int index, string original)
    {
        var kind = scene == 0 ? index == 4 ? 0 : index == 10 ? 2 : 1
            : index is 0 or 4 or 6 or 12 ? 0 : index == 5 ? 3 : index is 13 or 16 ? 5 : 4;
        var phrases = language switch
        {
            "English" => new[] { $"{name}!", $"Oh, {name}!", $"It looks like {name} arrived by train.", $"Battle {name} in order to catch it?", $"Please catch {name} so the ticket gate is clear.", $"Thank you for catching {name}!" },
            "Spanish" => new[] { $"¡{name}!", $"¡Oh, {name}!", $"Parece que {name} ha llegado en tren.", $"¿Quieres combatir contra {name} para capturarlo?", $"Captura a {name} para despejar el acceso.", $"¡Gracias por capturar a {name}!" },
            "French" => new[] { $"{name} !", $"Oh, {name} !", $"On dirait que {name} est venu en train.", $"Affronter {name} pour le capturer ?", $"Capture {name} pour libérer le passage.", $"Merci d’avoir capturé {name} !" },
            "German" => new[] { $"{name}!", $"Oh, {name}!", $"Anscheinend ist {name} mit dem Zug angekommen.", $"Gegen {name} kämpfen, um es zu fangen?", $"Fange bitte {name}, damit der Durchgang frei wird.", $"Danke, dass du {name} gefangen hast!" },
            "Italian" => new[] { $"{name}!", $"Oh, {name}!", $"Sembra che {name} sia arrivato in treno.", $"Vuoi affrontare {name} per catturarlo?", $"Cattura {name} per liberare il passaggio.", $"Grazie per aver catturato {name}!" },
            "JPN" or "JPN_KANJI" => new[] { $"{name}！", $"あっ　{name}！", $"{name}が　れっしゃに　のってきたようだ。", $"{name}と　たたかって　つかまえる？", $"{name}を　つかまえて　とおりみちを　あけてね。", $"{name}を　つかまえてくれて　ありがとう！" },
            "Korean" => new[] { $"{name}!", $"앗, {name}!", $"{name}, 기차를 타고 온 모양이야.", $"{name}, 배틀해서 잡을까?", $"{name}, 잡아서 통로를 비워 줘.", $"{name}, 잡아 줘서 고마워!" },
            "Simp_Chinese" => new[] { $"{name}！", $"啊，{name}！", $"{name}好像是坐火车来的。", $"要和{name}对战并捉住它吗？", $"请捉住{name}，让通道畅通。", $"谢谢你捉住了{name}！" },
            "Trad_Chinese" => new[] { $"{name}！", $"啊，{name}！", $"{name}好像是坐火車來的。", $"要和{name}對戰並捉住牠嗎？", $"請捉住{name}，讓通道暢通。", $"謝謝你捉住了{name}！" },
            _ => throw new InvalidDataException("Wedgehurst dialogue language is unsupported."),
        };
        return phrases[kind] + string.Concat(Regex.Matches(original, @"\[VAR 0114\([^\]]*\)\]").Select(match => match.Value));
    }
}
