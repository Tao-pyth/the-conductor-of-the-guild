using System.Numerics;
using System.Text.Json;
using Guild.Application;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class ResultAndHistoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "guild-feedback-results-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(directory, "save.sqlite3");
    private string Content => Path.Combine(directory, "content.json");
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, DomainJson.Options)!;

    public ResultAndHistoryTests()
    {
        Directory.CreateDirectory(directory);
        var fixture = ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));
        fixture = fixture with { Stages = fixture.Stages.Select(s => s with { RewardRank = 100 }).ToArray() };
        File.WriteAllText(Content, fixture.ToJson());
        using var game = new GameController(Save, Content);
    }

    private PlayerProfile Profile()
    {
        using var store = new SaveStore(Save);
        return Read<PlayerProfile>(store.ReadDocument("profile")!);
    }

    private void Write(PlayerProfile profile, MatchSession? session = null, IReadOnlyList<HistoryEntry>? history = null)
    {
        using var store = new SaveStore(Save);
        store.Commit(new(store.Revision, "seed-" + store.Revision, "seed", "fixture", new Dictionary<string, string>
        {
            ["profile"] = Json(profile), ["session"] = Json(session)
        }, history ?? []));
    }

    private string Snapshot()
    {
        using var store = new SaveStore(Save);
        return Json(new { store.Revision, Profile = store.ReadDocument("profile"), Session = store.ReadDocument("session"), History = store.ReadHistory(0, 10000) });
    }

    private MatchSession SeedResult(string mode, bool victory, Action<PlayerProfile>? customize = null)
    {
        var profile = Profile();
        foreach (var actor in profile.Adventurers) { actor.Stats = BattleStats.Standard; actor.Hp = 80; actor.Blessings = []; }
        profile.Adventurers[1].WeaknessStacks = 2;
        profile.Adventurers[2].NextMatchChanges["str"] = 2;
        customize?.Invoke(profile);
        Write(profile);
        using (var game = new GameController(Save, Content))
        {
            var stage = game.View.Stages.Single(s => s.Mode == mode);
            var party = string.Join(',', profile.Adventurers.Take(2).Select(a => a.Id));
            Assert.True(game.Execute(new("StartMatch", TargetId: stage.Id, Value: mode == "invasion" ? party : null)).Success);
        }
        using var store = new SaveStore(Save);
        var session = Read<MatchSession>(store.ReadDocument("session")!);
        session.MatchStart = Read<BattleSnapshot>(Json(session.Current));
        session.Phase = victory ? "Victory" : "Defeat";
        session.Current.Wave = victory ? mode == "invasion" ? 3 : 10 : 2;
        session.Current.Victory = victory; session.Current.Defeated = !victory; session.Current.Running = false;
        session.Current.DefeatReason = mode == "invasion" && !victory ? "annihilation" : null;
        var first = session.Current.Units.Single(u => u.Id == profile.Adventurers[0].Id);
        first.Deployed = first.EverDeployed = true; first.Hp = 60;
        var second = session.Current.Units.Single(u => u.Id == profile.Adventurers[1].Id);
        second.Deployed = second.EverDeployed = true; second.Hp = 0;
        Write(profile, session);
        return session;
    }

    [Theory]
    [InlineData(-2, 180)]
    [InlineData(2, 220)]
    public void InvasionReserveHealthUsesPersistentStateWithoutApplyingOrConsumingNextSortieEffects(int vitalityDelta, int battleMaximum)
    {
        var session = SeedResult("invasion", true, profile =>
        {
            var reserve = profile.Adventurers[2];
            reserve.Hp = 200;
            reserve.NextMatchChanges["vit"] = vitalityDelta;
        });
        var reserveId = Profile().Adventurers[2].Id;
        var reserveSnapshot = session.MatchStart!.Units.Single(u => u.Id == reserveId);
        Assert.Equal(new BigInteger(battleMaximum), reserveSnapshot.MaxHp);
        Assert.Equal(new BigInteger(Math.Min(200, battleMaximum)), reserveSnapshot.Hp);
        using var game = new GameController(Save, Content);
        var before = Snapshot();
        var result = Assert.IsType<MatchResultView>(game.View.ResultSummary).Adventurers.Single(a => a.ActorId == reserveId);
        Assert.False(result.WasDeployed); Assert.False(result.Incapacitated);
        Assert.Equal("200", result.StartHp); Assert.Equal("200", result.EndHp); Assert.Equal("200", result.EndMaxHp);
        Assert.Equal("200", result.ReturnHp); Assert.Equal("200", result.ReturnMaxHp);
        Assert.Equal(before, Snapshot());
        Assert.True(game.Execute(new("ReturnToGuild")).Success);
        var returned = Profile().Adventurers.Single(a => a.Id == reserveId);
        Assert.Equal(new BigInteger(200), returned.Hp);
        Assert.Equal(vitalityDelta, returned.NextMatchChanges["vit"]);
    }

    [Theory]
    [InlineData("defense", true)]
    [InlineData("defense", false)]
    [InlineData("invasion", true)]
    [InlineData("invasion", false)]
    public void ResultInspectionIsPureAndEveryPreviewValueMatchesOneTimeSettlement(string mode, bool victory)
    {
        SeedResult(mode, victory);
        var beforeProfile = Profile();
        using var game = new GameController(Save, Content);
        var before = Snapshot();
        MatchResultView? result = null;
        for (var i = 0; i < 5; i++) result = Assert.IsType<MatchResultView>(game.View.ResultSummary);
        Assert.Equal(before, Snapshot());
        Assert.Equal(6, result!.Adventurers.Count);
        Assert.Equal(mode, result.Mode); Assert.Equal(victory, result.Victory);
        Assert.Equal(2, result.Adventurers.Count(a => a.WasDeployed));
        Assert.Single(result.Adventurers, a => a.Incapacitated);
        Assert.Equal("60", result.Adventurers[0].EndHp); Assert.Equal("80", result.Adventurers[0].StartHp);
        Assert.Equal("1", result.Adventurers[1].ReturnHp); Assert.Equal(3, result.Adventurers[1].WeaknessAfter);
        if (victory) Assert.True(BigInteger.Parse(result.Adventurers[0].RankAfter) > 2);
        var command = new GameCommand("ReturnToGuild", CommandId: "settlement-once");
        Assert.True(game.Execute(command).Success);
        var settled = Profile();
        var afterFirstReturn = Snapshot();
        Assert.True(game.Execute(command).Success);
        Assert.Equal(afterFirstReturn, Snapshot());
        Assert.Equal(beforeProfile.Resources + BigInteger.Parse(result.GuildResourceGain), settled.Resources);
        foreach (var row in result.Adventurers)
        {
            var actual = settled.Adventurers.Single(a => a.Id == row.ActorId);
            Assert.Equal(row.RankAfter, actual.Rank.ToString()); Assert.Equal(row.ReturnHp, actual.Hp.ToString());
            Assert.Equal(row.WeaknessAfter, actual.WeaknessStacks);
            if (!row.WasDeployed) Assert.Equal(beforeProfile.Adventurers.Single(a => a.Id == row.ActorId).NextMatchChanges, actual.NextMatchChanges);
        }
        Assert.Single(game.View.History);
        Assert.Contains(victory ? mode == "invasion" ? "全区画突破" : "全Wave突破" : mode == "invasion" ? "区画 2にて敗走" : "Wave 2にて敗走", game.View.History[0].Text);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("upgrade")]
    [InlineData("replace")]
    public void BlessingDetailsAndChoicesDescribeActualOwnedAndResultingEffects(string route)
    {
        var definitions = ContentBundle.Load(Content);
        var vitality = definitions.Blessings.Single(x => x.Id == "tech-vitality");
        definitions = definitions with
        {
            Blessings = definitions.Blessings.Concat(Enumerable.Range(0, 5).Select(i => vitality with { Id = "tech-extra-" + i, Name = "所持加護" + i })).ToArray()
        };
        File.WriteAllText(Content, definitions.ToJson());
        var profile = Profile(); var actor = profile.Adventurers[0];
        actor.Blessings = route switch
        {
            "upgrade" => [new() { Id = "tech-strength", Rank = 1, InheritedModifiers = new() { ["str|Fixed|||"] = 4 } }],
            "replace" => new[] { "tech-vitality" }.Concat(Enumerable.Range(0, 5).Select(i => "tech-extra-" + i)).Select(id => new OwnedBlessing { Id = id }).ToList(),
            _ => [new() { Id = "tech-vitality" }]
        };
        profile.PendingEvents = [new() { Id = "selected", ActorId = actor.Id, Rank = 2, Sequence = 1, Kind = "blessing", Candidates = ["tech-strength"], DefinitionJson = definitions.ToJson() }];
        Write(profile);
        using var game = new GameController(Save, Content);
        var before = Snapshot();
        var detail = game.View.Adventurers[0];
        Assert.Equal(actor.Blessings.Count, detail.BlessingDetails.Count);
        Assert.All(detail.BlessingDetails, b => { Assert.NotEmpty(b.Name); Assert.NotEmpty(b.Description); Assert.DoesNotContain("vit ", b.Description); });
        var choice = Assert.Single(game.View.Choices);
        Assert.True(choice.IsBlessing); Assert.Equal(route == "upgrade" ? 1 : 0, choice.CurrentRank);
        Assert.Equal(route == "upgrade" ? 2 : 1, choice.ResultRank);
        if (route == "upgrade") { Assert.Contains("+4.00", detail.BlessingDetails[0].Description); Assert.Contains("+6.00", choice.Description); }
        Assert.Equal(before, Snapshot());
        var acquired = game.Execute(new("ChooseBlessing", TargetId: choice.Id, Value: route == "replace" ? "tech-vitality" : null));
        Assert.True(acquired.Success, acquired.Message);
        var actual = Profile().Adventurers[0];
        Assert.Equal(choice.ResultRank, actual.Blessings.Single(b => b.Id == choice.Id).Rank);
        Assert.Contains(definitions.Blessings.Single(b => b.Id == choice.Id).Name, acquired.Message);
        if (route == "replace") { Assert.Equal(6, actual.Blessings.Count); Assert.Contains(vitality.Name, acquired.Message); Assert.DoesNotContain(actual.Blessings, b => b.Id == vitality.Id); }
        if (route == "upgrade") Assert.Contains("I→II", acquired.Message);
        using var reloaded = new GameController(Save, Content);
        Assert.Equal(game.View.History.ToArray(), reloaded.View.History.ToArray());
    }

    [Fact]
    public void LegacyAuditIsPreservedWhileGrowthHistorySummarizesTheFinalAttemptAndResolvedChoices()
    {
        var session = SeedResult("defense", true);
        var initial = Profile(); var actor = initial.Adventurers[0];
        var frozen = ContentBundle.Load(Content) with
        {
            Blessings = ContentBundle.Load(Content).Blessings.Select(b => b.Id == "tech-vitality" ? b with { Name = "当時の活力" } : b).ToArray()
        };
        initial.PendingEvents = [new() { Id = "old-event", Sequence = 1, ActorId = actor.Id, Rank = 24 }];
        var selected = Read<PlayerProfile>(Json(initial));
        selected.PendingEvents[0].Kind = "blessing"; selected.PendingEvents[0].DefinitionJson = frozen.ToJson();
        var acquired = Read<PlayerProfile>(Json(selected));
        acquired.PendingEvents.Clear(); acquired.Adventurers[0].Blessings = [new() { Id = "tech-vitality" }];
        var returned = Read<PlayerProfile>(Json(acquired)); returned.Resources += 1000; returned.Adventurers[0].Rank++;
        var failed = Read<MatchSession>(Json(session)); failed.Phase = "Defeat"; failed.Current.Wave = 2;
        var rows = new List<HistoryEntry>
        {
            new("old-start", "player", "0", "StartMatch", "{}", "{}", "出撃準備です。"),
            new("old-failed", "player", "0", "WaveEnd", "{}", Json(failed), "防衛に失敗しました。"),
            new("old-retry", "player", "0", "Retry", "{}", "{}", "再挑戦しました。"),
            new("old-lv", actor.Id, "0", "ChooseLevel", "{}", "{}", "Lv強化を確定しました。"),
            new("old-wave", "player", "0", "StartWave", "{}", "{}", "Wave 10開始。"),
            new("old-finish", "player", "0", "WaveEnd", "{}", Json(session), "全Waveを防衛しました。"),
            new("old-return", "player", "0", "ReturnToGuild", Json(acquired), Json(returned), "結果を1回精算しました。"),
            new("old-actor", actor.Id, "0", "AdventurerResult", Json(actor), Json(returned.Adventurers[0]), "Rank上昇"),
            new("old-begin", actor.Id, "2", "BeginEvent", Json(initial), Json(selected), "Rank 24イベントを選択しました。"),
            new("old-choice", actor.Id, "2", "ChooseBlessing", Json(selected), Json(acquired), "加護取得を確定しました。")
        };
        Write(acquired, history: rows);
        using var rawStore = new SaveStore(Save);
        var originalRaw = Json(rawStore.ReadHistory(0, 10000));
        using var game = new GameController(Save, Content);
        var history = game.View.History;
        Assert.Equal(3, history.Count);
        Assert.Contains("全Wave突破", history[0].Text); Assert.DoesNotContain("敗走", history[0].Text);
        Assert.Contains("加護取得", history[1].Text); Assert.Contains("当時の活力 I", history[2].Text);
        Assert.DoesNotContain(history, h => h.Text.Contains("Lv強化", StringComparison.Ordinal));
        Assert.Equal(originalRaw, Json(rawStore.ReadHistory(0, 10000)));
        using var reloaded = new GameController(Save, Content);
        Assert.Equal(history.ToArray(), reloaded.View.History.ToArray());
    }

    [Fact]
    public void LegacyMissingOutcomeAndChoiceMetadataRetainsOriginalTextWithoutInventingDetails()
    {
        var profile = Profile(); var id = profile.Adventurers[0].Id;
        Write(profile, history:
        [
            new("old-return", "player", "0", "ReturnToGuild", "{}", "{}", "旧出撃結果"),
            new("old-choose", id, "0", "ChooseBlessing", "{}", "{}", "旧加護取得記録")
        ]);
        using var game = new GameController(Save, Content);
        Assert.Equal(new[] { "旧出撃結果", "旧加護取得記録" }, game.View.History.Select(h => h.Text));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
