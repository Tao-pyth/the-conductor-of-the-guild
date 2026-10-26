using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;

namespace Guild.Application;

public sealed partial class GameController
{
    // Sortie and its read-only preview must use the same saved equipment and preparation steps.
    private List<BattleUnitSetup> BuildOwnedBattleSetups(ContentBundle definitions)
        => profile.Adventurers.Select(actor =>
        {
            var weapon = profile.Weapons.Single(x => x.Id == actor.WeaponId && x.OwnerId == actor.Id);
            return new BattleUnitSetup
            {
                Id = actor.Id, Name = actor.Name, Stats = EffectiveStats(actor, true, definitions),
                WeaponTemplateId = weapon.TemplateId, BasePower = weapon.BasePower,
                AttackIntervalMs = weapon.AttackIntervalMs, RangeCenti = weapon.RangeCenti,
                SkillIds = weapon.SkillIds.ToList(), CurrentHp = actor.Hp
            };
        }).ToList();

    private void ApplyOwnedBattleModifiers(BattleSimulation simulation, ContentBundle definitions)
    {
        foreach (var actor in profile.Adventurers)
        {
            var unit = simulation.State.Units.Single(x => x.Id == actor.Id);
            unit.MaxHpMultiplier = 1 - new Rational(actor.WeaknessStacks * 5, 100);
            simulation.RecalculateUnit(actor.Id);
            ApplyBattleModifiers(actor, unit, definitions, simulation);
        }
    }

    private static readonly HashSet<string> SupportedBattleParameters = new(StringComparer.Ordinal)
    {
        "str", "vit", "dex", "agi", "int", "max-hp", "physical-defense", "magical-defense",
        "attack-power", "attack-interval-ms", "move-centi-per-second"
    };

    /// <summary>Registration of a parameter does not grant an implemented modifier action.</summary>
    private static void ValidateSupportedContent(ContentBundle definitions)
    {
        definitions.EnsureValid();
        foreach (var component in definitions.Blessings.SelectMany(b => b.Modifiers))
            ValidateSupportedModifier(component, definitions);
    }

    private static void ValidateSupportedModifier(ModifierDefinition component, ContentBundle definitions)
    {
        if (component.Condition is not null)
            throw new InvalidDataException(DisplayText.Format("app.error.unsupportedConditionalModifier", component.Id));
        if (!SupportedBattleParameters.Contains(component.ParameterId))
            throw new InvalidDataException(DisplayText.Format("app.error.unsupportedModifierParameter", component.Id, component.ParameterId));
        if (definitions.Parameters.All(p => p.Id != component.ParameterId))
            throw new InvalidDataException(DisplayText.Format("app.error.missingModifierParameter", component.Id));
        if ((component.SkillId is not null || component.Slot is not null) && component.ParameterId != "attack-power")
            throw new InvalidDataException(DisplayText.Format("app.error.unsupportedTargetedModifier", component.Id));
    }

    private static ModifierDefinition[] ActiveBlessingComponents(AdventurerState actor, ContentBundle definitions)
    {
        var components = actor.Blessings.SelectMany(owned => BlessingModifiers(owned, definitions))
            .GroupBy(component => component.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        foreach (var component in components) ValidateSupportedModifier(component, definitions);
        return components;
    }

    private static Rational EvaluatePreparedParameter(string id, Rational basis, ContentBundle definitions,
        IEnumerable<ModifierDefinition> components)
    {
        var parameter = definitions.Parameters.FirstOrDefault(p => p.Id == id);
        // Missing unmodified registry entries do not invent a new content requirement.
        return parameter is null ? basis : ParameterCalculator.Evaluate(basis, parameter, components);
    }

    private Rational PreparedMaximumHp(AdventurerState actor, ContentBundle definitions, bool includeNext)
    {
        var stats = EffectiveStats(actor, includeNext, definitions);
        var components = ActiveBlessingComponents(actor, definitions)
            .Where(component => component.SkillId is null && component.Slot is null);
        return EvaluatePreparedParameter("max-hp", GameFormulas.MaximumHp(stats.Vit), definitions, components);
    }

    private void ApplyBattleModifiers(AdventurerState actor, CombatantState unit, ContentBundle definitions,
        BattleSimulation simulation)
    {
        var components = ActiveBlessingComponents(actor, definitions);
        var common = components.Where(component => component.SkillId is null && component.Slot is null).ToArray();
        var rawAttack = GameFormulas.AttackPower(unit.WeaponType, unit.BasePower, unit.Stats.Str, unit.Stats.Dex, unit.Stats.Int);
        unit.PreparedMaxHp = EvaluatePreparedParameter("max-hp", GameFormulas.MaximumHp(unit.Stats.Vit), definitions, common);
        unit.PreparedAttackPower = EvaluatePreparedParameter("attack-power", rawAttack, definitions, common);
        unit.PreparedAttackIntervalMs = EvaluatePreparedParameter("attack-interval-ms",
            GameFormulas.AttackInterval(unit.BaseAttackIntervalMs, unit.Stats.Agi), definitions, common);
        unit.PreparedMoveCentiPerSecond = EvaluatePreparedParameter("move-centi-per-second",
            GameFormulas.MovementCentiPerSecond(unit.Stats.Agi), definitions, common);
        unit.PhysicalDefense = EvaluatePreparedParameter("physical-defense", unit.Stats.Vit, definitions, common);
        unit.MagicalDefense = EvaluatePreparedParameter("magical-defense", unit.Stats.Int, definitions, common);

        const string preparedPrefix = "prepared-attack:";
        unit.SkillModifiers.RemoveAll(modifier => modifier.Id.StartsWith(preparedPrefix, StringComparison.Ordinal));
        foreach (var slot in unit.Skills)
        {
            var targeted = components.Where(component => component.ParameterId == "attack-power"
                && (component.SkillId == slot.SkillId || component.Slot == slot.Slot)).ToArray();
            if (targeted.Length == 0) continue;
            // A component matching both ID and real slot enters the shared pipeline exactly once.
            var applicable = common.Concat(targeted).GroupBy(component => component.Id, StringComparer.Ordinal)
                .Select(group => group.First());
            var targetPower = EvaluatePreparedParameter("attack-power", rawAttack, definitions, applicable);
            var commonPower = unit.PreparedAttackPower.Value;
            if (commonPower == 0 && targetPower != 0)
                throw new InvalidDataException(DisplayText.Get("app.error.unsupportedZeroAttackCorrection"));
            unit.SkillModifiers.Add(new SkillModifier
            {
                Id = preparedPrefix + actor.Id + ":" + slot.Slot,
                Slot = slot.Slot,
                AttackOnly = true,
                PowerMultiplier = commonPower == 0 ? Rational.One : targetPower / commonPower
            });
        }

        // The constructor initially clamps HP against unmodified stats. Restore persisted HP before
        // applying the permanent reference and the distinct weakness/Lv temporary stage.
        unit.Hp = actor.Hp;
        if (!simulation.RecalculateUnit(unit.Id)) throw new InvalidOperationException(DisplayText.Get("app.error.cannotRecalculateUnit"));
    }
}
