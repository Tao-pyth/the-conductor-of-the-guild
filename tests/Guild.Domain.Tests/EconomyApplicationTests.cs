using System.Numerics;
using System.Text.Json;
using Guild.Application;
using Guild.Domain.Economy;
using Guild.Domain.Randomness;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class EconomyApplicationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "guild-economy-app-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(directory, "save.sqlite3");
    private string Content => Path.Combine(AppContext.BaseDirectory, "tech-content.json");
    private const string Defense = "tech-first-defense";
    private const string Invasion = "tech-first-invasion";
    private static readonly Dictionary<string, BigInteger> FullMaterials = new()
        { ["wood"] = 8, ["ore"] = 6, ["herb"] = 6, ["crystal"] = 4 };

    public EconomyApplicationTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ReservationSupportsOneToSixMembersWithAtLeastOneAvailableAdventurer(int count)
    {
        SeedProfile(profile => profile.Resources = 100);
        using var game = new GameController(Save, Content);
        Ok(game, new("Recruit"));
        var ids = game.View.Adventurers.Take(count).Select(x => x.Id).ToArray();
        var reserve = new GameCommand("ReserveDispatch", TargetId: "recovery", Value: string.Join(',', ids), CommandId: "reserve-once");
        Ok(game, reserve);
        var after = Snapshot();
        Ok(game, reserve);
        Assert.Equal(after, Snapshot());
        var reservation = Profile().Economy.Reservation!;
        Assert.Equal(ids, reservation.ActorIds);
        Assert.False(game.View.Economy.Dispatch!.Active);
        Assert.Equal(ids, game.View.Economy.Dispatch.ActorIds);
        Ok(game, new("CancelDispatch"));
        Assert.Null(Profile().Economy.Reservation);
        Assert.Null(game.View.Economy.Dispatch);
    }

    [Fact]
    public void InvalidReservationsDoNotMutateOrLeaveTheWholeRosterUnavailable()
    {
        using var game = new GameController(Save, Content);
        var ids = game.View.Adventurers.Select(x => x.Id).ToArray();
        foreach (var value in new[] { "", "unknown", ids[0] + "," + ids[0], string.Join(',', ids), string.Join(',', ids) + ",unknown" })
            FailsWithoutMutation(game, new("ReserveDispatch", TargetId: "recovery", Value: value));
        FailsWithoutMutation(game, new("ReserveDispatch", TargetId: "unknown", Value: ids[0]));
        Ok(game, new("ReserveDispatch", TargetId: "recovery", Value: string.Join(',', ids.Take(5))));
        Ok(game, new("ReserveDispatch", TargetId: "survey", Value: ids[0]));
        Assert.Equal("survey", Profile().Economy.Reservation!.MissionId);
        Assert.Single(Profile().Economy.Reservation!.ActorIds);
    }

    [Fact]
    public void PreviewsArePureAndAddingALowRankMemberNeverLowersSuccess()
    {
        SeedProfile(profile => profile.Adventurers[0].Rank = 5);
        using var game = new GameController(Save, Content);
        var ids = game.View.Adventurers.Select(x => x.Id).ToArray();
        var before = Snapshot();
        var solo = game.PreviewDispatch("survey", [ids[0]]);
        var team = game.PreviewDispatch("survey", [ids[0], ids[1]]);
        Assert.True(solo.CanReserve);
        Assert.True(team.CanReserve);
        Assert.True(team.SuccessPercent >= solo.SuccessPercent);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(solo, game.PreviewDispatch("survey", [ids[0]]));
            Assert.Equal(team, game.PreviewDispatch("survey", [ids[0], ids[1]]));
            Assert.Equal(13, game.View.Economy.Deliveries.Count);
            Assert.Equal(4, game.View.Economy.Inventory.Count);
        }
        Assert.False(game.PreviewDispatch("survey", ids).CanReserve);
        Assert.False(game.PreviewDispatch("unknown", [ids[0]]).CanReserve);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void ReservedAdventurerCanChangeEquipmentAndDepartureFreezesTheUpdatedPreview()
    {
        string hammerId = Guid.NewGuid().ToString("N");
        SeedProfile(profile =>
        {
            var actor = profile.Adventurers[0];
            var hammer = Copy(profile.Weapons.Single(x => x.Id == actor.WeaponId));
            hammer.Id = hammerId; hammer.TemplateId = "tech-hammer"; hammer.Name = "適性確認用の槌";
            profile.Weapons.Add(hammer);
        });
        string actorId;
        int expected;
        using (var game = new GameController(Save, Content))
        {
            actorId = game.View.Adventurers[0].Id;
            Ok(game, new("ReserveDispatch", TargetId: "survey", Value: actorId));
            var before = game.PreviewDispatch("survey", [actorId]);
            Ok(game, new("EquipItem", ActorId: actorId, TargetId: hammerId));
            var after = game.PreviewDispatch("survey", [actorId]);
            Assert.Equal(before.SuccessPercent + 10, after.SuccessPercent);
            Assert.Equal(after.SuccessPercent, game.View.Economy.Dispatch!.SuccessPercent);
            expected = after.SuccessPercent;
            Ok(game, new("StartMatch"));
            var assignment = Session().Economy!.Dispatch!;
            Assert.Equal("hammer", Assert.Single(assignment.Members).WeaponType);
            Assert.Equal(expected, assignment.SuccessPercent);
        }
        using var restored = new GameController(Save, Content);
        Assert.Equal(expected, restored.View.Economy.Dispatch!.SuccessPercent);
        Assert.True(restored.View.Adventurers.Single(x => x.Id == actorId).IsDispatched);
    }

    [Theory]
    [InlineData("defense")]
    [InlineData("invasion")]
    public void DepartureChargesContributionOnceAndDispatchMembersCannotUseBattleCommands(string mode)
    {
        SeedProfile(profile => profile.Resources = 500);
        using var game = new GameController(Save, Content);
        var ids = game.View.Adventurers.Select(x => x.Id).ToArray();
        Ok(game, new("ReserveDispatch", TargetId: "recovery", Value: ids[0]));
        if (mode == "invasion")
            FailsWithoutMutation(game, new GameCommand("StartMatch", TargetId: Invasion, Value: ids[0]) { Contribution = "125" });
        var start = new GameCommand("StartMatch", TargetId: mode == "invasion" ? Invasion : Defense,
            Value: mode == "invasion" ? ids[1] : null, CommandId: "departure-once") { Contribution = "125" };
        Ok(game, start);
        var departed = Snapshot();
        Ok(game, start);
        Assert.Equal(departed, Snapshot());
        Assert.Equal("375", game.View.GuildResources);
        Assert.Equal("1125", game.View.MatchResources);
        Assert.Equal(new BigInteger(125), Session().Economy!.Contribution);
        Assert.Equal(new BigInteger(1125), Session().Current.Setup.InitialResources);
        Assert.True(game.View.Economy.Dispatch!.Active);
        Assert.Null(Profile().Economy.Reservation);
        Assert.Equal(Session().MatchId, Session().Economy!.Dispatch!.MatchId);
        var beforeAvailability = Snapshot();
        var dispatchedActor = game.View.Adventurers.Single(x => x.Id == ids[0]);
        Assert.True(dispatchedActor.IsDispatched);
        Assert.False(dispatchedActor.InParty);
        var preparation = Assert.IsType<PreparationInfoView>(dispatchedActor.PreparationInfo);
        Assert.False(preparation.CanDeploy);
        Assert.False(preparation.CanRevive);
        Assert.False(preparation.CanPurchaseLevel);
        var reason = DisplayText.Get("economy.app.unavailableActor");
        Assert.Equal(reason, preparation.DeployUnavailableReason);
        Assert.Equal(reason, preparation.ReviveUnavailableReason);
        Assert.Equal(reason, preparation.PurchaseUnavailableReason);
        Assert.False(Assert.IsType<RangeGuideView>(dispatchedActor.RangeGuide).Available);
        Assert.Equal(beforeAvailability, Snapshot());
        foreach (var type in new[] { "Deploy", "Reposition", "BuyLevel", "Revive", "SetTarget" })
            FailsWithoutMutation(game, new(type, ActorId: ids[0], X: mode == "invasion" ? 1 : 4,
                Y: mode == "invasion" ? 3 : 4, Value: "nearest"));
        FailsWithoutMutation(game, new("CancelDispatch"));
        FailsWithoutMutation(game, new("ReserveDispatch", TargetId: "patrol", Value: ids[1]));
    }

    [Theory]
    [InlineData("0", "0", true)]
    [InlineData("500", "500", true)]
    [InlineData("501", "500", false)]
    [InlineData("-1", "500", false)]
    [InlineData("", "500", false)]
    [InlineData(" 1", "500", false)]
    [InlineData("+1", "500", false)]
    [InlineData("1.5", "500", false)]
    [InlineData("1e2", "500", false)]
    [InlineData("10000000000000000000000000000000000000000", "10000000000000000000000000000000000000000", true)]
    public void ContributionsAcceptZeroEntireBalanceAndHugeIntegersButRejectIllegalOrUnaffordableValues(string amount, string gold, bool valid)
    {
        SeedProfile(profile => profile.Resources = BigInteger.Parse(gold));
        using var game = new GameController(Save, Content);
        var command = new GameCommand("StartMatch") { Contribution = amount };
        if (!valid) { FailsWithoutMutation(game, command); return; }
        Ok(game, command);
        Assert.Equal(BigInteger.Parse(gold) - BigInteger.Parse(amount), BigInteger.Parse(game.View.GuildResources));
        Assert.Equal(1000 + BigInteger.Parse(amount), BigInteger.Parse(game.View.MatchResources));
        Assert.Equal(BigInteger.Parse(amount), Session().Economy!.Contribution);
    }

    [Fact]
    public void RestartAndRetryKeepFrozenDispatchAndGoldAndOnlyFinalAttemptMaterialsAreReceived()
    {
        SeedProfile(profile => profile.Resources = 300);
        var dispatchId = Profile().Adventurers[0].Id;
        string assignment;
        string matchId;
        string attemptId;
        PcgState dispatchRng;
        BigInteger restartBudget;
        using (var game = new GameController(Save, Content))
        {
            Ok(game, new("ReserveDispatch", TargetId: "recovery", Value: dispatchId));
            var fighter = game.View.Adventurers[1].Id;
            Ok(game, new GameCommand("StartMatch", TargetId: Invasion, Value: fighter) { Contribution = "150" });
            Ok(game, new("Deploy", ActorId: fighter, X: 1, Y: 3));
            Ok(game, new("StartWave"));
            assignment = Json(Session().Economy!.Dispatch);
            matchId = Session().MatchId; attemptId = Session().AttemptId;
            dispatchRng = Profile().Economy.DispatchRng;
            restartBudget = Session().MatchStart!.Resources;
            game.Advance(.1);
        }
        using (var restored = new GameController(Save, Content))
        {
            Assert.Equal("Battle", restored.View.Screen);
            Assert.Equal(assignment, Json(Session().Economy!.Dispatch));
            Assert.Equal(dispatchRng, Profile().Economy.DispatchRng);
        }
        SetResult(false, 3);
        using (var game = new GameController(Save, Content))
        {
            Ok(game, new("Retry"));
            Assert.Equal(matchId, Session().MatchId);
            Assert.NotEqual(attemptId, Session().AttemptId);
            Assert.Equal(assignment, Json(Session().Economy!.Dispatch));
            Assert.Equal(dispatchRng, Profile().Economy.DispatchRng);
            Assert.Equal(restartBudget, BigInteger.Parse(game.View.MatchResources));
            Assert.Equal("150", game.View.GuildResources);
        }
        SetResult(false, 1);
        var dispatch = Session().Economy!.Dispatch!;
        var expected = DispatchCalculator.Rewards(dispatch.Mission, dispatch.Members.Count, dispatch.Roll < dispatch.SuccessPercent);
        using var final = new GameController(Save, Content);
        var settle = new GameCommand("ReturnToGuild", CommandId: "final-attempt-return");
        Ok(final, settle);
        var settled = Snapshot();
        Ok(final, settle);
        Assert.Equal(settled, Snapshot());
        Assert.Equal("150", final.View.GuildResources);
        foreach (var material in EconomyCatalog.Default.Materials)
            Assert.Equal(expected.GetValueOrDefault(material.Id), Profile().Economy.Materials[material.Id]);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MainBattleAndDispatchResultsSettleIndependentlyAndPreserveDispatchHealthAndNextSortieEffects(bool won, bool dispatchSucceeded)
    {
        SeedProfile(profile =>
        {
            var actor = profile.Adventurers[0];
            actor.Hp = 73; actor.WeaknessStacks = 2; actor.Experience = 95;
            actor.NextMatchChanges["str"] = 3; actor.NextMatchChanges["vit"] = 2;
            actor.Blessings = [new() { Id = "tech-strength", Rank = 3 }];
            var mission = EconomyCatalog.Default.Missions.Single(x => x.Id == "recovery");
            var chance = DispatchCalculator.Evaluate(mission, [new(actor.Id, actor.Rank, "sword", ["tech-strength"])]).SuccessPercent;
            profile.Economy.DispatchRng = SeedForOutcome(chance, dispatchSucceeded);
        });
        var old = Profile();
        var dispatchId = old.Adventurers[0].Id;
        StartWithFighter("invasion", dispatchId);
        var frozen = Session().Economy!.Dispatch!;
        Assert.Equal(dispatchSucceeded, frozen.Roll < frozen.SuccessPercent);
        var dispatchMaterials = DispatchCalculator.Rewards(frozen.Mission, frozen.Members.Count, dispatchSucceeded);
        var dispatchXp = dispatchSucceeded ? new BigInteger(30) : new BigInteger(15);
        Assert.Equal(dispatchXp, frozen.RankExperience[dispatchId]);
        SetResult(won, won ? 3 : 1);
        using var game = new GameController(Save, Content);
        var beforeViews = Snapshot();
        for (var i = 0; i < 3; i++) { Assert.NotNull(game.View.ResultSummary); Assert.True(game.View.Economy.Dispatch!.Active); }
        Assert.Equal(beforeViews, Snapshot());
        var command = new GameCommand("ReturnToGuild", CommandId: "independent-settlement");
        Ok(game, command);
        var after = Profile();
        var actorAfter = after.Adventurers.Single(x => x.Id == dispatchId);
        Assert.Equal(new BigInteger(73), actorAfter.Hp);
        Assert.Equal(2, actorAfter.WeaknessStacks);
        Assert.Equal(Json(old.Adventurers[0].NextMatchChanges), Json(actorAfter.NextMatchChanges));
        Assert.Equal(new BigInteger(2), actorAfter.Rank);
        Assert.Equal(95 + dispatchXp - 100, actorAfter.Experience);
        Assert.Contains(after.PendingEvents, x => x.ActorId == dispatchId && x.Rank == 2);
        Assert.Equal(won ? new BigInteger(300) : BigInteger.Zero, after.Resources);
        foreach (var material in EconomyCatalog.Default.Materials)
            Assert.Equal((won ? FullMaterials[material.Id] : BigInteger.Zero) + dispatchMaterials.GetValueOrDefault(material.Id), after.Economy.Materials[material.Id]);
        Assert.Null(game.View.Economy.Dispatch);
        Assert.False(game.View.Adventurers.Single(x => x.Id == dispatchId).IsDispatched);
        Assert.NotEmpty(game.View.Economy.LastReport);
        var committed = Snapshot();
        Ok(game, command);
        Assert.Equal(committed, Snapshot());
    }

    [Fact]
    public void HighRankDispatchMemberDoesNotReduceTheCombatParticipantsRankExperience()
    {
        SeedProfile(profile => profile.Adventurers[0].Rank = 1000);
        var dispatchId = Profile().Adventurers[0].Id;
        var fighter = Profile().Adventurers[1].Id;
        StartWithFighter("invasion", dispatchId);
        SetResult(true, 3);
        using var game = new GameController(Save, Content);
        Ok(game, new("ReturnToGuild"));
        var actor = Profile().Adventurers.Single(x => x.Id == fighter);
        Assert.Equal(new BigInteger(2), actor.Rank);
        Assert.Equal(new BigInteger(20), actor.Experience);
    }

    [Theory]
    [InlineData("defense", 6, 10)]
    [InlineData("invasion", 2, 3)]
    public void DefeatMaterialsUseCompletedProgressAndOperationGoldIsNotReturned(string mode, int failedWave, int total)
    {
        using (var initial = new GameController(Save, Content)) { }
        StartWithFighter(mode);
        SetResult(false, failedWave, resources: 123456);
        using var game = new GameController(Save, Content);
        Ok(game, new("ReturnToGuild"));
        var after = Profile();
        var completed = failedWave - 1;
        Assert.Equal(new BigInteger(100 * completed), after.Resources);
        foreach (var (id, quantity) in FullMaterials)
            Assert.Equal(quantity * completed / total, after.Economy.Materials[id]);
    }

    [Theory]
    [InlineData("relation-court")]
    [InlineData("equipment-bow")]
    [InlineData("gold-ore")]
    public void EachDeliveryKindRejectsInsufficiencyAndConsumesAndRewardsExactlyOnce(string id)
    {
        SeedProfile(profile => profile.Resources = 10);
        using (var game = new GameController(Save, Content))
        {
            FailsWithoutMutation(game, new("Deliver", TargetId: id));
            FailsWithoutMutation(game, new("Deliver", TargetId: "unknown"));
            Assert.False(game.View.Economy.Deliveries.Single(x => x.Id == id).CanComplete);
        }
        var definition = EconomyCatalog.Default.Deliveries.Single(x => x.Id == id);
        MutateProfile(profile =>
        {
            foreach (var (material, quantity) in definition.Materials) profile.Economy.Materials[material] = quantity;
            if (definition.Kind == "relation")
            {
                var faction = profile.Economy.Factions[definition.FactionId!];
                faction.Trust = 98; faction.ConflictUnresolved = true;
            }
        });
        var old = Profile();
        string? deliveredEquipment = null;
        using (var game = new GameController(Save, Content))
        {
            var preview = game.View.Economy.Deliveries.Single(x => x.Id == id);
            Assert.True(preview.CanComplete);
            var pure = Snapshot();
            _ = game.View.Economy;
            Assert.Equal(pure, Snapshot());
            var command = new GameCommand("Deliver", TargetId: id, CommandId: "delivery-once");
            var result = Ok(game, command);
            Assert.Contains(preview.RewardDescription, result.Message);
            var once = Snapshot();
            Ok(game, command);
            Assert.Equal(once, Snapshot());
            var after = Profile();
            Assert.Equal(old.Resources + definition.Gold, after.Resources);
            Assert.Equal(1, after.Economy.DeliveryCompletions[id]);
            Assert.All(definition.Materials.Keys, key => Assert.Equal(BigInteger.Zero, after.Economy.Materials[key]));
            if (definition.Kind == "relation")
            {
                var relation = after.Economy.Factions[definition.FactionId!];
                Assert.Equal(100, relation.Trust);
                Assert.True(relation.ConflictUnresolved);
                Assert.True(relation.RepairInProgress);
            }
            if (definition.Kind == "equipment")
            {
                var offered = old.Economy.OfferedEquipment[id];
                var received = Assert.Single(after.Weapons, x => x.Id == offered.Id);
                Assert.Equal(Json(offered), Json(received));
                Assert.Equal("guild", received.OwnerId);
                Assert.NotEqual(offered.Id, after.Economy.OfferedEquipment[id].Id);
                deliveredEquipment = Json(after.Economy.OfferedEquipment[id]);
            }
        }
        using var restored = new GameController(Save, Content);
        Assert.Equal(1, Profile().Economy.DeliveryCompletions[id]);
        if (deliveredEquipment is not null) Assert.Equal(deliveredEquipment, Json(Profile().Economy.OfferedEquipment[id]));
    }

    [Fact]
    public void DeliveredEquipmentUsesExistingRankTransferAndCanBeEquippedWithoutRegeneration()
    {
        var delivery = EconomyCatalog.Default.Deliveries.Single(x => x.Id == "equipment-bow");
        SeedProfile(profile =>
        {
            foreach (var (id, amount) in delivery.Materials) profile.Economy.Materials[id] = amount;
            profile.PendingEvents.Add(new() { Id = "delivery-transfer", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 });
        });
        var offered = Copy(Profile().Economy.OfferedEquipment[delivery.Id]);
        using var game = new GameController(Save, Content);
        var actor = game.View.Adventurers[0].Id;
        Ok(game, new("Deliver", TargetId: delivery.Id));
        Ok(game, new("BeginEvent", Value: "transfer"));
        Ok(game, new("TransferItem", TargetId: offered.Id));
        Ok(game, new("EquipItem", ActorId: actor, TargetId: offered.Id));
        offered.OwnerId = actor;
        var actual = Profile();
        Assert.Equal(offered.Id, actual.Adventurers.Single(x => x.Id == actor).WeaponId);
        Assert.Equal(Json(offered), Json(actual.Weapons.Single(x => x.Id == offered.Id)));
    }

    [Theory]
    [InlineData(CommitStage.AfterDocuments)]
    [InlineData(CommitStage.BeforeCommit)]
    [InlineData(CommitStage.AfterCommit)]
    public void DeliveryCommitFaultsDoNotPartiallyConsumeAndLostAcknowledgementDoesNotDuplicate(CommitStage faultStage)
    {
        SeedProfile(profile => profile.Economy.Materials["ore"] = 4);
        var armed = false;
        using var game = new GameController(Save, Content, stage =>
        {
            if (armed && stage == faultStage) { armed = false; throw new IOException("economy delivery fault"); }
        });
        var before = Snapshot();
        var command = new GameCommand("Deliver", TargetId: "gold-ore", CommandId: "delivery-with-fault");
        armed = true;
        var first = game.Execute(command);
        Assert.Equal(faultStage == CommitStage.AfterCommit, first.Success);
        if (!first.Success) Assert.Equal(before, Snapshot());
        Ok(game, command);
        var after = Snapshot();
        Ok(game, command);
        Assert.Equal(after, Snapshot());
        Assert.Equal(new BigInteger(30), Profile().Resources);
        Assert.Equal(BigInteger.Zero, Profile().Economy.Materials["ore"]);
        Assert.Equal(1, Profile().Economy.DeliveryCompletions["gold-ore"]);
    }

    [Theory]
    [InlineData(CommitStage.BeforeCommit)]
    [InlineData(CommitStage.AfterCommit)]
    public void DepartureCommitFaultsRestoreOrKeepTheWholeContributionReservationAndRandomDraw(CommitStage faultStage)
    {
        SeedProfile(profile => profile.Resources = 200);
        var armed = false;
        using var game = new GameController(Save, Content, stage =>
        {
            if (armed && stage == faultStage) { armed = false; throw new IOException("economy departure fault"); }
        });
        var actor = game.View.Adventurers[0].Id;
        Ok(game, new("ReserveDispatch", TargetId: "recovery", Value: actor));
        var before = Snapshot();
        var random = Pcg32.Restore(Profile().Economy.DispatchRng);
        var roll = random.NextInt(100);
        var command = new GameCommand("StartMatch", CommandId: "departure-with-fault") { Contribution = "100" };
        armed = true;
        var first = game.Execute(command);
        Assert.Equal(faultStage == CommitStage.AfterCommit, first.Success);
        if (!first.Success) Assert.Equal(before, Snapshot());
        Ok(game, command);
        var committed = Snapshot();
        Ok(game, command);
        Assert.Equal(committed, Snapshot());
        Assert.Equal(new BigInteger(100), Profile().Resources);
        Assert.Equal(new BigInteger(1100), Session().Current.Resources);
        Assert.Equal(roll, Session().Economy!.Dispatch!.Roll);
        Assert.Equal(random.Capture(), Profile().Economy.DispatchRng);
        Assert.Null(Profile().Economy.Reservation);
    }

    private void StartWithFighter(string mode, string? dispatchId = null)
    {
        using var game = new GameController(Save, Content);
        if (dispatchId is not null) Ok(game, new("ReserveDispatch", TargetId: "recovery", Value: dispatchId));
        var fighter = game.View.Adventurers.First(x => x.Id != dispatchId).Id;
        Ok(game, new("StartMatch", TargetId: mode == "invasion" ? Invasion : Defense, Value: mode == "invasion" ? fighter : null));
        Ok(game, new("Deploy", ActorId: fighter, X: mode == "invasion" ? 1 : 4, Y: mode == "invasion" ? 3 : 4));
        Ok(game, new("StartWave"));
    }

    // Keep the real departure, frozen dispatch and first-battle checkpoint intact;
    // only substitute the terminal battle result, as in existing application fixtures.
    private void SetResult(bool victory, int wave, BigInteger? resources = null)
    {
        var session = Session();
        session.Phase = victory ? "Victory" : "Defeat";
        session.Current.Running = false; session.Current.Wave = wave;
        session.Current.Victory = victory; session.Current.Defeated = !victory; session.Current.WaveCompleted = victory;
        session.Current.DefeatReason = victory ? null : "all-dead";
        session.Current.Projectiles.Clear();
        if (resources is { } amount) session.Current.Resources = amount;
        if (!victory)
            foreach (var actor in session.Current.Units.Where(x => !x.IsEnemy && x.Deployed)) actor.Hp = 0;
        using var store = new SaveStore(Save);
        Commit(store, new() { ["session"] = Json(session) });
    }

    private static PcgState SeedForOutcome(int chance, bool success)
    {
        for (ulong seed = 0; seed < 10000; seed++)
        {
            var candidate = new Pcg32(seed, 117);
            var saved = candidate.Capture();
            if ((candidate.NextInt(100) < chance) == success) return saved;
        }
        throw new InvalidOperationException("No deterministic dispatch outcome seed was found.");
    }

    private void FailsWithoutMutation(GameController game, GameCommand command)
    {
        var before = Snapshot();
        Assert.False(game.Execute(command).Success);
        Assert.Equal(before, Snapshot());
    }

    private static CommandResult Ok(GameController game, GameCommand command)
    {
        var result = game.Execute(command);
        Assert.True(result.Success, result.Message);
        return result;
    }

    private void SeedProfile(Action<PlayerProfile> mutate)
    {
        using (var game = new GameController(Save, Content)) { }
        MutateProfile(mutate);
    }

    private void MutateProfile(Action<PlayerProfile> mutate)
    {
        var profile = Profile();
        mutate(profile);
        using var store = new SaveStore(Save);
        Commit(store, new() { ["profile"] = Json(profile) });
    }

    private PlayerProfile Profile()
    {
        using var store = new SaveStore(Save);
        return Read<PlayerProfile>(store.ReadDocument("profile")!);
    }

    private MatchSession Session()
    {
        using var store = new SaveStore(Save);
        return Read<MatchSession>(store.ReadDocument("session")!);
    }

    private string Snapshot()
    {
        using var store = new SaveStore(Save);
        return Json(new { store.Revision, Profile = store.ReadDocument("profile"), Session = store.ReadDocument("session"), History = store.ReadHistory(0, 10000) });
    }

    private static T Copy<T>(T value) => Read<T>(Json(value));
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, DomainJson.Options)!;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    private static void Commit(SaveStore store, Dictionary<string, string> documents)
        => store.Commit(new(store.Revision, "fixture-" + Guid.NewGuid().ToString("N"), "fixture", "isolated economy fixture", documents, []));

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
