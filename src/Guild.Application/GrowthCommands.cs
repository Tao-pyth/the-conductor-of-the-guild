using System.Numerics;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;

namespace Guild.Application;

public sealed partial class GameController
{
    private string ReturnToGuild()
    {
        if (session?.Phase is not ("Victory" or "Defeat") || battle is null) throw new InvalidOperationException(DisplayText.Get("app.error.returnOnlyAfterResult"));
        var plan = CalculateSettlement();
        foreach (var result in plan.Actors)
        {
            var actor = result.Actor;
            actor.WeaknessStacks = result.Weakness;
            actor.Hp = result.Hp;
            var dispatched = session.Economy?.Dispatch?.RankExperience.ContainsKey(actor.Id) == true;
            if (!result.Participated && !dispatched) continue;
            if (result.Participated) actor.NextMatchChanges.Clear();
            actor.Experience = result.Experience;
            while (actor.Rank < result.Rank)
            {
                actor.Rank++;
                profile.PendingEvents.Add(new() { Id = Guid.NewGuid().ToString("N"), Sequence = profile.NextEventSequence++, ActorId = actor.Id, Rank = actor.Rank });
            }
        }
        // Explicit technical-fixture rewards; this is not adoption of the production balance table.
        profile.Resources += plan.ResourceGain;
        if (plan.Victory)
        {
            profile.Weapons.Add(CreateWeapon("guild"));
            if (!profile.FirstClear) { profile.FirstClear = true; profile.RerollTickets++; profile.PurificationDrops++; }
        }
        SettleEconomy(plan.Victory, plan.Victory ? battle.TotalWaves : Math.Max(0, battle.State.Wave - 1), battle.TotalWaves);
        var description = session.Economy is null ? plan.Description : plan.Description + "\n" + profile.Economy.LastReport;
        session = null; battle = null; accumulator = 0; paused = false;
        return description;
    }

    private RankEventState CurrentEvent()
    {
        RequireGuild();
        return PeekGrowthEvent() ?? throw new InvalidOperationException(DisplayText.Get("app.error.noPendingEvent"));
    }

    private string BeginEvent(GameCommand command)
    {
        var pending = CurrentEvent();
        if (command.ActorId is not null && command.ActorId != pending.ActorId
            || command.TargetId is not null && command.TargetId != pending.Id)
            throw new InvalidOperationException(DisplayText.Get("app.error.growthRecipientChanged"));
        var actor = Actor(pending.ActorId);
        var kind = command.Value ?? "blessing";
        var unavailable = GrowthEventUnavailableReason(pending, actor, kind);
        if (unavailable is not null) throw new InvalidOperationException(unavailable);
        var random = Pcg32.Restore(profile.GrowthRng);
        if (kind == "blessing")
        {
            var choices = DrawBlessings(actor, content, random);
            if (choices.Count == 0) throw new InvalidOperationException(DisplayText.Get("app.growth.noEligibleBlessing"));
            pending.Candidates = choices;
        }
        if (kind == "random") pending.FixedRandomResult = RollRandomResult(actor, random);
        pending.DefinitionJson = content.ToJson();
        pending.Kind = kind;
        profile.GrowthRng = random.Capture();
        return DisplayText.Format("history.event.selected", actor.Name, pending.Rank, EventKindName(kind));
    }

    private static readonly int[] RarityWeights = [100, 80, 64, 50, 40, 25, 15, 8, 3, 1];

    private static List<string> DrawBlessings(AdventurerState actor, ContentBundle definitions, Pcg32 random)
    {
        var eligible = definitions.Blessings.Where(x => x.Acquisition == "normal" && actor.Blessings.All(b => b.Id != x.Id || b.Rank < 3)).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
        var count = Math.Min(3 + actor.DiscardBonus, eligible.Count);
        if (count == 0) return [];
        var tagWeights = new Dictionary<string, Rational>(StringComparer.Ordinal);
        foreach (var owned in actor.Blessings)
        {
            var definition = definitions.Blessings.FirstOrDefault(x => x.Id == owned.Id);
            if (definition is null || definition.Tags.Count == 0) continue;
            foreach (var tag in definition.Tags) tagWeights[tag] = tagWeights.GetValueOrDefault(tag) + new Rational(1, definition.Tags.Count);
        }
        string? major = null;
        if (tagWeights.Count > 0)
        {
            var maximum = tagWeights.Values.Max();
            var ties = tagWeights.Where(x => x.Value == maximum).Select(x => x.Key).Order(StringComparer.Ordinal).ToArray();
            major = ties[random.NextInt(ties.Length)];
        }
        var result = new List<string>();
        void DrawFrom(List<BlessingDefinition> pool, int amount)
        {
            for (var i = 0; i < amount && pool.Count > 0; i++)
            {
                int Weight(BlessingDefinition definition)
                {
                    var owned = actor.Blessings.FirstOrDefault(x => x.Id == definition.Id);
                    return RarityWeights[definition.Rarity - 1] * (owned is null ? 10 : owned.Rank == 1 ? 6 : 3);
                }
                var roll = random.NextInt(pool.Sum(Weight)); var selected = pool[0];
                foreach (var candidate in pool) { roll -= Weight(candidate); if (roll < 0) { selected = candidate; break; } }
                result.Add(selected.Id); pool.Remove(selected); eligible.Remove(selected);
            }
        }
        if (major is not null)
        {
            var main = eligible.Where(x => x.Tags.Contains(major)).ToList();
            var minor = eligible.Where(x => !x.Tags.Contains(major)).ToList();
            var mainCount = (int)Math.Round(.6 * count, MidpointRounding.ToEven);
            DrawFrom(main, mainCount); DrawFrom(minor, count - mainCount);
        }
        DrawFrom(eligible.ToList(), count - result.Count);
        return result;
    }

    private string ChooseBlessing(GameCommand command)
    {
        var pending = CurrentEvent();
        if (pending.Kind is not ("blessing" or "random") || !pending.Candidates.Contains(command.TargetId ?? "")) throw new InvalidOperationException(DisplayText.Get("app.error.chooseOfferedBlessing"));
        var actor = Actor(pending.ActorId);
        var definitions = EventContent(pending);
        var oldRank = actor.Blessings.FirstOrDefault(x => x.Id == command.TargetId)?.Rank ?? 0;
        var removed = oldRank == 0 && actor.Blessings.Count >= 6
            ? actor.Blessings.FirstOrDefault(x => x.Id == command.Value?.Split(' ', 2)[0]) : null;
        AcquireBlessing(actor, command.TargetId!, command.Value);
        if (pending.Kind == "blessing") actor.DiscardBonus = 0;
        CompleteEvent(pending);
        return DescribeAcquisition(actor.Name, definitions.Blessings.Single(x => x.Id == command.TargetId).Name,
            oldRank, oldRank + 1, removed is null ? null : definitions.Blessings.Single(x => x.Id == removed.Id).Name, removed?.Rank ?? 0);
    }

    private static void AcquireBlessing(AdventurerState actor, string blessingId, string? replacement = null)
    {
        var owned = actor.Blessings.FirstOrDefault(x => x.Id == blessingId);
        if (owned is not null)
        {
            if (owned.Rank >= 3) throw new InvalidOperationException(DisplayText.Get("app.error.blessingRankCap"));
            owned.Rank++; return;
        }
        if (actor.Blessings.Count >= 6)
        {
            var replacementId = replacement?.Split(' ', 2)[0];
            var removed = actor.Blessings.FirstOrDefault(x => x.Id == replacementId);
            if (removed is null) throw new InvalidOperationException(DisplayText.Get("app.error.chooseReplacedBlessing"));
            actor.Blessings.Remove(removed);
        }
        actor.Blessings.Add(new() { Id = blessingId });
    }

    private string DiscardBlessings()
    {
        var pending = CurrentEvent();
        if (pending.Kind != "blessing" || pending.Candidates.Count == 0) throw new InvalidOperationException(DisplayText.Get("app.error.noDiscardableChoices"));
        var actor = Actor(pending.ActorId);
        actor.DiscardBonus = Math.Min(5, actor.DiscardBonus + 1); CompleteEvent(pending);
        return DisplayText.Format("history.blessing.discard", actor.Name, actor.DiscardBonus);
    }

    private string RerollBlessings()
    {
        var pending = CurrentEvent();
        if (pending.Kind != "blessing" || pending.Rerolled || profile.RerollTickets < 1) throw new InvalidOperationException(DisplayText.Get("app.error.noRerollAvailable"));
        var random = Pcg32.Restore(profile.GrowthRng);
        pending.Candidates = DrawBlessings(Actor(pending.ActorId), EventContent(pending), random);
        pending.Rerolled = true; profile.RerollTickets--; profile.GrowthRng = random.Capture();
        return DisplayText.Format("history.blessing.reroll", Actor(pending.ActorId).Name);
    }

    private string TransferItem(GameCommand command)
    {
        var pending = CurrentEvent();
        if (pending.Kind != "transfer") throw new InvalidOperationException(DisplayText.Get("app.error.chooseTransferEvent"));
        var actor = Actor(pending.ActorId);
        var weapon = profile.Weapons.FirstOrDefault(x => x.Id == command.TargetId && x.OwnerId == "guild") ?? throw new InvalidOperationException(DisplayText.Get("app.error.chooseGuildItem"));
        weapon.OwnerId = actor.Id; CompleteEvent(pending);
        return DisplayText.Format("app.transfer.completed", weapon.Name, actor.Name);
    }

    private string EquipItem(GameCommand command)
    {
        RequireGuild();
        var actor = Actor(command.ActorId);
        var weapon = profile.Weapons.FirstOrDefault(x => x.Id == command.TargetId && x.OwnerId == actor.Id) ?? throw new InvalidOperationException(DisplayText.Get("app.error.chooseOwnedItem"));
        actor.WeaponId = weapon.Id;
        return DisplayText.Format("app.equipment.completed", actor.Name, weapon.Name);
    }

    private string BuyWeapon(GameCommand command)
    {
        RequireGuild();
        var templateId = command.Value;
        if (templateId is not null && content.WeaponTemplates.All(x => x.Id != templateId)) throw new InvalidOperationException(DisplayText.Get("app.error.noWeaponTypeToBuy"));
        var budget = templateId is null ? new Rational(100) : content.WeaponTemplates.Single(x => x.Id == templateId).Budget;
        var price = (budget * 10).Ceiling();
        if (profile.Resources < price) throw new InvalidOperationException(DisplayText.Format("app.error.weaponResources", price));
        var weapon = CreateWeapon("guild", templateId);
        profile.Weapons.Add(weapon); profile.Resources -= price;
        return DisplayText.Format("app.weapon.purchaseCompleted", price, weapon.Name);
    }

    private static List<RecipeDefinition> EligibleRecipes(AdventurerState actor, ContentBundle definitions)
        => definitions.Recipes.Where(recipe => actor.Blessings.Any(x => x.Id == recipe.FirstBlessingId && x.Rank == 3)
            && actor.Blessings.Any(x => x.Id == recipe.SecondBlessingId && x.Rank == 3)
            && actor.Blessings.All(x => x.Id != recipe.ResultBlessingId || x.Rank < 3)).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();

    private string Fuse(GameCommand command)
    {
        var pending = CurrentEvent();
        if (pending.Kind != "fusion") throw new InvalidOperationException(DisplayText.Get("app.error.chooseFusionEvent"));
        var definitions = EventContent(pending); var actor = Actor(pending.ActorId);
        var recipe = EligibleRecipes(actor, definitions).FirstOrDefault(x => x.Id == command.TargetId) ?? throw new InvalidOperationException(DisplayText.Get("app.error.invalidFusion"));
        var inherited = new Dictionary<string, Rational>(StringComparer.Ordinal);
        foreach (var parentId in new[] { recipe.FirstBlessingId, recipe.SecondBlessingId })
        {
            var parent = actor.Blessings.Single(x => x.Id == parentId);
            foreach (var component in BlessingModifiers(parent, definitions))
            {
                var key = ModifierKey(component);
                var contribution = component.Stage == ModifierStage.Multiplicative ? component.Amount - 1 : component.Amount;
                inherited[key] = inherited.GetValueOrDefault(key) + contribution * new Rational(2, 3);
            }
            actor.Blessings.Remove(parent);
        }
        var existing = actor.Blessings.FirstOrDefault(x => x.Id == recipe.ResultBlessingId);
        if (existing is null) actor.Blessings.Add(new() { Id = recipe.ResultBlessingId, InheritedModifiers = inherited });
        else existing.Rank++;
        profile.DiscoveredRecipes.Add(recipe.Id); CompleteEvent(pending);
        var name = definitions.Blessings.Single(x => x.Id == recipe.ResultBlessingId).Name;
        return DisplayText.Format("history.blessing.fuse", actor.Name,
            definitions.Blessings.Single(x => x.Id == recipe.FirstBlessingId).Name,
            definitions.Blessings.Single(x => x.Id == recipe.SecondBlessingId).Name,
            name, Roman(actor.Blessings.Single(x => x.Id == recipe.ResultBlessingId).Rank));
    }

    private sealed record RandomOutcome(string Kind, string? Id, string? Stat, int Delta);
    private string RollRandomResult(AdventurerState actor, Pcg32 random)
    {
        var category = random.NextInt(100);
        if (category < 35) return Serialize(new RandomOutcome("equipment", null, null, 0));
        var stats = new[] { "str", "vit", "dex", "agi", "int" };
        var stat = stats[random.NextInt(5)];
        var amount = random.NextInt(100); var delta = amount < 1 ? -2 : amount < 5 ? -1 : amount < 15 ? 0 : amount < 90 ? 1 : 2;
        if (category < 70) return Serialize(new RandomOutcome("next", null, stat, delta));
        if (category is >= 90 and < 99) return Serialize(new RandomOutcome("stat", null, stat, delta));
        var route = category < 90 ? "normal" : "unique";
        var eligible = content.Blessings.Where(x => x.Acquisition == route && actor.Blessings.All(b => b.Id != x.Id || b.Rank < 3)).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return Serialize(new RandomOutcome("blessing", eligible.Length == 0 ? null : eligible[random.NextInt(eligible.Length)].Id, null, 0));
    }

    private string ResolveRandom()
    {
        var pending = CurrentEvent();
        if (pending.Kind != "random" || pending.FixedRandomResult is null) throw new InvalidOperationException(DisplayText.Get("app.error.chooseRandomEvent"));
        var outcome = Deserialize<RandomOutcome>(pending.FixedRandomResult); var actor = Actor(pending.ActorId);
        var message = DisplayText.Format("app.random.noChange", actor.Name);
        switch (outcome.Kind)
        {
            case "equipment":
                var weapon = CreateWeapon(actor.Id); profile.Weapons.Add(weapon); message = DisplayText.Format("app.random.weaponAcquired", actor.Name, weapon.Name); break;
            case "next":
                actor.NextMatchChanges[outcome.Stat!] = actor.NextMatchChanges.GetValueOrDefault(outcome.Stat!) + outcome.Delta;
                message = DisplayText.Format("history.random.next", actor.Name, DisplayText.Get("app.parameter." + outcome.Stat), outcome.Delta); break;
            case "stat":
                actor.Growth = ChangeStat(actor.Growth, outcome.Stat!, outcome.Delta);
                message = DisplayText.Format("history.random.stat", actor.Name, DisplayText.Get("app.parameter." + outcome.Stat), outcome.Delta); break;
            case "blessing" when outcome.Id is not null:
                pending.RandomRevealed = true; pending.Candidates = [outcome.Id];
                return DisplayText.Format("app.random.blessingsRevealed", actor.Name,
                    EventContent(pending).Blessings.Single(x => x.Id == outcome.Id).Name);
        }
        CompleteEvent(pending); return message;
    }

    private string DeclineRandom()
    {
        var pending = CurrentEvent();
        if (pending.Kind != "random" || !pending.RandomRevealed || pending.Candidates.Count == 0) throw new InvalidOperationException(DisplayText.Get("app.error.noRandomBlessingToDecline"));
        var actor = Actor(pending.ActorId);
        var name = EventContent(pending).Blessings.Single(x => x.Id == pending.Candidates[0]).Name;
        CompleteEvent(pending);
        return DisplayText.Format("history.blessing.decline", actor.Name, name);
    }

    private void CompleteEvent(RankEventState pending)
    {
        profile.PendingEvents.Remove(pending);
        foreach (var recipe in content.Recipes)
            if (profile.Adventurers.Any(x => x.Blessings.Any(b => b.Id == recipe.ResultBlessingId))) profile.DiscoveredRecipes.Add(recipe.Id);
    }

    private static BattleStats ChangeStat(BattleStats basis, string stat, BigInteger delta) => stat switch
    {
        "str" => basis with { Str = basis.Str + delta }, "vit" => basis with { Vit = basis.Vit + delta },
        "dex" => basis with { Dex = basis.Dex + delta }, "agi" => basis with { Agi = basis.Agi + delta },
        "int" => basis with { Int = basis.Int + delta }, _ => throw new InvalidOperationException(DisplayText.Get("app.error.unknownAttribute"))
    };

    private BattleStats EffectiveStats(AdventurerState actor, bool includeNext = true, ContentBundle? definitions = null)
    {
        definitions ??= content;
        var values = new Dictionary<string, BigInteger> { ["str"] = actor.Stats.Str + actor.Growth.Str, ["vit"] = actor.Stats.Vit + actor.Growth.Vit,
            ["dex"] = actor.Stats.Dex + actor.Growth.Dex, ["agi"] = actor.Stats.Agi + actor.Growth.Agi, ["int"] = actor.Stats.Int + actor.Growth.Int };
        var modifiers = actor.Blessings.SelectMany(x => BlessingModifiers(x, definitions)).Where(x => x.Condition is null && x.SkillId is null && x.Slot is null).ToArray();
        foreach (var stat in values.Keys.ToArray())
        {
            var definition = definitions.Parameters.FirstOrDefault(x => x.Id.Equals(stat, StringComparison.OrdinalIgnoreCase));
            Rational value = values[stat];
            if (definition is not null) value = ParameterCalculator.Evaluate(value, definition, modifiers);
            if (includeNext) value += actor.NextMatchChanges.GetValueOrDefault(stat);
            values[stat] = BigInteger.Max(1, value.Floor());
        }
        return new(values["str"], values["vit"], values["dex"], values["agi"], values["int"]);
    }

    private BigInteger PersistentMaximumHp(AdventurerState actor) => PreparedMaximumHp(actor, content, false).Floor();
    private static string ModifierKey(ModifierDefinition component) => string.Join('|', component.ParameterId, component.Stage.ToString(), component.Condition ?? "", component.SkillId ?? "", component.Slot ?? "");
    private static IEnumerable<ModifierDefinition> BlessingModifiers(OwnedBlessing owned, ContentBundle definitions)
    {
        if (owned.InheritedModifiers is not null)
        {
            var factor = owned.Rank == 1 ? Rational.One : owned.Rank == 2 ? new Rational(3, 2) : new Rational(2);
            foreach (var entry in owned.InheritedModifiers)
            {
                var fields = entry.Key.Split('|');
                var stage = Enum.Parse<ModifierStage>(fields[1]);
                yield return new ModifierDefinition { Id = owned.Id + ":" + entry.Key, ParameterId = fields[0], Stage = stage,
                    Amount = stage == ModifierStage.Multiplicative ? 1 + entry.Value * factor : entry.Value * factor,
                    Condition = fields[2].Length == 0 ? null : fields[2], SkillId = fields[3].Length == 0 ? null : fields[3], Slot = fields[4].Length == 0 ? null : fields[4] };
            }
            yield break;
        }
        var definition = definitions.Blessings.FirstOrDefault(x => x.Id == owned.Id);
        if (definition is null) throw new InvalidDataException(DisplayText.Format("app.error.missingBlessingDefinition", owned.Id));
        foreach (var modifier in definition.Modifiers)
            yield return modifier with { Id = owned.Id + ":" + modifier.Id,
                Amount = modifier.Stage == ModifierStage.Multiplicative ? 1 + (modifier.Amount - 1) * owned.Rank : modifier.Amount * owned.Rank };
    }

    private static ContentBundle EventContent(RankEventState pending) => ContentBundle.FromJson(pending.DefinitionJson ?? throw new InvalidDataException(DisplayText.Get("app.error.missingFrozenEventContent")));
}
