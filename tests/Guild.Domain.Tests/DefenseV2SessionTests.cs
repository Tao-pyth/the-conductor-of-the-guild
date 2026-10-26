using System.Text.Json;
using Guild.Application;
using Guild.Domain.Battle;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class DefenseV2SessionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "guild-defense-v2-session-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(root, "save.sqlite3");
    private static string Content => Path.Combine(AppContext.BaseDirectory, "tech-content.json");
    public DefenseV2SessionTests() => Directory.CreateDirectory(root);

    private static void Execute(GameController game, GameCommand command)
    {
        var result = game.Execute(command);
        Assert.True(result.Success, command.Type + ": " + result.Message);
    }

    private MatchSession ReadSession()
    {
        using var store = new SaveStore(Save);
        return JsonSerializer.Deserialize<MatchSession>(store.ReadDocument("session")!, DomainJson.Options)!;
    }

    private PlayerProfile ReadProfile()
    {
        using var store = new SaveStore(Save);
        return JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    private static void AssertV2(MatchSession session)
    {
        Assert.Equal("defense-v2", session.Current.RuleSetId);
        if (session.MatchStart is { } start) Assert.Equal("defense-v2", start.RuleSetId);
        if (session.WaveStart is { } wave) Assert.Equal("defense-v2", wave.RuleSetId);
        Assert.Equal(3, session.FormatVersion);
        Assert.Equal(1, session.Current.SimulationVersion);
    }

    [Fact]
    public void SaveRestartAndNaturalDefeatRetryKeepV2AndTheirCorrectGrowthRandomCheckpoints()
    {
        MatchSession firstWave;
        string actorId;
        using (var game = new GameController(Save, Content))
        {
            Execute(game, new("StartMatch"));
            var preparation = ReadSession();
            AssertV2(preparation); Assert.Null(preparation.MatchStart); Assert.Null(preparation.WaveStart);
            actorId = game.View.Adventurers[0].Id;
            // Four tiles away from the lane: this is a legal real placement that
            // naturally loses to leaks, without changing HP/resources/definitions.
            Execute(game, new("Deploy", ActorId: actorId, X: 1, Y: 1));
            Execute(game, new("StartWave"));
            firstWave = ReadSession();
            AssertV2(firstWave); Assert.NotNull(firstWave.MatchStart); Assert.NotNull(firstWave.WaveStart);
            Assert.NotNull(firstWave.MatchStartGrowthRng); Assert.NotNull(firstWave.WaveStartGrowthRng);
            game.Advance(.14);
            Execute(game, new("SetTarget", ActorId: actorId, Value: "arrival"));
            var midWave = ReadSession();
            AssertV2(midWave);
            Assert.True(midWave.Current.TimeMs > midWave.WaveStart!.TimeMs);
            Assert.Equal(Json(firstWave.MatchStartGrowthRng), Json(midWave.MatchStartGrowthRng));
            Assert.Equal(Json(firstWave.WaveStartGrowthRng), Json(midWave.WaveStartGrowthRng));
        }

        using var resumed = new GameController(Save, Content);
        Assert.Equal("Battle", resumed.View.Screen);
        Assert.Contains(resumed.View.Entities, e => e.Kind == "enemy");
        Assert.All(resumed.View.Entities.Where(e => e.Kind == "enemy"), e => Assert.Equal(0, e.X));
        AssertV2(ReadSession());
        Assert.Equal(Json(firstWave.WaveStartGrowthRng), Json(ReadProfile().GrowthRng));

        for (var i = 0; resumed.View.Screen != "Defeat" && i < 10000; i++)
        {
            switch (resumed.View.Screen)
            {
                case "Preparation": Execute(resumed, new("StartWave")); break;
                case "LevelChoice":
                    var view = resumed.View;
                    var choice = view.Choices.FirstOrDefault(c => c.Id == "firepower") ?? view.Choices[0];
                    Execute(resumed, new("ChooseLevel", ActorId: view.EventActorId, TargetId: choice.Id));
                    break;
                case "Battle": resumed.Advance(.2); break;
                default: Assert.Fail("Unexpected screen: " + resumed.View.Screen); break;
            }
        }
        Assert.Equal("Defeat", resumed.View.Screen);
        Assert.Equal(0, resumed.View.DefenseHp);
        var defeated = ReadSession();
        AssertV2(defeated);
        Assert.Equal(Json(firstWave.MatchStart), Json(defeated.MatchStart));
        var oldAttempt = defeated.AttemptId;
        Execute(resumed, new("Retry"));
        Assert.Equal("Preparation", resumed.View.Screen);
        var retried = ReadSession();
        AssertV2(retried);
        Assert.NotEqual(oldAttempt, retried.AttemptId);
        Assert.Null(retried.WaveStart); Assert.Null(retried.WaveStartGrowthRng);
        Assert.Equal(Json(firstWave.MatchStartGrowthRng), Json(retried.MatchStartGrowthRng));
        Assert.Equal(Json(firstWave.MatchStartGrowthRng), Json(ReadProfile().GrowthRng));
        var expected = JsonSerializer.Deserialize<BattleSnapshot>(Json(firstWave.MatchStart), DomainJson.Options)!;
        expected.Setup = expected.Setup with { AttemptId = retried.AttemptId };
        Assert.Equal(Json(expected), Json(retried.Current));

        Execute(resumed, new("StartWave"));
        var newTrial = ReadSession();
        AssertV2(newTrial);
        Assert.Equal(retried.AttemptId, newTrial.Current.Setup.AttemptId);
        Assert.Equal(retried.AttemptId, newTrial.WaveStart!.Setup.AttemptId);
        Assert.Equal(Json(firstWave.WaveStartGrowthRng), Json(newTrial.WaveStartGrowthRng));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var resolved = Path.GetFullPath(root);
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected temporary fixture directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
