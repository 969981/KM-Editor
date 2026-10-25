// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Pokemon;
using KM.SwSh.Trainers;
using T = KM.SwSh.Trainers.SwShTrainersWorkflowService;

namespace KM.SwSh.Randomizer;

public sealed partial class SwShRandomizerService
{
    private readonly SwShTrainersWorkflowService trainersWorkflowService = new();
    private readonly SwShTrainersEditSessionService trainersEditSessionService = new();
    private static readonly HashSet<int> BossTrainerClasses = [4, 6, 7, 8, 11, 12, 13, 14, 15,
        20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 32, 74, 75, 184, 185, 188, 199,
        205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220,
        221, 222, 227, 228, 250, 251, 252, 253];
    private static readonly HashSet<int> LegendarySpecies = [144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 251,
        377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493,
        494, 638, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649, 716, 717, 718, 719, 720, 721, 772, 773,
        785, 786, 787, 788, 789, 790, 791, 792, 793, 794, 795, 796, 797, 798, 799, 800, 801, 802, 803, 804, 805, 806, 807, 808, 809,
        888, 889, 890, 891, 892, 893, 894, 895, 896, 897, 898];
    private static readonly string[] TrainerStatFields = [T.EvHpField, T.EvAttackField, T.EvDefenseField,
        T.EvSpecialAttackField, T.EvSpecialDefenseField, T.EvSpeedField];
    private static readonly string[] TrainerIvFields = [T.IvHpField, T.IvAttackField, T.IvDefenseField,
        T.IvSpecialAttackField, T.IvSpecialDefenseField, T.IvSpeedField];
    private static readonly string[] TrainerMoveFields = [T.Move1IdField, T.Move2IdField, T.Move3IdField, T.Move4IdField];

    private static bool IsFullTeamBoss(SwShTrainerRecord trainer) => BossTrainerClasses.Contains(trainer.TrainerClassId)
        && (trainer.BattleTypeValue == 0 || trainer.TrainerClassId == 29);

    private static bool IsStartingPokemonForm(PokemonCandidate candidate) => candidate.Form == 0 || candidate.SpeciesId switch
    {
        351 or 421 or 648 or 681 or 746 or 778 or 845 or 875 or 877 or 888 or 889 or 890 => false,
        555 => candidate.Form % 2 == 0,
        718 => candidate.Form != 4,
        658 => candidate.Form != 1,
        800 => candidate.Form != 3,
        _ => true,
    };

    private void AddTrainerPlan(OpenedProject project, SwShPokemonWorkflow? pokemon,
        IReadOnlyList<MoveCandidate> moves, IReadOnlyList<ItemCandidate> items, string key,
        SwShRandomizerOptions options, ICollection<RandomizerDomainPlan> plans, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.RandomizeTrainers || pokemon is null || options.Trainers is not { IsValid: true } settings) return;
        var workflow = trainersWorkflowService.Load(project);
        AddWorkflowErrors(workflow.Diagnostics, diagnostics);
        var records = pokemon.Pokemon.ToDictionary(row => row.PersonalId);
        var candidates = CreatePokemonTargets(pokemon, includeProtected: true, useCurrentPools: true);
        var pool = candidates
            .Where(row => settings.AllowLegendaries || !LegendarySpecies.Contains(row.SpeciesId)).ToArray();
        if (pool.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "No available Pokemon match the trainer pool.", field: "trainers"));
            return;
        }
        var updates = new List<SwShTrainerFieldUpdate>();
        foreach (var trainer in workflow.Trainers.OrderBy(row => row.TrainerId))
        {
            var rng = DeterministicRandom.Create(key, "trainers/" + Number(trainer.TrainerId));
            var occupied = trainer.Team.Where(row => row.SpeciesId > 0).OrderBy(row => row.Slot).ToList();
            if (occupied.Count == 0) continue;
            var expand = settings.FullBossTeams && IsFullTeamBoss(trainer) && occupied.Count < 6;
            if (expand)
            {
                var ace = occupied[^1]; occupied.RemoveAt(occupied.Count - 1);
                while (occupied.Count < 5) occupied.Add(ace with { CanDynamax = false, CanGigantamax = false });
                occupied.Add(ace);
            }
            for (var index = 0; index < occupied.Count; index++)
            {
                var original = occupied[index];
                var slot = index + 1;
                var moved = expand;
                var target = settings.Species || expand && index >= trainer.Team.Count(row => row.SpeciesId > 0) - 1 && index < 5
                    ? rng.Pick(pool) : candidates.FirstOrDefault(row => row.SpeciesId == original.SpeciesId && row.Form == original.Form);
                var personalId = target?.PersonalId ?? (original.Form == 0 ? original.SpeciesId :
                    records.TryGetValue(original.SpeciesId, out var baseRecord) ? baseRecord.Personal.FormStatsIndex + original.Form - 1 : -1);
                if (!records.TryGetValue(personalId, out var personal))
                {
                    diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "A trainer Pokemon has no matching personal record.", field: "trainers"));
                    continue;
                }
                target ??= new(personalId, original.SpeciesId, original.Form, personal.Personal.Type1, personal.Personal.Type2,
                    personal.EvolutionStage, Source(personal.Provenance));
                var changed = target.SpeciesId != original.SpeciesId || target.Form != original.Form;
                void Set(string field, int value) => updates.Add(new(trainer.TrainerId, slot, field, Number(value)));
                if (settings.Species || moved || changed) { Set(T.SpeciesIdField, target.SpeciesId); Set(T.FormField, target.Form); }
                if (settings.LevelPercent != 100 || moved)
                    Set(T.LevelField, Math.Clamp((int)Math.Round(original.Level * settings.LevelPercent / 100d, MidpointRounding.AwayFromZero), 1, 100));
                if (settings.Abilities || changed || moved)
                {
                    var slots = new List<int>();
                    if (personal.Abilities.Ability1 > 0) slots.Add(1);
                    if (personal.Abilities.Ability2 > 0) slots.Add(2);
                    if (personal.Abilities.HiddenAbility > 0) slots.Add(3);
                    Set(T.AbilityField, settings.Abilities && slots.Count > 0 ? rng.Pick(slots) : changed ? 0 : original.Ability);
                }
                if (settings.Genders || changed || moved)
                    Set(T.GenderField, settings.Genders ? personal.Personal.GenderRatio switch { 0 => 1, 254 => 2, 255 => 0, _ => rng.NextBool() ? 1 : 2 } : changed ? 0 : original.Gender);
                if (settings.Natures || moved) Set(T.NatureField, settings.Natures ? rng.NextInt(25) : original.Nature);
                if (settings.HeldItems || moved) Set(T.HeldItemIdField, settings.HeldItems ? rng.Pick(items).ItemId : original.HeldItemId);
                if (settings.Moves || changed || moved)
                {
                    var selected = settings.Moves ? GenerateLearnset(personal, moves, 4,
                        options with { LearnsetStabFirst = true, LearnsetRequireDamagingMove = true }, rng).Select(row => row.MoveId).ToArray()
                        : changed ? new int[4] : original.MoveIds.ToArray();
                    for (var move = 0; move < 4; move++) Set(TrainerMoveFields[move], move < selected.Length ? selected[move] : 0);
                }
                if (settings.Ivs != "unchanged" || moved)
                {
                    var values = StatValues(original.Ivs);
                    for (var stat = 0; stat < 6; stat++) Set(TrainerIvFields[stat], settings.Ivs switch { "maximum" => 31, "random" => rng.NextInt(32), _ => values[stat] });
                }
                if (settings.Evs != "unchanged" || moved)
                {
                    var values = settings.Evs == "unchanged" ? StatValues(original.Evs) : TrainerEvs(personal, settings.Evs, rng);
                    for (var stat = 0; stat < 6; stat++) Set(TrainerStatFields[stat], values[stat]);
                }
                if (changed || moved) Set(T.CanGigantamaxField, !changed && original.CanGigantamax ? 1 : 0);
                if (moved || changed && personal.Personal.CanNotDynamax) Set(T.CanDynamaxField, original.CanDynamax && !personal.Personal.CanNotDynamax ? 1 : 0);
                if (moved) { Set(T.DynamaxLevelField, original.DynamaxLevel); Set(T.ShinyField, original.Shiny ? 1 : 0); }
            }
        }
        if (updates.Count == 0) return;
        var staged = trainersEditSessionService.CreateBatchSession(project.Paths, updates);
        AddWorkflowErrors(staged.Diagnostics, diagnostics);
        if (staged.Session.PendingEdits.Count > 0)
            plans.Add(new("Trainers", staged.Session, trainersEditSessionService.CreateChangePlan, trainersEditSessionService.ApplyChangePlan));
    }

    private static int[] StatValues(SwShTrainerPokemonStatsRecord stats) =>
        [stats.HP, stats.Attack, stats.Defense, stats.SpecialAttack, stats.SpecialDefense, stats.Speed];

    private static int[] TrainerEvs(SwShPokemonRecord pokemon, string mode, DeterministicRandom rng)
    {
        var result = new int[6];
        if (mode == "maximum")
        {
            var values = new[] { pokemon.BaseStats.HP, pokemon.BaseStats.Attack, pokemon.BaseStats.Defense,
                pokemon.BaseStats.SpecialAttack, pokemon.BaseStats.SpecialDefense, pokemon.BaseStats.Speed };
            var order = Enumerable.Range(0, 6).OrderByDescending(index => values[index]).ThenBy(index => index).ToArray();
            result[order[0]] = 252; result[order[1]] = 252; result[order[2]] = 6;
        }
        else
        {
            var remaining = 510;
            while (remaining-- > 0)
            {
                var eligible = Enumerable.Range(0, 6).Where(index => result[index] < 252).ToArray();
                result[rng.Pick(eligible)]++;
            }
        }
        return result;
    }
}
