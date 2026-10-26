using System.Numerics;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;

namespace Guild.Application;

public sealed partial class GameController
{
    private sealed record ActorSettlement(AdventurerState Actor, BigInteger Hp, int Weakness,
        BigInteger Rank, BigInteger Experience, bool Participated, AdventurerResultView View);
    private sealed record MatchSettlement(bool Victory, BigInteger ResourceGain, BigInteger ExperienceGain,
        IReadOnlyList<ActorSettlement> Actors, MatchResultView View, string Description);

    // Both the result screen and ReturnToGuild consume this plan. It owns no saved state or RNG.
    private MatchSettlement CalculateSettlement()
    {
        if (session?.Phase is not ("Victory" or "Defeat") || battle is null)
            throw new InvalidOperationException(DisplayText.Get("history.result.unavailable"));
        var won = session.Phase == "Victory";
        var participants = battle.State.Units.Where(x => !x.IsEnemy && x.EverDeployed).ToArray();
        var stage = session.Current.Setup.Content.Stages.Single(x => x.Id == session.Current.Setup.StageId);
        var total = battle.TotalWaves;
        var completed = won ? total : Math.Max(0, battle.State.Wave - 1);
        Rational difference = participants.Length == 0 ? 0 : new Rational(stage.RewardRank)
            - new Rational(participants.Aggregate(BigInteger.Zero, (sum, x) => sum + session.StartingRanks[x.Id]), participants.Length);
        var coefficient = Rational.Clamp(1 + new Rational(1, 20) * difference, new Rational(1, 4), 2);
        var xp = ((100 + 20 * new Rational(stage.RewardRank)) * coefficient
            * (won ? Rational.One : new Rational(completed, 4 * total))).Floor();
        var reward = won ? new BigInteger(battle.IsInvasion ? 300 : 1000) : new BigInteger(100 * completed);
        var actors = profile.Adventurers.OrderBy(x => x.Id, StringComparer.Ordinal).Select(actor =>
        {
            var dispatched = session.Economy?.Dispatch?.RankExperience.ContainsKey(actor.Id) == true;
            var unit = battle.State.Units.Single(x => x.Id == actor.Id);
            var incapacitated = unit.EverDeployed && unit.Hp == 0;
            var weakness = dispatched ? actor.WeaknessStacks : incapacitated ? actor.WeaknessStacks + 1 : 0;
            var returned = Clone(actor);
            returned.WeaknessStacks = weakness;
            var maximum = PersistentMaximumHp(returned);
            var stayedAtGuild = battle.IsInvasion && !unit.EverDeployed;
            var finalHp = stayedAtGuild ? actor.Hp : unit.Hp;
            var hp = dispatched ? actor.Hp : incapacitated ? BigInteger.One
                : BigInteger.Min(maximum, finalHp + new Rational(maximum, 2).Ceiling());
            var rank = actor.Rank;
            var experience = actor.Experience + (dispatched ? session.Economy!.Dispatch!.RankExperience[actor.Id] : unit.EverDeployed ? xp : BigInteger.Zero);
            if (unit.EverDeployed || dispatched)
                while (experience >= GameFormulas.RankExperienceForNext(rank))
                {
                    experience -= GameFormulas.RankExperienceForNext(rank);
                    rank++;
                }
            // An invasion reserve never used the battle's next-sortie/temporary modifiers.
            // Show all three health values against its persistent guild state instead.
            var initial = stayedAtGuild || dispatched ? actor.Hp
                : session.MatchStart?.Units.FirstOrDefault(x => x.Id == actor.Id)?.Hp ?? actor.Hp;
            var endMaximum = stayedAtGuild || dispatched ? PersistentMaximumHp(actor) : unit.MaxHp;
            if (dispatched) finalHp = actor.Hp;
            var view = new AdventurerResultView(actor.Id, actor.Name, unit.WeaponType, unit.EverDeployed,
                incapacitated, initial.ToString(), finalHp.ToString(), endMaximum.ToString(), hp.ToString(),
                maximum.ToString(), session.StartingRanks[actor.Id].ToString(), rank.ToString(), weakness) { WasDispatched = dispatched };
            return new ActorSettlement(actor, hp, weakness, rank, experience, unit.EverDeployed, view);
        }).ToArray();
        var mode = battle.IsInvasion ? "invasion" : "defense";
        var resultView = new MatchResultView(session.MatchId, mode, won, battle.State.Wave, total,
            reward.ToString(), xp.ToString(), profile.Adventurers.Select(a => actors.Single(p => p.Actor.Id == a.Id).View).ToArray());
        var outcome = DisplayText.Format(won ? "history.match.victory" : "history.match.defeat",
            DisplayText.Get(battle.IsInvasion ? "history.unit.section" : "history.unit.wave"), battle.State.Wave);
        var description = DisplayText.Format("history.match.summary", outcome, stage.Name, reward, xp);
        return new(won, reward, xp, actors, resultView, description);
    }

    private MatchResultView? BuildMatchResultView()
        => session?.Phase is "Victory" or "Defeat" ? CalculateSettlement().View : null;
}
