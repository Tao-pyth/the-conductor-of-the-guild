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
                    unit?.Hp == 0, unit?.AnchorX ?? 0, unit?.AnchorY ?? 0, blessings, skills, unit?.TargetPreset ?? "arrival");
            }).ToArray();
            var entities = new List<MapEntityView>();
            if (battle is not null)
            {
                entities.AddRange(battle.State.Units.Where(x => x.Deployed && x.Hp > 0).Select(unit => new MapEntityView(unit.Id,
                    unit.IsEnemy ? "enemy" : "ally", unit.X.ToDouble(), unit.Y.ToDouble(), unit.MaxHp == 0 ? 0 : Math.Clamp(new Rational(unit.Hp, unit.MaxHp).ToDouble(), 0, 1))));
                entities.AddRange(battle.State.Projectiles.Select(x => new MapEntityView(x.Id, "projectile", x.X.ToDouble(), x.Y.ToDouble(), 1)));
            }
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
            var stage = definitions.Stages[0];
            return new(screen, status, profile.Resources.ToString(), battle?.State.Resources.ToString() ?? "0",
                battle?.State.Wave ?? 0, stage.Waves.Count, battle?.State.DefenseHp ?? stage.DefenseHp,
                speed, paused, profile.Capacity, profile.PendingEvents.Count, levelActor ?? (selected ? pending!.ActorId : null),
                selected ? pending!.Kind : null, adventurers, entities, choices, items, history.ToArray(), stage.MapWidth, stage.MapHeight);
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
