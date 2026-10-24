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
            var pending = profile.PendingEvents.OrderBy(x => x.Sequence).FirstOrDefault();
            var selected = pending?.Kind is not null;
            var levelActor = session?.LevelChoices.Keys.FirstOrDefault();
            var screen = selected ? "RankEvent" : levelActor is not null ? "LevelChoice" : session?.Phase ?? "Guild";
            var definitions = session?.Current.Setup.Content ?? content;
            var stage = definitions.Stages.Single(s => s.Id == (session?.Current.Setup.StageId ?? definitions.Stages.First(s => s.Mode != "invasion").Id));
            var invasion = stage.Mode == "invasion";
            var partyIds = session?.Current.Setup.PartyIds ?? profile.Adventurers.Select(a => a.Id).ToList();
            var adventurers = profile.Adventurers.Select(actor =>
            {
                var unit = battle?.State.Units.FirstOrDefault(x => x.Id == actor.Id);
                var weapon = profile.Weapons.Single(x => x.Id == actor.WeaponId);
                var maxHp = unit?.MaxHp ?? (new Rational(PersistentMaximumHp(actor)) * Rational.Max(new Rational(1, 100), 1 - new Rational(actor.WeaknessStacks * 5, 100))).Ceiling();
                var blessingDefinitions = selected && pending?.ActorId == actor.Id ? EventContent(pending) : content;
                var blessings = actor.Blessings.Select(owned => $"{owned.Id} {blessingDefinitions.Blessings.FirstOrDefault(x => x.Id == owned.Id)?.Name ?? owned.Id} {Roman(owned.Rank)}").ToArray();
                var skills = unit?.Skills.Select(slot => new SkillView(slot.Slot,
                    definitions.Skills.Single(x => x.Id == slot.SkillId).Name, Math.Clamp(slot.Progress.ToDouble(), 0, 1))).ToArray() ?? [];
                return new AdventurerView(actor.Id, actor.Name, actor.Rank.ToString(), unit?.Level ?? 1,
                    (unit?.Hp ?? actor.Hp).ToString(), maxHp.ToString(), weapon.Name, unit?.Deployed ?? false,
                    unit?.Hp == 0, invasion ? unit?.DeploymentX ?? 0 : unit?.AnchorX ?? 0,
                    invasion ? unit?.DeploymentY ?? 0 : unit?.AnchorY ?? 0, blessings, skills, unit?.TargetPreset ?? "arrival")
                {
                    InParty = partyIds.Contains(actor.Id, StringComparer.Ordinal),
                    Activity = unit is null ? "拠点待機" : invasion && !partyIds.Contains(actor.Id, StringComparer.Ordinal) ? "遠征外"
                        : unit.Hp <= 0 ? "戦闘不能" : !unit.Deployed ? "控え" : session?.Phase != "Battle" ? "出撃準備"
                        : unit.Action is not null ? unit.Action.Kind == "skill" ? "技能動作" : "攻撃動作"
                        : unit.IsEngaged ? "交戦中" : invasion ? "進軍中" : "警戒中",
                    DeploymentCost = BigInteger.Min(250, 25 * (1 + actor.Rank / 10)).ToString(),
                    RevivalCost = ((unit?.RevivalCount + 1 ?? 1) * (unit?.Level ?? 1) * 25).ToString(),
                    LevelPurchaseCost = unit is null || unit.Level >= 10 ? "0" : (BigInteger.Max(0, Guild.Domain.Battle.BattleSimulation.LevelCost(unit.Level) - unit.LevelExperience) * 2).ToString()
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
                    var resultingRank = (actor.Blessings.FirstOrDefault(x => x.Id == id)?.Rank ?? 0) + 1;
                    return new ChoiceView(id, $"{blessing.Name} {Roman(resultingRank)}", DescribeBlessing(new() { Id = id, Rank = resultingRank }, frozen));
                }));
            }
            else if (selected && pending!.Kind == "fusion")
            {
                var frozen = EventContent(pending);
                choices.AddRange(EligibleRecipes(Actor(pending.ActorId), frozen).Select(recipe => new ChoiceView(recipe.Id,
                    frozen.Blessings.Single(x => x.Id == recipe.FirstBlessingId).Name + " III ＋ " + frozen.Blessings.Single(x => x.Id == recipe.SecondBlessingId).Name + " III",
                    profile.DiscoveredRecipes.Contains(recipe.Id) ? "結果: " + frozen.Blessings.Single(x => x.Id == recipe.ResultBlessingId).Name + " I" : "未発見の結果 — Rank I。名前と効果は実行後に公開します。")));
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
                selected ? pending!.Kind : null, adventurers, entities, choices, items, history.ToArray(), stage.MapWidth, stage.MapHeight)
            {
                Mode = invasion ? "invasion" : "defense", StageId = stage.Id, StageName = stage.Name,
                Stages = content.Stages.Select(s => new StageView(s.Id, s.Name, s.Mode ?? "defense",
                    s.Mode == "invasion" ? s.Sections!.Count : s.Waves.Count, s.RewardRank.ToString(),
                    s.Mode == "invasion" ? s.Sections![0].TimeLimitMs : 0,
                    s.Mode == "invasion" ? "勝利: 拠点資源300・武器1個 / 敗北: 突破区画×100" : "勝利: 拠点資源1000・武器1個 / 敗北: 突破Wave×100") { Rank = s.Rank.ToString() }).ToArray(),
                PartyIds = partyIds, Path = stage.Path,
                DeploymentCells = stage.DeploymentCells ?? Enumerable.Range(0, stage.MapWidth).SelectMany(x => Enumerable.Range(0, stage.MapHeight)
                    .Where(y => !stage.Path.Any(p => p.X == x && p.Y == y)).Select(y => new GridPoint(x, y))).ToArray(),
                RemainingTimeMs = !invasion ? 0 : session?.Phase == "Preparation" ? sectionLimit
                    : Math.Max(0, sectionLimit - ((battle?.State.TimeMs ?? 0) - (battle?.State.SectionStartTimeMs ?? 0))),
                RemainingEnemies = preview?.Count ?? battle?.State.Units.Count(x => x.IsEnemy && x.Hp > 0) ?? 0,
                ObjectiveHp = previewObjective?.Hp.Floor().ToString() ?? objective?.Hp.ToString() ?? "0",
                ObjectiveMaxHp = previewObjective?.Hp.Floor().ToString() ?? objective?.MaxHp.ToString() ?? "0",
                DefeatReason = battle?.State.DefeatReason is { } reason ? InvasionDefeatDescription(reason) : ""
            };
        }
    }

    private static string DescribeBlessing(OwnedBlessing owned, ContentBundle definitions)
        => string.Join(" / ", BlessingModifiers(owned, definitions).Select(component =>
        {
            var amount = component.Stage == ModifierStage.Multiplicative ? component.Amount : component.Amount;
            return component.ParameterId + " " + (component.Stage == ModifierStage.Fixed ? amount.ToDisplayString() : component.Stage == ModifierStage.Additive ? (amount * 100).ToDisplayString() + "%" : "×" + amount.ToDisplayString());
        }));
    private static string Roman(int rank) => rank switch { 1 => "I", 2 => "II", 3 => "III", _ => "?" };
    private static string LevelName(string id) => id switch { "vitality" => "活力", "firepower" => "火力", "rapid" => "連撃", "mobility" => "機動", "reach" => "間合い", "defense" => "防備", _ => id };
    private static string LevelDescription(string id) => id switch
    {
        "vitality" => "最大HP+10%、増加分を回復", "firepower" => "攻撃威力+8%", "rapid" => "攻撃間隔−5%",
        "mobility" => "移動速度+10%", "reach" => "射程+5%", "defense" => "物理・魔法防御+5%", _ => ""
    };
}
