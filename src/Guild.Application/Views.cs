using System.Numerics;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;

namespace Guild.Application;

public sealed partial class GameController
{
    public GameView View
    {
        get
        {
            var pending = PeekGrowthEvent();
            var selected = pending?.Kind is not null;
            var levelActor = session?.LevelChoices.Keys.FirstOrDefault();
            var screen = selected ? "RankEvent" : levelActor is not null ? "LevelChoice" : session?.Phase ?? "Guild";
            var definitions = session?.Current.Setup.Content ?? content;
            var stage = definitions.Stages.Single(s => s.Id == (session?.Current.Setup.StageId ?? definitions.Stages.First(s => s.Mode != "invasion").Id));
            var invasion = stage.Mode == "invasion";
            var partyIds = session?.Current.Setup.PartyIds ?? profile.Adventurers.Select(a => a.Id).ToList();
            var decisionSimulation = DecisionSimulation();
            var adventurers = profile.Adventurers.Select(actor =>
            {
                var unit = decisionSimulation.State.Units.Single(x => x.Id == actor.Id);
                var values = decisionSimulation.DescribeUnit(actor.Id);
                var weapon = profile.Weapons.Single(x => x.Id == actor.WeaponId);
                var maxHp = values.MaxHp;
                var blessingDefinitions = selected && pending?.ActorId == actor.Id ? EventContent(pending) : definitions;
                var blessings = actor.Blessings.Select(owned => $"{owned.Id} {blessingDefinitions.Blessings.FirstOrDefault(x => x.Id == owned.Id)?.Name ?? owned.Id} {Roman(owned.Rank)}").ToArray();
                var skills = unit.Skills.Select(slot => new SkillView(slot.Slot,
                    definitions.Skills.Single(x => x.Id == slot.SkillId).Name, Math.Clamp(slot.Progress.ToDouble(), 0, 1))).ToArray();
                var preparation = BuildPreparationInfoView(actor, unit);
                return new AdventurerView(actor.Id, actor.Name, actor.Rank.ToString(), unit.Level,
                    values.Hp.ToString(), maxHp.ToString(), weapon.Name, unit.Deployed,
                    unit.Hp == 0, invasion ? unit.DeploymentX : unit.AnchorX,
                    invasion ? unit.DeploymentY : unit.AnchorY, blessings, skills, unit.TargetPreset)
                {
                    InParty = !IsDispatched(actor.Id) && partyIds.Contains(actor.Id, StringComparer.Ordinal),
                    IsDispatched = IsDispatched(actor.Id),
                    WeaponType = unit.WeaponType,
                    BlessingDetails = BuildBlessingDetails(actor, blessingDefinitions),
                    ActivityCode = IsDispatched(actor.Id) ? "dispatch" : battle is null ? "guild" : invasion && !partyIds.Contains(actor.Id, StringComparer.Ordinal) ? "outside-party"
                        : unit.Hp <= 0 ? "incapacitated" : !unit.Deployed ? "reserve" : session?.Phase != "Battle" ? "preparation"
                        : unit.Action is not null ? unit.Action.Kind == "skill" ? "skill" : "attack"
                        : unit.IsEngaged ? "engaged" : invasion ? "advancing" : "watch",
                    Activity = IsDispatched(actor.Id) ? EconomyText(session is null ? "activity.reserved" : "activity.active") : battle is null ? DisplayText.Get("app.activity.guild") : invasion && !partyIds.Contains(actor.Id, StringComparer.Ordinal) ? DisplayText.Get("app.activity.outsideParty")
                        : unit.Hp <= 0 ? DisplayText.Get("app.activity.incapacitated") : !unit.Deployed ? DisplayText.Get("app.activity.reserve") : session?.Phase != "Battle" ? DisplayText.Get("app.activity.preparation")
                        : unit.Action is not null ? unit.Action.Kind == "skill" ? DisplayText.Get("app.activity.skill") : DisplayText.Get("app.activity.attack")
                        : unit.IsEngaged ? DisplayText.Get("app.activity.engaged") : invasion ? DisplayText.Get("app.activity.advancing") : DisplayText.Get("app.activity.watch"),
                    DeploymentCost = DeploymentPrice(actor).ToString(),
                    RevivalCost = ((unit.RevivalCount + 1) * unit.Level * 25).ToString(),
                    LevelPurchaseCost = preparation?.PurchaseCost ?? (unit.Level >= 10 ? DisplayText.Get("app.preparation.cap") : "0"),
                    Performance = BuildPerformanceView(actor, unit, values, definitions),
                    PreparationInfo = preparation,
                    RangeGuide = BuildRangeGuideView(unit, values, definitions)
                };
            }).ToArray();
            var entities = new List<MapEntityView>();
            if (battle is not null)
            {
                entities.AddRange(battle.State.Units.Where(x => x.Deployed && x.Hp > 0).Select(unit => new MapEntityView(unit.Id,
                    unit.IsEnemy ? invasion ? unit.Role : "enemy" : "ally", unit.X.ToDouble(), unit.Y.ToDouble(), unit.MaxHp == 0 ? 0 : Math.Clamp(new Rational(unit.Hp, unit.MaxHp).ToDouble(), 0, 1))));
                entities.AddRange(battle.State.Projectiles.Select(x => new MapEntityView(x.Id, "projectile", x.X.ToDouble(), x.Y.ToDouble(), 1)));
            }
            var preview = invasion && session?.Phase == "Preparation" && battle is not null
                ? stage.Sections![Math.Min(battle.State.Wave, stage.Sections.Count - 1)].Enemies : null;
            if (preview is not null)
                entities.AddRange(preview.Select(enemy => new MapEntityView("preview:" + enemy.Id, enemy.Role, enemy.X, enemy.Y, 1)));
            var choices = new List<ChoiceView>();
            if (levelActor is not null)
                choices.AddRange(session!.LevelChoices[levelActor].Select(id => new ChoiceView(id, LevelName(id), LevelDescription(id))));
            else if (selected && (pending!.Kind == "blessing" || pending.Kind == "random" && pending.RandomRevealed))
            {
                var frozen = EventContent(pending);
                var actor = Actor(pending.ActorId);
                choices.AddRange(pending.Candidates.Select(id =>
                {
                    var blessing = frozen.Blessings.Single(x => x.Id == id);
                    var current = actor.Blessings.FirstOrDefault(x => x.Id == id);
                    var resultingRank = (current?.Rank ?? 0) + 1;
                    var next = current is null ? new OwnedBlessing { Id = id, Rank = resultingRank } : Clone(current);
                    next.Rank = resultingRank;
                    return new ChoiceView(id, $"{blessing.Name} {Roman(resultingRank)}", DescribeBlessing(next, frozen))
                    {
                        IsBlessing = true, CurrentRank = current?.Rank ?? 0, ResultRank = resultingRank
                    };
                }));
            }
            else if (selected && pending!.Kind == "fusion")
            {
                var frozen = EventContent(pending);
                choices.AddRange(EligibleRecipes(Actor(pending.ActorId), frozen).Select(recipe => new ChoiceView(recipe.Id,
                    frozen.Blessings.Single(x => x.Id == recipe.FirstBlessingId).Name + " III ＋ " + frozen.Blessings.Single(x => x.Id == recipe.SecondBlessingId).Name + " III",
                    profile.DiscoveredRecipes.Contains(recipe.Id) ? DisplayText.Get("app.fusion.discoveredPrefix") + frozen.Blessings.Single(x => x.Id == recipe.ResultBlessingId).Name + " I" : DisplayText.Get("app.fusion.undiscovered"))));
            }
            var items = profile.Weapons.Select(x => new ItemView(x.Id, x.Name, x.OwnerId,
                profile.Adventurers.Any(a => a.WeaponId == x.Id))).ToArray();
            var objective = battle?.State.Units.FirstOrDefault(x => x.IsEnemy && x.Role is "gate" or "core");
            var previewObjective = preview?.FirstOrDefault(x => x.Role is "gate" or "core");
            var sectionIndex = session?.Phase == "Preparation" ? battle?.State.Wave ?? 0 : Math.Max(0, (battle?.State.Wave ?? 1) - 1);
            var sectionLimit = invasion ? stage.Sections![Math.Min(sectionIndex, stage.Sections.Count - 1)].TimeLimitMs : 0;
            return new(screen, status, profile.Resources.ToString(), battle?.State.Resources.ToString() ?? "0",
                battle?.State.Wave ?? 0, invasion ? stage.Sections!.Count : stage.Waves.Count, battle?.State.DefenseHp ?? stage.DefenseHp,
                speed, paused, profile.Capacity, profile.PendingEvents.Count, levelActor ?? (selected ? pending!.ActorId : null),
                selected ? pending!.Kind : null, adventurers, entities, choices, items, BuildGrowthHistory(), stage.MapWidth, stage.MapHeight)
            {
                Mode = invasion ? "invasion" : "defense", StageId = stage.Id, StageName = stage.Name,
                Stages = content.Stages.Select(s => new StageView(s.Id, s.Name, s.Mode ?? "defense",
                    s.Mode == "invasion" ? s.Sections!.Count : s.Waves.Count, s.RewardRank.ToString(),
                    s.Mode == "invasion" ? s.Sections![0].TimeLimitMs : 0,
                    s.Mode == "invasion" ? DisplayText.Get("app.stage.reward.invasion") : DisplayText.Get("app.stage.reward.defense")) { Rank = s.Rank.ToString(), InitialResources = "1000" }).ToArray(),
                PartyIds = partyIds, Path = stage.Path,
                DeploymentCells = stage.DeploymentCells ?? Enumerable.Range(0, stage.MapWidth).SelectMany(x => Enumerable.Range(0, stage.MapHeight)
                    .Where(y => !stage.Path.Any(p => p.X == x && p.Y == y)).Select(y => new GridPoint(x, y))).ToArray(),
                RemainingTimeMs = !invasion ? 0 : session?.Phase == "Preparation" ? sectionLimit
                    : Math.Max(0, sectionLimit - ((battle?.State.TimeMs ?? 0) - (battle?.State.SectionStartTimeMs ?? 0))),
                RemainingEnemies = preview?.Count ?? battle?.State.Units.Count(x => x.IsEnemy && x.Hp > 0) ?? 0,
                ObjectiveHp = previewObjective?.Hp.Floor().ToString() ?? objective?.Hp.ToString() ?? "0",
                ObjectiveMaxHp = previewObjective?.Hp.Floor().ToString() ?? objective?.MaxHp.ToString() ?? "0",
                DefeatReason = battle?.State.DefeatReason is { } reason ? InvasionDefeatDescription(reason) : "",
                NextGrowthEvent = BuildNextGrowthEventView(),
                NextEncounter = BuildNextEncounterView(stage),
                ResultSummary = BuildMatchResultView(),
                Economy = BuildEconomyView()
            };
        }
    }

    private static string Roman(int rank) => rank switch { 1 => "I", 2 => "II", 3 => "III", _ => "?" };
    private static string LevelName(string id) => id switch { "vitality" => DisplayText.Get("app.level.vitality.name"), "firepower" => DisplayText.Get("app.level.firepower.name"), "rapid" => DisplayText.Get("app.level.rapid.name"), "mobility" => DisplayText.Get("app.level.mobility.name"), "reach" => DisplayText.Get("app.level.reach.name"), "defense" => DisplayText.Get("app.level.defense.name"), _ => id };
    private static string LevelDescription(string id) => id switch
    {
        "vitality" => DisplayText.Get("app.level.vitality.effect"), "firepower" => DisplayText.Get("app.level.firepower.effect"), "rapid" => DisplayText.Get("app.level.rapid.effect"),
        "mobility" => DisplayText.Get("app.level.mobility.effect"), "reach" => DisplayText.Get("app.level.reach.effect"), "defense" => DisplayText.Get("app.level.defense.effect"), _ => ""
    };
}
