using System.Text.Json;
using Guild.Application;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class InvasionAcceptanceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "guild-invasion-acceptance-" + Guid.NewGuid().ToString("N"));
    private static string Content => Path.Combine(AppContext.BaseDirectory, "tech-content.json");

    private static BattleSetup Setup(int rosterCount, int partyCount) => new()
    {
        Content = ContentBundle.Load(Content), StageId = "tech-first-invasion", InitialResources = 1000,
        Units = Enumerable.Range(0, rosterCount).Select(i => new BattleUnitSetup
        {
            Id = "acceptance-" + i, Name = "検証" + i, WeaponTemplateId = "tech-staff",
            Stats = BattleStats.Standard, SkillIds = i == 0 ? ["tech-mend"] : []
        }).ToList(),
        PartyIds = Enumerable.Range(0, partyCount).Select(i => "acceptance-" + i).ToList()
    };

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void ExpeditionSizeBoundariesAndAllTenDeploymentSlotsAreEnforced(int count, bool valid)
    {
        if (!valid) { Assert.Throws<ArgumentException>(() => new BattleSimulation(Setup(11, count))); return; }
        var battle = new BattleSimulation(Setup(11, count));
        var cells = battle.State.Setup.Content.Stages.Single(s => s.Mode == "invasion").DeploymentCells!;
        for (var index = 0; index < count; index++)
            Assert.True(battle.Deploy("acceptance-" + index, cells[index].X, cells[index].Y));
        Assert.Equal(count, battle.Units.Count(u => u.Deployed));
        Assert.False(battle.Deploy("acceptance-10", 2, 5));
        Assert.True(battle.BeginWave());
    }

    [Fact]
    public void MissingExperiencePurchaseUsesExactFundsAndRejectsOneShortOrMaximumLevel()
    {
        var battle = new BattleSimulation(Setup(1, 1));
        Assert.True(battle.Deploy("acceptance-0", 2, 5));
        var unit = battle.Units.Single();
        unit.LevelExperience = 50;
        battle.State.Resources = 149;
        Assert.False(battle.PurchaseLevel(unit.Id));
        Assert.Equal(149, battle.State.Resources); Assert.Equal(1, unit.Level);
        battle.State.Resources = 150;
        Assert.True(battle.PurchaseLevel(unit.Id));
        Assert.Equal(0, battle.State.Resources); Assert.Equal(2, unit.Level);
        Assert.True(battle.GrantLevel(unit.Id, "firepower"));
        unit.Level = 10; battle.State.Resources = 999;
        Assert.False(battle.PurchaseLevel(unit.Id));
        Assert.Equal(999, battle.State.Resources);
    }

    [Fact]
    public void HealingIncludesTheExactRangeBoundaryButExcludesOutsideAndIncapacitatedAllies()
    {
        var battle = new BattleSimulation(Setup(4, 4));
        var cells = battle.State.Setup.Content.Stages.Single(s => s.Mode == "invasion").DeploymentCells!;
        for (var index = 0; index < 4; index++)
            Assert.True(battle.Deploy("acceptance-" + index, cells[index].X, cells[index].Y));
        Assert.True(battle.BeginWave());
        foreach (var enemy in battle.Units.Where(x => x.IsEnemy))
        { enemy.AttacksAdventurers = false; enemy.EnemySpeedCentiPerSecond = 0; }
        var allies = battle.Units.Where(x => !x.IsEnemy).ToArray();
        for (var index = 0; index < allies.Length; index++)
        {
            allies[index].X = index == 0 ? 2 : 5; allies[index].Y = 5;
            allies[index].MarchPathIndex = index == 0 ? 2 : 5; allies[index].MarchJoined = true;
            allies[index].MoveMultiplier = 0;
        }
        allies[1].Hp = 50;
        allies[2].X = new Rational(501, 100); allies[2].Hp = 1;
        allies[3].Hp = 0;
        for (var step = 0; step < 50; step++) battle.Step();
        Assert.True(allies[1].Hp > 50);
        Assert.Equal(1, allies[2].Hp);
        Assert.Equal(0, allies[3].Hp);
    }

    [Fact]
    public void DifferentFrameRatesAndDoubleSpeedReachTheSameInvasionStateAndPauseFreezesIt()
    {
        Directory.CreateDirectory(directory);
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0", "profile-profile.json"));
        var firstPath = Path.Combine(directory, "normal.sqlite3");
        var secondPath = Path.Combine(directory, "double.sqlite3");
        foreach (var path in new[] { firstPath, secondPath })
        {
            using var store = new SaveStore(path);
            store.Commit(new(0, "fixture", "fixture", "test profile", new Dictionary<string, string>
                { ["profile"] = original, ["session"] = "null" }, []));
        }
        using var normal = new GameController(firstPath, Content);
        using var accelerated = new GameController(secondPath, Content);
        foreach (var game in new[] { normal, accelerated })
        {
            var ids = game.View.Adventurers.Select(x => x.Id).ToArray();
            Assert.True(game.Execute(new("StartMatch", TargetId: "tech-first-invasion", Value: string.Join(',', ids))).Success);
            for (var index = 0; index < ids.Length; index++)
                Assert.True(game.Execute(new("Deploy", ActorId: ids[index], X: 1 + index % 2, Y: 3 + index / 2)).Success);
            Assert.True(game.Execute(new("StartWave")).Success);
        }
        Assert.True(accelerated.Execute(new("SetSpeed", Value: "2")).Success);
        for (var frame = 0; frame < 480; frame++) normal.Advance(1d / 120);
        for (var frame = 0; frame < 60; frame++) accelerated.Advance(1d / 30);
        Assert.Equal(State(normal.View), State(accelerated.View));
        Assert.Equal("Battle", normal.View.Screen);
        Assert.Equal(296000, normal.View.RemainingTimeMs);

        Assert.True(normal.Execute(new("TogglePause")).Success);
        var paused = State(normal.View);
        normal.Advance(60);
        Assert.Equal(paused, State(normal.View));
        Assert.True(normal.Execute(new("TogglePause")).Success);
        normal.Advance(.02);
        Assert.Equal(295980, normal.View.RemainingTimeMs);
    }

    private static string State(GameView view) => JsonSerializer.Serialize(new
    {
        view.Screen, view.Mode, view.StageId, view.Wave, view.RemainingTimeMs,
        view.MatchResources, view.RemainingEnemies, view.ObjectiveHp, view.Entities,
        Actors = view.Adventurers.Select(x => new { x.Id, x.Level, x.Hp, x.MaxHp, x.Deployed, x.Incapacitated, x.Skills })
    }, DomainJson.Options);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
