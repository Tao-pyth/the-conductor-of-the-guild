using System.Globalization;
using System.Numerics;
using Guild.Domain.Definitions;
using Guild.Domain.Economy;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;

namespace Guild.Application;

public sealed partial class GameController
{
    private static readonly string[] FactionIds = ["C", "M", "K", "T"];
    private static readonly IReadOnlyDictionary<string, BigInteger> FullMatchMaterials = new Dictionary<string, BigInteger>
        { ["wood"] = 8, ["ore"] = 6, ["herb"] = 6, ["crystal"] = 4 };
    private static string EconomyText(string key) => DisplayText.Get("economy.app." + key);
    private static string EconomyFormat(string key, params object?[] args) => DisplayText.Format("economy.app." + key, args);

    // Called only during a new-profile commit or backed-up document migration, never by a view.
    private static void InitializeEconomy(PlayerProfile target, ContentBundle definitions)
    {
        target.Economy ??= new();
        foreach (var material in EconomyCatalog.Default.Materials) target.Economy.Materials.TryAdd(material.Id, 0);
        foreach (var id in FactionIds) target.Economy.Factions.TryAdd(id, new());
        foreach (var delivery in EconomyCatalog.Default.Deliveries.Where(x => x.Kind == "equipment"))
            if (!target.Economy.OfferedEquipment.ContainsKey(delivery.Id))
                target.Economy.OfferedEquipment.Add(delivery.Id, GenerateDeliveryEquipment(target, definitions, delivery));
    }

    private static WeaponState GenerateDeliveryEquipment(PlayerProfile target, ContentBundle definitions, DeliveryDefinition delivery)
    {
        var template = definitions.WeaponTemplates.FirstOrDefault(x => x.WeaponType == delivery.WeaponType)
            ?? throw new InvalidDataException(EconomyText("missingEquipment"));
        var random = Pcg32.Restore(target.Economy.OfferRng);
        var generated = WeaponGenerator.Generate(definitions, random, template.Id);
        target.Economy.OfferRng = random.Capture();
        return new() { Id = Guid.NewGuid().ToString("N"), TemplateId = generated.TemplateId, Name = generated.Name,
            OwnerId = "guild", BasePower = generated.BasePower, AttackIntervalMs = generated.AttackIntervalMs,
            RangeCenti = generated.RangeCenti, SkillIds = generated.SkillIds.ToList(), Seed = generated.Seed,
            GeneratorVersion = generated.GeneratorVersion, DefinitionVersion = definitions.Version };
    }

    private static BigInteger DispatchRankExperience(BigInteger rank, BigInteger missionRank, bool success)
    {
        var coefficient = Rational.Clamp(1 + new Rational(missionRank - rank, 20), new Rational(1, 4), 2);
        return ((100 + 20 * new Rational(missionRank)) * coefficient * new Rational(1, success ? 4 : 8)).Floor();
    }

    private IReadOnlyList<string> DispatchedIds()
        => session?.Economy?.Dispatch is { } active ? active.Members.Select(x => x.ActorId).ToArray()
            : profile.Economy.Reservation?.ActorIds ?? (IReadOnlyList<string>)Array.Empty<string>();

    private bool IsDispatched(string id) => DispatchedIds().Contains(id, StringComparer.Ordinal);

    private void RequireNotDispatched(string? id)
    {
        if (id is not null && IsDispatched(id)) throw new InvalidOperationException(EconomyText("unavailableActor"));
    }

    private string? ReservationUnavailable(IReadOnlyList<string> actorIds)
    {
        if (session is not null) return EconomyText("onlyGuild");
        if (actorIds.Count is < 1 or > 6 || actorIds.Any(string.IsNullOrWhiteSpace)
            || actorIds.Distinct(StringComparer.Ordinal).Count() != actorIds.Count) return EconomyText("selectMembers");
        if (actorIds.Any(id => profile.Adventurers.All(a => a.Id != id || a.Hp <= 0))) return EconomyText("invalidMember");
        if (!profile.Adventurers.Any(a => a.Hp > 0 && !actorIds.Contains(a.Id, StringComparer.Ordinal))) return EconomyText("leaveMember");
        return null;
    }

    private DispatchMember DispatchMemberFor(string id)
    {
        var actor = Actor(id);
        var weapon = profile.Weapons.Single(x => x.Id == actor.WeaponId && x.OwnerId == actor.Id);
        var type = content.WeaponTemplates.Single(x => x.Id == weapon.TemplateId).WeaponType;
        return new(id, actor.Rank, type, actor.Blessings.Select(x => x.Id).ToArray());
    }

    public DispatchPreviewView PreviewDispatch(string missionId, IReadOnlyList<string> actorIds)
    {
        var mission = EconomyCatalog.Default.Missions.FirstOrDefault(x => x.Id == missionId);
        var reason = mission is null ? EconomyText("chooseMission") : ReservationUnavailable(actorIds);
        if (reason is not null) return new(missionId, 0, "0", "0", "0", "", "", false, reason);
        var evaluation = DispatchCalculator.Evaluate(mission!, actorIds.Select(DispatchMemberFor).ToArray());
        string Points(Rational value) => value.ToDouble().ToString("0.##", CultureInfo.InvariantCulture);
        return new(missionId, evaluation.SuccessPercent, Points(evaluation.RankPoints), Points(evaluation.WeaponPoints),
            Points(evaluation.BlessingPoints), DescribeMaterials(DispatchCalculator.Rewards(mission!, actorIds.Count, true)),
            DescribeMaterials(DispatchCalculator.Rewards(mission!, actorIds.Count, false)), true, "");
    }

    private string ReserveDispatch(GameCommand command)
    {
        RequireGuild();
        var ids = command.Value?.Split(',').Select(x => x.Trim()).ToArray() ?? [];
        var preview = PreviewDispatch(command.TargetId ?? "", ids);
        if (!preview.CanReserve) throw new InvalidOperationException(preview.UnavailableReason);
        profile.Economy.Reservation = new() { MissionId = command.TargetId!, ActorIds = ids.ToList() };
        return EconomyFormat("reserved", EconomyCatalog.Default.Missions.Single(x => x.Id == command.TargetId).Name, preview.SuccessPercent);
    }

    private string CancelDispatch()
    {
        RequireGuild();
        profile.Economy.Reservation = null;
        return EconomyText("cancelled");
    }

    private MatchEconomyState PrepareMatchEconomy(GameCommand command, string matchId)
    {
        var amount = command.Contribution ?? "0";
        if (amount.Length == 0 || amount.Any(c => c is < '0' or > '9')
            || !BigInteger.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out var contribution))
            throw new InvalidOperationException(EconomyText("invalidContribution"));
        if (contribution > profile.Resources) throw new InvalidOperationException(EconomyText("insufficientContribution"));
        DispatchAssignmentState? assignment = null;
        if (profile.Economy.Reservation is { } reservation)
        {
            var reason = ReservationUnavailable(reservation.ActorIds);
            if (reason is not null) throw new InvalidOperationException(reason);
            var mission = Clone(EconomyCatalog.Default.Missions.Single(x => x.Id == reservation.MissionId));
            var members = reservation.ActorIds.Select(DispatchMemberFor).ToArray();
            var evaluation = DispatchCalculator.Evaluate(mission, members);
            var random = Pcg32.Restore(profile.Economy.DispatchRng);
            var roll = random.NextInt(100);
            profile.Economy.DispatchRng = random.Capture();
            assignment = new() { MatchId = matchId, Mission = mission, SuccessPercent = evaluation.SuccessPercent, Roll = roll,
                Members = members.Select(x => new DispatchMemberSnapshot { ActorId = x.Id, Rank = x.Rank,
                    WeaponType = x.WeaponType, BlessingIds = x.BlessingIds.ToList(), Hp = Actor(x.Id).Hp,
                    WeaknessStacks = Actor(x.Id).WeaknessStacks }).ToList(),
                RankExperience = members.ToDictionary(x => x.Id, x => DispatchRankExperience(x.Rank, mission.RecommendedRank, roll < evaluation.SuccessPercent)) };
        }
        profile.Resources -= contribution;
        profile.Economy.Reservation = null;
        return new() { CatalogVersion = EconomyCatalog.Default.Version, Contribution = contribution, Dispatch = assignment,
            VictoryMaterials = FullMatchMaterials.ToDictionary(x => x.Key, x => x.Value) };
    }

    private Dictionary<string, BigInteger> MatchMaterials(int completed, int total)
        => session?.Economy is not { } economy ? [] : economy.VictoryMaterials.ToDictionary(x => x.Key,
            x => new Rational(x.Value * completed, total).Floor());

    private Dictionary<string, BigInteger> DispatchMaterials()
        => session?.Economy?.Dispatch is not { } dispatch ? []
            : DispatchCalculator.Rewards(dispatch.Mission, dispatch.Members.Count, dispatch.Roll < dispatch.SuccessPercent);

    private void SettleEconomy(bool won, int completed, int total)
    {
        if (session?.Economy is null) return;
        var matchMaterials = MatchMaterials(completed, total);
        var dispatchMaterials = DispatchMaterials();
        foreach (var entry in matchMaterials.Concat(dispatchMaterials)) profile.Economy.Materials[entry.Key] += entry.Value;
        var report = EconomyFormat("matchMaterials", DescribeMaterials(matchMaterials));
        if (session.Economy.Dispatch is { } dispatch)
        {
            report += "\n" + EconomyFormat(dispatch.Roll < dispatch.SuccessPercent ? "dispatchSucceeded" : "dispatchFailed",
                dispatch.Mission.Name, DescribeMaterials(dispatchMaterials));
            report += "\n" + string.Join(" / ", dispatch.RankExperience.Select(x => EconomyFormat("dispatchXp", Actor(x.Key).Name, x.Value)));
        }
        report += "\n" + EconomyFormat("discarded", battle!.State.Resources, session.Economy.Contribution);
        profile.Economy.LastReport = report;
    }

    private static string DescribeMaterials(IReadOnlyDictionary<string, BigInteger> values)
    {
        var items = EconomyCatalog.Default.Materials.Where(x => values.GetValueOrDefault(x.Id) > 0)
            .Select(x => EconomyFormat("materialQuantity", x.Name, values[x.Id])).ToArray();
        return items.Length == 0 ? EconomyText("noMaterials") : string.Join(" / ", items);
    }

    private string? DeliveryUnavailable(DeliveryDefinition delivery)
    {
        if (session is not null) return EconomyText("onlyGuild");
        return delivery.Materials.Any(x => profile.Economy.Materials.GetValueOrDefault(x.Key) < x.Value)
            ? EconomyText("insufficientMaterials") : null;
    }

    private string Deliver(GameCommand command)
    {
        RequireGuild();
        var delivery = EconomyCatalog.Default.Deliveries.FirstOrDefault(x => x.Id == command.TargetId)
            ?? throw new InvalidOperationException(EconomyText("chooseDelivery"));
        var reason = DeliveryUnavailable(delivery);
        if (reason is not null) throw new InvalidOperationException(reason);
        if (profile.Economy.DeliveryCompletions.GetValueOrDefault(delivery.Id) == int.MaxValue)
            throw new InvalidOperationException(EconomyText("deliveryLimit"));
        var rewardDescription = DeliveryRewardDescription(delivery);
        foreach (var requirement in delivery.Materials) profile.Economy.Materials[requirement.Key] -= requirement.Value;
        profile.Resources += delivery.Gold;
        if (delivery.Kind == "relation")
        {
            var faction = profile.Economy.Factions[delivery.FactionId!];
            faction.Trust = Math.Clamp(faction.Trust + delivery.Trust, -100, 100);
            if (faction.ConflictUnresolved) faction.RepairInProgress = true;
        }
        if (delivery.Kind == "equipment")
        {
            profile.Weapons.Add(Clone(profile.Economy.OfferedEquipment[delivery.Id]));
            profile.Economy.OfferedEquipment[delivery.Id] = GenerateDeliveryEquipment(profile, content, delivery);
        }
        profile.Economy.DeliveryCompletions[delivery.Id] = checked(profile.Economy.DeliveryCompletions.GetValueOrDefault(delivery.Id) + 1);
        return EconomyFormat("delivered", delivery.Name, rewardDescription);
    }

    private string DeliveryRewardDescription(DeliveryDefinition delivery)
    {
        if (delivery.Kind == "relation") return EconomyFormat("relationReward", EconomyText("faction." + delivery.FactionId), delivery.Trust, delivery.Gold);
        if (delivery.Kind == "gold") return EconomyFormat("goldReward", delivery.Gold);
        var item = profile.Economy.OfferedEquipment[delivery.Id];
        return EconomyFormat("equipmentReward", item.Name, item.BasePower.ToDouble().ToString("0.##", CultureInfo.InvariantCulture),
            (item.AttackIntervalMs / 1000).ToDouble().ToString("0.##", CultureInfo.InvariantCulture),
            new Rational(item.RangeCenti, 100).ToDouble().ToString("0.##", CultureInfo.InvariantCulture),
            item.SkillIds.Count == 0 ? EconomyText("noSkills") : string.Join(" / ", item.SkillIds.Select(id => content.Skills.Single(x => x.Id == id).Name)));
    }

    private GuildEconomyView BuildEconomyView()
    {
        DispatchStatusView? dispatchView = null;
        if (session?.Economy?.Dispatch is { } active)
            dispatchView = new(active.Mission.Id, active.Mission.Name, active.Members.Select(x => x.ActorId).ToArray(),
                active.Members.Select(x => Actor(x.ActorId).Name).ToArray(), true, active.SuccessPercent,
                DescribeMaterials(DispatchCalculator.Rewards(active.Mission, active.Members.Count, true)),
                DescribeMaterials(DispatchCalculator.Rewards(active.Mission, active.Members.Count, false)));
        else if (profile.Economy.Reservation is { } reservation)
        {
            var mission = EconomyCatalog.Default.Missions.Single(x => x.Id == reservation.MissionId);
            var preview = PreviewDispatch(reservation.MissionId, reservation.ActorIds);
            dispatchView = new(mission.Id, mission.Name, reservation.ActorIds.ToArray(), reservation.ActorIds.Select(id => Actor(id).Name).ToArray(),
                false, preview.SuccessPercent, preview.SuccessRewards, preview.FailureRewards);
        }
        return new()
        {
            Inventory = EconomyCatalog.Default.Materials.Select(x => new MaterialStockView(x.Id, x.Name, profile.Economy.Materials.GetValueOrDefault(x.Id).ToString())).ToArray(),
            Factions = FactionIds.Select(id =>
            {
                var faction = profile.Economy.Factions[id];
                var state = faction.ConflictUnresolved ? faction.RepairInProgress ? "repair" : "conflict" : faction.Trust < 0 ? "tense" : faction.Trust >= 50 ? "cooperative" : "normal";
                return new FactionRelationView(id, EconomyText("faction." + id), faction.Trust, EconomyText("relation." + state));
            }).ToArray(),
            Missions = EconomyCatalog.Default.Missions.Select(x => new DispatchMissionView(x.Id, x.Name, x.Description,
                x.RecommendedRank.ToString(), x.RecommendedMembers, string.Join(" / ", x.WeaponTypes.Select(t => EconomyText("weapon." + t))),
                string.Join(" / ", x.BlessingIds.Select(id => content.Blessings.FirstOrDefault(b => b.Id == id)?.Name ?? id)))).ToArray(),
            Deliveries = EconomyCatalog.Default.Deliveries.Select(x => new DeliveryView(x.Id, x.Name, x.Kind,
                DescribeMaterials(x.Materials), DeliveryRewardDescription(x), DeliveryUnavailable(x) is null, DeliveryUnavailable(x) ?? "")).ToArray(),
            Dispatch = dispatchView, LastReport = profile.Economy.LastReport, Contribution = session?.Economy?.Contribution.ToString() ?? "0",
            LegacyMatch = session is { Economy: null }
        };
    }
}
