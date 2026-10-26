using System.Numerics;
using Guild.Domain.Economy;
using Guild.Domain.Numerics;

namespace Guild.Domain.Tests;

public sealed class EconomyDomainTests
{
    private static DispatchMissionDefinition Mission() => new()
    {
        Id = "test", Name = "Test mission", Description = "A test mission.", RecommendedRank = 10, RecommendedMembers = 3,
        WeaponTypes = ["bow", "staff"], BlessingIds = ["tech-strength", "tech-vitality"],
        SuccessMaterials = new() { ["wood"] = 8, ["ore"] = 4 }
    };

    [Fact]
    public void EmbeddedCatalogContainsTheContractedMaterialsMissionsAndThirteenDeliveries()
    {
        var catalog = EconomyCatalog.Default;
        Assert.Empty(catalog.Validate());
        Assert.Equal(new[] { "wood", "ore", "herb", "crystal" }, catalog.Materials.Select(x => x.Id));
        Assert.Equal(3, catalog.Missions.Count);
        var recovery = Assert.Single(catalog.Missions, x => x.Id == "recovery");
        Assert.Equal(BigInteger.One, recovery.RecommendedRank);
        Assert.Equal(2, recovery.RecommendedMembers);
        Assert.Equal(new[] { "hammer", "sword" }, recovery.WeaponTypes);
        Assert.Equal(new[] { "tech-strength", "tech-fused" }, recovery.BlessingIds);
        Assert.Equal(new BigInteger(8), recovery.SuccessMaterials["wood"]);
        Assert.Equal(new BigInteger(4), recovery.SuccessMaterials["ore"]);
        var patrol = Assert.Single(catalog.Missions, x => x.Id == "patrol");
        Assert.Equal(new BigInteger(3), patrol.RecommendedRank);
        Assert.Equal(3, patrol.RecommendedMembers);
        Assert.Equal(new BigInteger(8), patrol.SuccessMaterials["herb"]);
        var survey = Assert.Single(catalog.Missions, x => x.Id == "survey");
        Assert.Equal(new BigInteger(5), survey.RecommendedRank);
        Assert.Equal(2, survey.RecommendedMembers);
        Assert.Equal(new BigInteger(4), survey.SuccessMaterials["crystal"]);
        Assert.Equal(13, catalog.Deliveries.Count);
        var relations = catalog.Deliveries.Where(x => x.Kind == "relation").ToArray();
        Assert.Equal(new[] { "C", "M", "K", "T" }, relations.Select(x => x.FactionId));
        Assert.All(relations, x => { Assert.Equal(new BigInteger(5), x.Gold); Assert.Equal(5, x.Trust); });
        Assert.Equal(new[] { "sword", "spear", "hammer", "bow", "staff" },
            catalog.Deliveries.Where(x => x.Kind == "equipment").Select(x => x.WeaponType));
        Assert.All(catalog.Deliveries.Where(x => x.Kind == "equipment"), x =>
            Assert.InRange(x.Materials.Values.Aggregate(BigInteger.Zero, (a, b) => a + b), 20, 40));
        foreach (var (id, quantity, gold) in new[] { ("wood", 4, 20), ("ore", 4, 30), ("herb", 4, 15), ("crystal", 2, 35) })
        {
            var delivery = Assert.Single(catalog.Deliveries, x => x.Id == "gold-" + id);
            Assert.Equal(new BigInteger(quantity), Assert.Single(delivery.Materials).Value);
            Assert.Equal(new BigInteger(gold), delivery.Gold);
        }
    }

    [Fact]
    public void CatalogRoundTripsAndRejectsFutureSchemasMissingReferencesAndInvalidRewards()
    {
        var catalog = EconomyCatalog.Default;
        Assert.Equal(catalog.ToJson(), EconomyCatalog.FromJson(catalog.ToJson()).ToJson());
        Assert.Throws<InvalidDataException>(() => (catalog with { SchemaVersion = 99 }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (catalog with { Materials = [catalog.Materials[0], catalog.Materials[0]] }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (catalog with
        {
            Missions = [catalog.Missions[0] with { SuccessMaterials = new() { ["unknown"] = 1 } }]
        }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (catalog with
        {
            Missions = [catalog.Missions[0] with { RecommendedRank = 0 }]
        }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (catalog with
        {
            Deliveries = [catalog.Deliveries[0] with { Materials = new() { ["wood"] = -1 } }]
        }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (catalog with
        {
            Deliveries = [catalog.Deliveries[0] with { FactionId = "queen" }]
        }).EnsureValid());
    }

    [Fact]
    public void SuccessUsesExactPointBreakdownAndFloorsOnlyTheFinalSum()
    {
        var mission = Mission();
        var result = DispatchCalculator.Evaluate(mission,
            [new("a", 10, "bow", ["tech-strength"]), new("b", 5, "staff", [])]);
        Assert.Equal((Rational)20, result.RankPoints);
        Assert.Equal((Rational)20, result.WeaponPoints);
        Assert.Equal((Rational)10, result.BlessingPoints);
        Assert.Equal(70, result.SuccessPercent);
        var fraction = DispatchCalculator.Evaluate(mission with { RecommendedRank = 3, RecommendedMembers = 2 },
            [new("a", 1, "bow", ["tech-strength"])]);
        Assert.Equal(new Rational(20, 3), fraction.RankPoints);
        Assert.Equal(46, fraction.SuccessPercent);
    }

    [Fact]
    public void FullCoverageAndAdequateTeamReachOneHundredPercent()
    {
        var result = DispatchCalculator.Evaluate(Mission(),
            [new("a", 10, "bow", ["tech-strength"]), new("b", 10, "staff", ["tech-vitality"]), new("c", 10, "sword", [])]);
        Assert.Equal(100, result.SuccessPercent);
        Assert.Equal((Rational)40, result.RankPoints);
        Assert.Equal((Rational)20, result.WeaponPoints);
        Assert.Equal((Rational)20, result.BlessingPoints);
    }

    [Fact]
    public void UnboundedRankCannotReplaceSeveralMembersOrMissingAdaptations()
    {
        var huge = BigInteger.Pow(10, 300);
        var result = DispatchCalculator.Evaluate(Mission(), [new("a", huge, "bow", ["tech-strength", "tech-vitality"])]);
        Assert.Equal(new Rational(40, 3), result.RankPoints);
        Assert.Equal(63, result.SuccessPercent);
        var rankOnly = DispatchCalculator.Evaluate(Mission() with { RecommendedMembers = 1 }, [new("a", huge, "sword", [])]);
        Assert.Equal(60, rankOnly.SuccessPercent);
    }

    [Fact]
    public void AddingLowRankMembersNeverReducesSuccessAndRepeatedKindsDoNotAddCoverage()
    {
        var members = new List<DispatchMember> { new("a", 10, "bow", ["tech-strength", "tech-strength"]) };
        var previous = DispatchCalculator.Evaluate(Mission(), members);
        for (var i = 1; i < 6; i++)
        {
            members.Add(new("member-" + i, 1, "bow", ["tech-strength"]));
            var current = DispatchCalculator.Evaluate(Mission(), members);
            Assert.True(current.SuccessPercent >= previous.SuccessPercent);
            Assert.Equal(previous.WeaponPoints, current.WeaponPoints);
            Assert.Equal(previous.BlessingPoints, current.BlessingPoints);
            Assert.True(current.RankPoints >= previous.RankPoints);
            previous = current;
        }
    }

    [Fact]
    public void PurePreviewIsRepeatableAndReflectsCurrentWeaponAndOwnedBlessingIds()
    {
        DispatchMember member = new("a", 10, "sword", []);
        var before = DispatchCalculator.Evaluate(Mission(), [member]);
        Assert.Equal(before, DispatchCalculator.Evaluate(Mission(), [member]));
        var after = DispatchCalculator.Evaluate(Mission(), [member with { WeaponType = "bow", BlessingIds = ["tech-strength"] }]);
        Assert.Equal(before.SuccessPercent + 20, after.SuccessPercent);
        Assert.Empty(member.BlessingIds);
        Assert.Equal("sword", member.WeaponType);
    }

    [Fact]
    public void RewardsScaleByTeamSizeAndFailureReturnsOneQuarterOfScaledMaterials()
    {
        for (var count = 1; count <= 6; count++)
        {
            var success = DispatchCalculator.Rewards(Mission(), count, true);
            var failure = DispatchCalculator.Rewards(Mission(), count, false);
            Assert.Equal(new BigInteger(8 * (count + 1) / 2), success["wood"]);
            Assert.Equal(new BigInteger(4 * (count + 1) / 2), success["ore"]);
            Assert.Equal(success["wood"] / 4, failure["wood"]);
            Assert.Equal(success["ore"] / 4, failure["ore"]);
        }
    }

    [Fact]
    public void OddRewardQuantitiesFloorAndZeroFailureKeepsOnePrimaryMaterial()
    {
        var odd = Mission() with { SuccessMaterials = new() { ["wood"] = 3 } };
        Assert.Equal(new BigInteger(4), DispatchCalculator.Rewards(odd, 2, true)["wood"]);
        Assert.Equal(BigInteger.One, DispatchCalculator.Rewards(odd, 2, false)["wood"]);
        var tiny = Mission() with { SuccessMaterials = new() { ["crystal"] = 1, ["ore"] = 1 } };
        var fallback = Assert.Single(DispatchCalculator.Rewards(tiny, 1, false));
        Assert.Equal("crystal", fallback.Key);
        Assert.Equal(BigInteger.One, fallback.Value);
    }

    [Fact]
    public void InvalidTeamsAndDefinitionsAreRejectedBeforeCalculation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DispatchCalculator.Evaluate(Mission(), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => DispatchCalculator.Rewards(Mission(), 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => DispatchCalculator.Rewards(Mission(), 7, true));
        DispatchMember actor = new("a", 1, "bow", []);
        Assert.Throws<ArgumentException>(() => DispatchCalculator.Evaluate(Mission(), [actor, actor]));
        Assert.Throws<ArgumentException>(() => DispatchCalculator.Evaluate(Mission(), [actor with { Rank = -1 }]));
        Assert.Throws<ArgumentException>(() => DispatchCalculator.Evaluate(Mission() with { RecommendedRank = 0 }, [actor]));
        Assert.Throws<ArgumentException>(() => DispatchCalculator.Rewards(Mission() with { SuccessMaterials = new() { ["wood"] = 0 } }, 1, false));
    }
}
