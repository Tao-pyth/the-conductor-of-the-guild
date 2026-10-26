using System.Numerics;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;

namespace Guild.Application;

public sealed partial class GameController
{
    private BattleSimulation? decisionPreview;
    private long decisionPreviewRevision = -1;
    private ContentBundle? decisionPreviewContent;

    private BattleSimulation DecisionSimulation()
    {
        if (battle is not null) return battle;
        if (decisionPreview is not null && decisionPreviewRevision == revision
            && ReferenceEquals(decisionPreviewContent, content)) return decisionPreview;
        var stage = content.Stages.FirstOrDefault(s => s.Mode != "invasion") ?? content.Stages[0];
        // This simulation owns its clone and local PCG. Never Step/Capture or attach it to a session.
        var preview = new BattleSimulation(new()
        {
            MatchId = "decision-preview", AttemptId = "decision-preview", Seed = 42,
            Content = content, StageId = stage.Id, InitialResources = stage.InitialResources,
            DefenseHp = stage.DefenseHp, Units = BuildOwnedBattleSetups(content),
            PartyIds = stage.Mode == "invasion" ? profile.Adventurers.Take(10).Select(a => a.Id).ToList() : null
        });
        ApplyOwnedBattleModifiers(preview, content);
        decisionPreview = preview;
        decisionPreviewRevision = revision;
        decisionPreviewContent = content;
        return preview;
    }

    private AdventurerPerformanceView BuildPerformanceView(AdventurerState actor, CombatantState unit,
        UnitDecisionValues values, ContentBundle definitions)
        => new(DisplayText.Get(battle is null ? "app.performance.reference" : "app.performance.current"),
            values.AttackPower.ToDisplayString(), values.PhysicalDefense.ToDisplayString(),
            values.MagicalDefense.ToDisplayString(), (values.AttackIntervalMs / 1000).ToDisplayString(),
            (values.MovementCentiPerSecond / 100).ToDisplayString(),
            new Rational(values.WeaponRangeCenti, 100).ToDisplayString(), actor.WeaknessStacks,
            unit.Skills.Select(slot =>
            {
                var skill = definitions.Skills.Single(s => s.Id == slot.SkillId);
                return new SkillDetailView(slot.Slot, slot.SkillId, skill.Name, SkillTargetDescription(skill),
                    DescribeSkillEffect(unit, slot, skill), new Rational(skill.RangeCenti, 100).ToDisplayString(),
                    (CooldownMath.Calculate(skill.CooldownMs, skill.IsUltimate, slot.Cooldown) / 1000).ToDisplayString(), skill.IsUltimate);
            }).ToArray())
        {
            AttributeSummary = DescribeAttributes(unit.Stats),
            BaseAttributeSummary = DescribeAttributes(new(actor.Stats.Str + actor.Growth.Str,
                actor.Stats.Vit + actor.Growth.Vit, actor.Stats.Dex + actor.Growth.Dex,
                actor.Stats.Agi + actor.Growth.Agi, actor.Stats.Int + actor.Growth.Int))
        };

    private static string DescribeAttributes(BattleStats stats)
        => DisplayText.Format("app.performance.attributes", stats.Str, stats.Vit, stats.Dex, stats.Agi, stats.Int);

    private static string SkillTargetDescription(SkillDefinition skill)
        => DisplayText.Get(skill.Effects.Any(e => e.Kind == "heal") ? "app.skill.target.heal"
            : skill.Effects.All(e => e.Kind == "status" && e.Status is "defenseup" or "attackup") ? "app.skill.target.self"
            : "app.skill.target.enemy");

    private static string DescribeSkillEffect(CombatantState unit, SkillSlotState slot, SkillDefinition skill)
    {
        var attack = CombatMath.Attack(unit);
        var power = CombatMath.SkillPower(unit.SkillModifiers, skill.Id, slot.Slot);
        var healingPower = CombatMath.SkillPower(unit.SkillModifiers, skill.Id, slot.Slot, healing: true);
        return string.Join(" / ", skill.Effects.Select(effect => effect.Kind switch
        {
            "damage" => DisplayText.Format("app.skill.effect.damage", DisplayText.Get(unit.WeaponType is "staff" or "杖" ? "app.damage.magical" : "app.damage.physical"),
                (attack * effect.Amount * power).ToDisplayString(), (effect.Amount * power).ToDisplayString()),
            "heal" => DisplayText.Format("app.skill.effect.heal", BigInteger.Max(0, ((5 + unit.Stats.Int) * effect.Amount * healingPower).Floor()), (effect.Amount * healingPower).ToDisplayString()),
            "status" => DescribeStatusEffect(effect, attack),
            _ => effect.Kind
        }));
    }

    private static string DescribeStatusEffect(EffectDefinition effect, Rational attack)
    {
        var duration = new Rational(effect.DurationMs, 1000).ToDisplayString();
        return effect.Status switch
        {
            "burn" or "poison" => DisplayText.Format("app.skill.effect.periodic", DisplayText.Get(effect.Status == "burn" ? "app.status.burn" : "app.status.poison"),
                duration, new Rational(effect.TickMs, 1000).ToDisplayString(), (attack * effect.Amount).ToDisplayString(), effect.Amount.ToDisplayString()),
            "slow" => DisplayText.Format("app.skill.effect.slow", (effect.Amount * 100).ToDisplayString(), duration),
            "stun" => DisplayText.Format("app.skill.effect.stun", duration),
            "attackup" => DisplayText.Format("app.skill.effect.attackup", (effect.Amount * 100).ToDisplayString(), duration),
            "defenseup" => DisplayText.Format("app.skill.effect.defenseup", (effect.Amount * 100).ToDisplayString(), duration),
            _ => DisplayText.Format("app.skill.effect.duration", effect.Status, duration)
        };
    }

    private static BigInteger DeploymentPrice(AdventurerState actor)
        => BigInteger.Min(250, 25 * (1 + actor.Rank / 10));

    private PreparationInfoView? BuildPreparationInfoView(AdventurerState actor, CombatantState unit)
    {
        if (battle is null || session is null) return null;
        var preparing = session.Phase == "Preparation";
        var dispatched = IsDispatched(actor.Id);
        var inParty = !dispatched && (!battle.IsInvasion || battle.State.Setup.PartyIds!.Contains(actor.Id, StringComparer.Ordinal));
        var cost = DeploymentPrice(actor);
        var reviveCost = (unit.RevivalCount + 1) * unit.Level * 25;
        var atCap = unit.Level >= 10;
        var required = atCap ? BigInteger.Zero : BattleSimulation.LevelCost(unit.Level);
        var missing = atCap ? BigInteger.Zero : BigInteger.Max(0, required - unit.LevelExperience);
        var purchaseCost = missing * 2;
        var commonReason = !preparing ? DisplayText.Get("app.preparation.onlyPreparing") : dispatched ? EconomyText("unavailableActor")
            : !inParty ? DisplayText.Get("app.preparation.notInParty") : null;
        var deployReason = commonReason ?? (unit.Deployed ? DisplayText.Get("app.preparation.alreadyDeployed")
            : unit.Hp <= 0 ? DisplayText.Get("app.preparation.cannotDeployIncapacitated")
            : battle.State.Units.Count(u => !u.IsEnemy && u.Deployed) >= 10 ? DisplayText.Get("app.preparation.deploymentLimit")
            : battle.State.Resources < cost ? DisplayText.Get("app.preparation.insufficientDeploymentResources") : null);
        var reviveReason = commonReason ?? (!unit.Deployed ? DisplayText.Get("app.preparation.onlyDeployedRevive")
            : unit.Hp > 0 ? DisplayText.Get("app.preparation.onlyIncapacitatedRevive")
            : battle.State.Resources < reviveCost ? DisplayText.Get("app.preparation.insufficientRevivalResources") : null);
        var purchaseReason = commonReason ?? (atCap ? DisplayText.Get("app.preparation.levelCap")
            : !unit.Deployed ? DisplayText.Get("app.preparation.onlyDeployedPurchase")
            : battle.IsInvasion && unit.Hp <= 0 ? DisplayText.Get("app.preparation.reviveBeforePurchase")
            : unit.PendingLevelChoices > 0 ? DisplayText.Get("app.preparation.chooseLevelFirst")
            : battle.State.Resources < purchaseCost ? DisplayText.Get("app.preparation.insufficientPurchaseResources") : null);
        return new(cost.ToString(), deployReason is null, deployReason,
            reviveCost.ToString(), reviveReason is null, reviveReason,
            unit.LevelExperience.ToString(), atCap ? DisplayText.Get("app.preparation.cap") : required.ToString(), missing.ToString(),
            atCap ? null : purchaseCost.ToString(), atCap ? null : unit.Level + 1,
            purchaseReason is null, purchaseReason, atCap);
    }

    private RangeGuideView? BuildRangeGuideView(CombatantState unit, UnitDecisionValues values,
        ContentBundle definitions)
    {
        if (battle is null || session?.Phase != "Preparation") return null;
        var invasion = battle.IsInvasion;
        var available = !IsDispatched(unit.Id) && unit.Hp > 0 && (!invasion || battle.State.Setup.PartyIds!.Contains(unit.Id, StringComparer.Ordinal));
        return new(available, invasion ? "invasion" : "defense", battle.State.RuleSetId,
            !invasion && battle.State.RuleSetId is null or "defense-v1",
            values.ActualX.ToDouble(), values.ActualY.ToDouble(),
            invasion ? unit.DeploymentX : values.AnchorX, invasion ? unit.DeploymentY : values.AnchorY,
            values.WatchRadiusCenti / 100d, values.ActionRadiusCenti / 100d, values.WeaponRangeCenti / 100d,
            unit.Skills.Select(slot =>
            {
                var skill = definitions.Skills.Single(s => s.Id == slot.SkillId);
                return new SkillRangeView(slot.Slot, skill.Name, SkillTargetDescription(skill),
                    skill.RangeCenti / 100d, skill.Effects.Any(e => e.Kind == "heal"));
            }).ToArray(), invasion
                ? DisplayText.Get("app.range.invasionExplanation")
                : DisplayText.Get("app.range.defenseExplanation"));
    }

    private NextEncounterView? BuildNextEncounterView(StageDefinition stage)
    {
        if (battle is null || session?.Phase != "Preparation") return null;
        var index = battle.State.Wave;
        if (battle.IsInvasion)
        {
            if (index >= stage.Sections!.Count) return null;
            var section = stage.Sections[index];
            return new("invasion", index + 1, stage.Sections.Count, section.Name, section.Enemies.Count,
                null, null, section.Enemies.Count(e => e.Role == "guard"), section.Enemies.Count(e => e.Role == "turret"),
                section.Enemies.Count(e => e.Role == "gate"), section.Enemies.Count(e => e.Role == "core"),
                new Rational(section.TimeLimitMs, 1000).ToDisplayString());
        }
        if (index >= stage.Waves.Count) return null;
        var wave = stage.Waves[index];
        return new("defense", index + 1, stage.Waves.Count, $"Wave {index + 1}", wave.EnemyCount,
            BigInteger.Max(1, wave.EnemyHp.Floor()).ToString(), wave.AttacksAdventurers, 0, 0, 0, 0, null);
    }
}
