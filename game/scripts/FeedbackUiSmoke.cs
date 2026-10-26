using Godot;
using Guild.Application;
using Guild.Domain.Definitions;
using Guild.Domain.Serialization;
using Guild.Persistence;
using System.Text.Json;

namespace Guild.Game;

// Explicit, isolated-save-only UI scenarios; dispatched after ConfigureDecisionSmoke's guard.
public partial class Main
{
    private static bool IsFeedbackSmokeScenario(string? scenario) => scenario?.StartsWith("Feedback", StringComparison.Ordinal) == true;

    private async Task RunFeedbackUiSmoke(string scenario)
    {
        if (_controller is null || _view?.Screen != "Guild") throw new InvalidOperationException("Feedback checks require an isolated guild save.");
        if (scenario is "FeedbackHome" or "FeedbackEquipment" or "FeedbackHomeMany")
        {
            if (scenario == "FeedbackHomeMany") BuildCivicManyFixture();
            _activeTab = "actors";
            RenderScreen();
            await CivicFrames();
            var before = CivicStateFingerprint();
            if (scenario == "FeedbackEquipment")
            {
                PressSmokeButton(FeedbackControl<Button>("HomeEquipmentTab"), "open equipment tab");
                await CivicFrames();
                if (_activeTab != "equipment") throw new InvalidOperationException("Equipment tab did not open through its button.");
                var scroll = FeedbackControl<ScrollContainer>("HomeDetailsScroll");
                scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
                await CivicFrames();
                var equipment = FeedbackControl<Control>("Section_Equipment");
                var scrollbar = scroll.GetVScrollBar();
                if (scrollbar.Value + scrollbar.Page < scrollbar.MaxValue - 2)
                    throw new InvalidOperationException("Equipment details did not reach the bottom of their scroll area.");
                AssertFeedbackVisibleInScroll(equipment, scroll, "equipment section at scroll bottom");
            }
            else { RenderScreen(); await CivicFrames(); }
            AssertCivicState(before, "feedback home inspection");
            AssertFeedbackSection("Section_Skill");
            if (scenario == "FeedbackHomeMany")
            {
                var last = FeedbackControl<Button>("Inspect_" + _view.Adventurers.Last().Id);
                last.GrabFocus(); await CivicFrames();
                if (GetViewport().GuiGetFocusOwner() != last) throw new InvalidOperationException("Cannot reach member 50 from Home.");
            }
        }
        else if (scenario is "FeedbackBlessing" or "FeedbackBlessingOwned" or "FeedbackBlessingFull"
            or "FeedbackBlessingAcquire" or "FeedbackBlessingUpgrade" or "FeedbackBlessingReplace")
        {
            SeedFeedbackGrowth(scenario);
            RenderScreen(); await CivicFrames();
            var growth = _view!.NextGrowthEvent!;
            PressSmokeButton(FeedbackControl<Button>("Growth_blessing"), "begin feedback blessing");
            await CivicFrames();
            if (_view.Screen != "RankEvent" || _view.EventActorId != growth.ActorId) throw new InvalidOperationException("Wrong blessing recipient.");
            AssertFeedbackSection("OwnedBlessings");
            AssertFeedbackSection("BlessingChoiceGrid");
            var before = CivicStateFingerprint(); RenderScreen(); await CivicFrames(); AssertCivicState(before, "blessing comparison");
            if (scenario is "FeedbackBlessingFull" or "FeedbackBlessingReplace")
            {
                var replacement = FeedbackControl<OptionButton>("BlessingReplacement");
                if (replacement.ItemCount != 7) throw new InvalidOperationException("Six owned replacement choices are missing.");
                replacement.Select(1); replacement.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
                AssertCivicState(before, "blessing replacement selection");
            }
            if (scenario is "FeedbackBlessingAcquire" or "FeedbackBlessingUpgrade" or "FeedbackBlessingReplace")
            {
                var actorBefore = _view.Adventurers.Single(a => a.Id == growth.ActorId);
                var choice = _view.Choices.FirstOrDefault(candidate => candidate.IsBlessing
                    && (scenario == "FeedbackBlessingUpgrade" ? candidate.Id == "tech-vitality" && candidate.CurrentRank == 1
                        : candidate.CurrentRank == 0))
                    ?? throw new InvalidOperationException("The isolated fixture did not expose the required blessing action.");
                var replacedId = scenario == "FeedbackBlessingReplace" ? actorBefore.BlessingDetails[0].Id : null;
                var choose = FeedbackControl<Button>("ChooseBlessing_" + choice.Id);
                choose.GrabFocus(); await CivicFrames();
                AssertFeedbackVisibleInScroll(choose, FeedbackControl<ScrollContainer>("GrowthEventScroll"), "blessing choice button");
                var revisionBefore = CivicStoredState().revision;
                PressSmokeButton(choose, "confirm feedback blessing action");
                await CivicFrames();
                if (_view.Screen != "Guild" || _view.PendingEvents != 0 || CivicStoredState().revision != revisionBefore + 1)
                    throw new InvalidOperationException("Blessing action did not complete exactly once through its UI button.");
                var actorAfter = _view.Adventurers.Single(a => a.Id == growth.ActorId);
                var acquired = actorAfter.BlessingDetails.SingleOrDefault(b => b.Id == choice.Id);
                var expectedCount = actorBefore.BlessingDetails.Count + (scenario == "FeedbackBlessingAcquire" ? 1 : 0);
                if (acquired is null || acquired.Rank != choice.ResultRank || actorAfter.BlessingDetails.Count != expectedCount
                    || replacedId is not null && actorAfter.BlessingDetails.Any(b => b.Id == replacedId))
                    throw new InvalidOperationException("New acquisition, evolution, or six-slot replacement did not match the displayed choice.");
                if (scenario == "FeedbackBlessingUpgrade" && (choice.ResultRank != 2 || acquired.Rank != 2))
                    throw new InvalidOperationException("Owned blessing did not evolve from I to II.");
                using var store = new SaveStore(_controller!.SavePath);
                var saved = JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
                var savedActor = saved.Adventurers.Single(a => a.Id == growth.ActorId);
                if (savedActor.Blessings.Count != expectedCount || savedActor.Blessings.Single(b => b.Id == choice.Id).Rank != choice.ResultRank
                    || replacedId is not null && savedActor.Blessings.Any(b => b.Id == replacedId))
                    throw new InvalidOperationException("Blessing UI action did not persist the expected ownership change.");
                GD.Print($"GUILD_FEEDBACK_BLESSING_ACTION_OK scenario={scenario} rank={acquired.Rank} count={expectedCount} revisionDelta=1");
            }
        }
        else if (scenario == "FeedbackLevel")
        {
            PrepareBattleSmoke(); PrepareSmokeScreen("LevelChoice");
            RenderScreen(); await CivicFrames();
            var grid = FeedbackControl<GridContainer>("LevelChoiceGrid");
            if (grid.Columns != 3 || grid.GetChildCount() != 3 || _levelChoiceButtons.Count != 3)
                throw new InvalidOperationException("Level choice is not three cards in one row.");
            var cards = grid.GetChildren().OfType<Control>().ToArray();
            if (cards.Any(card => !GetViewport().GetVisibleRect().Grow(2).Encloses(card.GetGlobalRect()))
                || cards.Any(card => Math.Abs(card.GlobalPosition.Y - cards[0].GlobalPosition.Y) > 2))
                throw new InvalidOperationException("Level cards are outside the viewport or not horizontally aligned.");
            var before = CivicStateFingerprint(); RenderScreen(); await CivicFrames(); AssertCivicState(before, "level card comparison");
        }
        else if (scenario is "FeedbackResultDefense" or "FeedbackResultInvasion" or "FeedbackResultMany" or "FeedbackHistory")
        {
            if (scenario == "FeedbackResultMany") BuildCivicManyFixture();
            if (scenario == "FeedbackResultInvasion")
            {
                _selectedMode = "invasion"; _selectedStageId = null; OpenExpedition();
                RenderScreen(); await CivicFrames(); PressSmokeButton(_startMatchButton, "feedback invasion preparation");
                var members = _view.Adventurers.Where(a => a.InParty).ToArray();
                var cells = _view.DeploymentCells.ToArray();
                for (var index = 0; index < members.Length; index++)
                    CivicCommand(new("Deploy", ActorId: members[index].Id, X: cells[index].X, Y: cells[index].Y));
                RenderScreen();
                for (var step = 0; step < 250 && _controller!.View.Screen != "Victory"; step++)
                {
                    var view = _controller.View;
                    if (view.Screen == "Preparation")
                    {
                        PrepareInvasionPartyForSmoke();
                        if (_view.Screen == "Preparation") CivicCommand(new("StartWave"));
                    }
                    else if (view.Screen == "LevelChoice") CivicCommand(new("ChooseLevel", ActorId: view.EventActorId, TargetId: view.Choices[0].Id));
                    else if (view.Screen == "Battle") { _controller.Advance(30); _view = _controller.View; }
                    else throw new InvalidOperationException("Cannot reach invasion result: " + view.Screen);
                    RenderScreen();
                }
            }
            else { PrepareBattleSmoke(); PrepareSmokeScreen("Victory"); }
            _view = _controller!.View; RenderScreen(); await CivicFrames();
            if (_view.Screen != "Victory") throw new InvalidOperationException("Result scenario did not win.");
            var grid = FeedbackControl<GridContainer>("ResultGrid");
            if (grid.Columns != 5 || grid.GetChildCount() != _view.Adventurers.Count)
                throw new InvalidOperationException("Result roster is not a five-column grid of all members.");
            var cards = grid.GetChildren().OfType<Control>().ToArray();
            var rows = cards.Select(card => (int)Math.Round(card.GlobalPosition.Y)).Distinct().Count();
            if (rows != (cards.Length + 4) / 5) throw new InvalidOperationException("Wrong number of result rows.");
            var resultScroll = FeedbackControl<ScrollContainer>("ResultScroll");
            var viewport = GetViewport().GetVisibleRect();
            var scrollRect = resultScroll.GetGlobalRect();
            var firstRow = cards.Take(5).Select(card => card.GetGlobalRect()).ToArray();
            for (var index = 0; index < firstRow.Length; index++)
            {
                var rect = firstRow[index];
                if (rect.Size.X <= 0 || rect.Position.X < Math.Max(viewport.Position.X, scrollRect.Position.X) - 2
                    || rect.End.X > Math.Min(viewport.End.X, scrollRect.End.X) + 2
                    || Math.Abs(rect.Position.Y - firstRow[0].Position.Y) > 2
                    || index > 0 && rect.Position.X < firstRow[index - 1].End.X - 2)
                    throw new InvalidOperationException("The first five result cards overlap or extend outside the visible horizontal bounds.");
            }
            var before = CivicStateFingerprint(); RenderScreen(); await CivicFrames(); AssertCivicState(before, "result preview");
            if (scenario == "FeedbackResultMany")
            {
                var scroll = FeedbackControl<ScrollContainer>("ResultScroll");
                scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue; await CivicFrames();
                if (cards.Length != 50 || rows != 10 || scroll.ScrollVertical <= 0) throw new InvalidOperationException("Cannot reach all ten result rows.");
            }
            if (scenario == "FeedbackHistory")
            {
                PressSmokeButton(_returnButton, "confirm one result");
                _activeTab = "history"; RenderScreen(); await CivicFrames();
                if (_view.History.Count(entry => entry.Text.Contains("出撃完了", StringComparison.Ordinal)) != 1)
                    throw new InvalidOperationException("Completed match is not exactly one growth history item.");
                if (_view.History.Any(entry => entry.Text.Contains("Wave 1 開始", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Wave audit leaked into growth history.");
            }
        }
        else throw new ArgumentException("Unknown feedback scenario: " + scenario);
        GD.Print($"GUILD_FEEDBACK_OK scenario={scenario} screen={_view!.Screen} actors={_view.Adventurers.Count}");
    }

    private T FeedbackControl<T>(string name) where T : Node
        => _body.FindChild(name, true, false) as T ?? throw new InvalidOperationException("Missing feedback control: " + name);

    private void AssertFeedbackSection(string name)
    {
        var section = FeedbackControl<Control>(name);
        if (!section.IsVisibleInTree() || section.Size.X <= 0 || section.Size.Y <= 0)
            throw new InvalidOperationException("Feedback section is not visible: " + name);
    }

    private void AssertFeedbackVisibleInScroll(Control control, ScrollContainer scroll, string operation)
    {
        var rect = control.GetGlobalRect();
        if (!control.IsVisibleInTree() || rect.Size.X <= 0 || rect.Size.Y <= 0
            || !GetViewport().GetVisibleRect().Grow(2).Encloses(rect)
            || !scroll.GetGlobalRect().Grow(2).Encloses(rect))
            throw new InvalidOperationException("Feedback control is clipped or outside the viewport during " + operation + ": " + rect);
    }

    private void SeedFeedbackGrowth(string scenario)
    {
        var save = _controller!.SavePath;
        _controller.Dispose(); _controller = null;
        var definitions = ContentBundle.Load(PrepareContent());
        var fixtureContent = PrepareContent();
        var full = scenario is "FeedbackBlessingFull" or "FeedbackBlessingReplace";
        if (full)
        {
            var template = definitions.Blessings.First(item => item.Acquisition == "normal");
            definitions = definitions with { Blessings = definitions.Blessings.Concat(Enumerable.Range(1, 6)
                .Select(index => template with { Id = "tech-feedback-owned-" + index, Name = "所持枠検証用の加護" + index })).ToArray() };
            fixtureContent = save + ".content.json";
            System.IO.File.WriteAllText(fixtureContent, definitions.ToJson());
        }
        using (var store = new SaveStore(save))
        {
            var profile = JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
            var actor = profile.Adventurers[0]; actor.Rank = 2;
            actor.Blessings = full
                ? Enumerable.Range(1, 6).Select(index => new OwnedBlessing
                {
                    Id = "tech-feedback-owned-" + index,
                    // Rank III excludes owned copies from the normal draw, so the replacement
                    // action is deterministic without overriding candidates or acquisition rules.
                    Rank = scenario == "FeedbackBlessingReplace" ? 3 : 1
                }).ToList()
                : scenario is "FeedbackBlessingOwned" or "FeedbackBlessingUpgrade" ? [new() { Id = "tech-vitality", Rank = 1 }] : [];
            profile.PendingEvents = [new() { Id = "feedback-growth", ActorId = actor.Id, Rank = 2, Sequence = profile.NextEventSequence++ }];
            store.Commit(new(store.Revision, "feedback-growth-seed", "feedback-test-only", "Isolated UI boundary fixture; not gameplay balance evidence.",
                new Dictionary<string, string> { ["profile"] = JsonSerializer.Serialize(profile, DomainJson.Options) }, []));
        }
        _controller = new GameController(save, fixtureContent); _view = _controller.View;
        GD.Print("GUILD_FEEDBACK_FIXTURE isolated=true kind=" + scenario);
    }
}
