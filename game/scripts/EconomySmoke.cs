using Godot;
using Guild.Application;
using Guild.Domain.Battle;
using Guild.Domain.Serialization;
using Guild.Persistence;
using System.Numerics;
using System.Text.Json;

namespace Guild.Game;

// These scenarios require an explicit, isolated save and never open the player's default database.
public partial class Main
{
    private string? _economySmokeScenario;
    private bool _economySmokePending;
    private bool _economySmokeRunning;
    private bool _economySmokeFailed;

    private void ConfigureEconomySmoke(string[] args)
    {
        _economySmokeScenario = ReadArgument(args, "--economy-scenario");
        if (_economySmokeScenario is null) return;
        if (_smokeSeconds <= 0 || ReadArgument(args, "--save-path") is null
            || args.Contains("--decision-scenario") || args.Contains("--invasion-scenario")
            || args.Contains("--smoke-screen") || args.Contains("--smoke-battle") || args.Contains("--invasion-demo") || args.Contains("--smoke-rename"))
            throw new InvalidOperationException("Economy scenarios require only an explicit isolated save and bounded smoke invocation.");
        if (_economySmokeScenario is not ("Dispatch" or "Reserved" or "Deliveries" or "Funding"
            or "PreparationDefense" or "PreparationInvasion" or "ReturnDefense" or "ReturnInvasion" or "Reopen"))
            throw new ArgumentException("Unknown economy scenario: " + _economySmokeScenario);
        _economySmokePending = true;
    }

    private async void RunEconomySmoke()
    {
        _economySmokePending = false; _economySmokeRunning = true;
        try
        {
            if (_controller is null || _view?.Screen != "Guild") throw new InvalidOperationException("Economy scenarios must start at an isolated guild.");
            var scenario = _economySmokeScenario!;
            if (scenario == "Deliveries") await SmokeEconomyInsufficientMaterials();
            SeedEconomySmoke();
            if (scenario == "Deliveries") await SmokeEconomyDeliveries();
            else if (scenario == "Funding") await SmokeEconomyFunding();
            else
            {
                await SmokeEconomyDispatch(scenario != "Dispatch");
                if (scenario is not ("Dispatch" or "Reserved"))
                {
                    var invasion = scenario is "PreparationInvasion" or "ReturnInvasion";
                    await SmokeEconomyPreparation(invasion);
                    if (scenario is "ReturnDefense" or "ReturnInvasion") await SmokeEconomyReturn(invasion);
                    if (scenario == "Reopen") await SmokeEconomyReopen();
                }
            }
            AssertEconomyVisible();
            var before = CivicStateFingerprint();
            RenderScreen(); await CivicFrames(); AssertCivicState(before, "economy final inspection");
            GD.Print("GUILD_ECONOMY_OK scenario=" + scenario);
        }
        catch (Exception exception)
        {
            _economySmokeFailed = true;
            GD.PushError("GUILD_ECONOMY_SCENARIO_FAILED " + exception);
            GetTree().Quit(1);
        }
        finally { _economySmokeRunning = false; }
    }

    private void SeedEconomySmoke()
    {
        var save = _controller!.SavePath;
        _controller.Dispose(); _controller = null;
        using (var store = new SaveStore(save))
        {
            var profile = JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
            profile.Resources = 5000;
            foreach (var id in new[] { "wood", "ore", "herb", "crystal" }) profile.Economy.Materials[id] = 1000;
            foreach (var actor in profile.Adventurers)
            {
                actor.Rank = 20; actor.Experience = 0;
                actor.Stats = new BattleStats(100, 100, 100, 100, 100); actor.Hp = 1100;
                actor.Blessings = [new() { Id = "tech-strength", Rank = 1 }, new() { Id = "tech-vitality", Rank = 1 }];
            }
            store.Commit(new(store.Revision, "economy-ui-fixture-" + Guid.NewGuid().ToString("N"), "economy-ui-fixture",
                "Isolated UI boundary fixture only; seeded funding, materials, Rank, stats and two existing blessings. Not balance evidence.",
                new Dictionary<string, string> { ["profile"] = JsonSerializer.Serialize(profile, DomainJson.Options) }, []));
        }
        _controller = new GameController(save, PrepareContent()); _view = _controller.View;
        _activeTab = "actors"; RenderScreen();
        GD.Print("GUILD_ECONOMY_FIXTURE isolated=true gold=5000 materials=1000 rank=20 stats=100 balanceEvidence=false");
    }

    private async Task SmokeEconomyDispatch(bool reserve)
    {
        var before = CivicStateFingerprint();
        PressSmokeButton(CivicControl<Button>("HomeDispatchTab"), "open dispatch tab"); await CivicFrames();
        var picker = CivicControl<OptionButton>("EconomyMissionPicker");
        var surveyIndex = _view!.Economy.Missions.ToList().FindIndex(mission => mission.Id == "survey");
        if (surveyIndex < 0) throw new InvalidOperationException("Survey mission is missing.");
        picker.Select(surveyIndex); picker.EmitSignal(OptionButton.SignalName.ItemSelected, surveyIndex); await CivicFrames();
        foreach (var actor in _view.Adventurers)
        {
            var check = CivicControl<CheckBox>("EconomyMember_" + actor.Id);
            check.ButtonPressed = true;
        }
        if (_economyReserveButton?.Disabled != true || _controller!.PreviewDispatch("survey", _economyDispatchIds.ToArray()).CanReserve)
            throw new InvalidOperationException("Dispatching the entire roster was not rejected.");
        foreach (var actor in _view.Adventurers) CivicControl<CheckBox>("EconomyMember_" + actor.Id).ButtonPressed = false;
        foreach (var actor in _view.Adventurers.Where(actor => actor.WeaponType is "hammer" or "staff").Take(2))
            CivicControl<CheckBox>("EconomyMember_" + actor.Id).ButtonPressed = true;
        await CivicFrames();
        var preview = _controller!.PreviewDispatch("survey", _economyDispatchIds.Order(StringComparer.Ordinal).ToArray());
        if (_economyDispatchIds.Count != 2 || preview.SuccessPercent != 100 || !preview.CanReserve
            || _economyDispatchPreview is null || !_economyDispatchPreview.Text.Contains("100%", StringComparison.Ordinal))
            throw new InvalidOperationException("Displayed survey preview does not match rank/weapon/blessing evaluation.");
        AssertCivicState(before, "dispatch mission and member preview");
        GD.Print("GUILD_ECONOMY_PREVIEW_OK members=2 percent=100 allMembersRejected=true readOnly=true");
        if (!reserve)
        {
            _economyReserveButton!.GrabFocus(); await CivicFrames();
            AssertFeedbackVisibleInScroll(_economyReserveButton, CivicControl<ScrollContainer>("EconomyDispatchScroll"), "dispatch reservation button");
            return;
        }
        var expectedIds = _economyDispatchIds.Order(StringComparer.Ordinal).ToArray();
        var revision = CivicStoredState().revision;
        PressSmokeButton(_economyReserveButton, "reserve dispatch"); await CivicFrames();
        var status = _view!.Economy.Dispatch;
        if (status is null || status.Active || !status.ActorIds.Order(StringComparer.Ordinal).SequenceEqual(expectedIds)
            || CivicStoredState().revision != revision + 1 || _view.Adventurers.Count(actor => actor.IsDispatched) != 2)
            throw new InvalidOperationException("Dispatch reservation did not save once or bind the displayed members.");
        PressSmokeButton(CivicControl<Button>("EconomyCancelDispatch"), "cancel dispatch reservation"); await CivicFrames();
        if (_view.Economy.Dispatch is not null || _view.Adventurers.Any(actor => actor.IsDispatched))
            throw new InvalidOperationException("Reservation cancellation did not release the members.");
        foreach (var id in expectedIds) CivicControl<CheckBox>("EconomyMember_" + id).ButtonPressed = true;
        PressSmokeButton(_economyReserveButton, "reserve dispatch again"); await CivicFrames();
        if (_view.Economy.Dispatch is not { Active: false }) throw new InvalidOperationException("Cannot reserve after cancellation.");
        GD.Print("GUILD_ECONOMY_RESERVATION_OK cancelAndReserve=true");
    }

    private async Task SmokeEconomyFunding()
    {
        _selectedMode = "defense"; _selectedStageId = null;
        PressSmokeButton(CivicControl<Button>("HomeExpedition"), "open funded expedition"); await CivicFrames();
        var before = CivicStateFingerprint();
        var input = CivicControl<LineEdit>("EconomyContributionInput");
        foreach (var invalid in new[] { "-1", "5001", "999999999999999999999999999999999999999", "0.5", "" })
        {
            input.Text = invalid; input.EmitSignal(LineEdit.SignalName.TextChanged, invalid);
            if (_startMatchButton?.Disabled != true) throw new InvalidOperationException("Invalid contribution was accepted: " + invalid);
        }
        input.Text = "250"; input.EmitSignal(LineEdit.SignalName.TextChanged, "250"); await CivicFrames();
        if (_startMatchButton?.Disabled != false || !_economyContributionSummary!.Text.Contains("1250", StringComparison.Ordinal)
            || !_economyContributionSummary.Text.Contains("4750", StringComparison.Ordinal))
            throw new InvalidOperationException("Contribution balance preview is incorrect.");
        AssertCivicState(before, "contribution input and invalid amount inspection");
        GD.Print("GUILD_ECONOMY_FUNDING_OK gold=5000 contribution=250 mission=1250 treasury=4750 readOnly=true");
    }

    private async Task SmokeEconomyPreparation(bool invasion)
    {
        PressSmokeButton(CivicControl<Button>("HomeExpedition"), "open expedition after dispatch reservation"); await CivicFrames();
        PressSmokeButton(CivicControl<Button>(invasion ? "Mode_invasion" : "Mode_defense"), "choose economy battle mode"); await CivicFrames();
        if (invasion)
            foreach (var member in _view!.Adventurers.Where(actor => actor.IsDispatched))
                if (!CivicControl<CheckBox>("DecisionParty_" + member.Id).Disabled || _expeditionIds.Contains(member.Id))
                    throw new InvalidOperationException("A dispatched member remains selectable in the invasion party.");
        var input = CivicControl<LineEdit>("EconomyContributionInput");
        input.Text = "250"; input.EmitSignal(LineEdit.SignalName.TextChanged, "250");
        PressSmokeButton(_startMatchButton, "start funded match with dispatch"); await CivicFrames();
        if (_view!.Screen != "Preparation" || _view.GuildResources != "4750" || _view.MatchResources != "1250"
            || _view.Economy.Dispatch is not { Active: true, SuccessPercent: 100 })
            throw new InvalidOperationException("Funding debit or dispatch departure does not match the preview.");
        foreach (var member in _view.Adventurers.Where(actor => actor.IsDispatched))
            if (_body.FindChild("Inspect_" + member.Id, true, false) is not null)
                throw new InvalidOperationException("A dispatched member remains in the deployment roster.");
        GD.Print("GUILD_ECONOMY_PREPARATION_OK mode=" + _view.Mode + " mission=1250 treasury=4750 dispatchedExcluded=true");
    }

    private async Task SmokeEconomyDeliveries()
    {
        var pure = CivicStateFingerprint();
        PressSmokeButton(CivicControl<Button>("HomeDeliveryTab"), "open materials and deliveries"); await CivicFrames();
        if (_view!.Economy.Inventory.Count != 4 || _view.Economy.Factions.Count != 4 || _view.Economy.Deliveries.Count != 13)
            throw new InvalidOperationException("The material/faction/delivery catalog is incomplete.");
        AssertCivicState(pure, "inventory and equipment-offer inspection");
        var items = _view.Items.Count;
        var guildGold = BigInteger.Parse(_view.GuildResources);
        foreach (var kind in new[] { "relation", "equipment", "gold" })
        {
            var delivery = _view.Economy.Deliveries.First(item => item.Kind == kind && item.CanComplete);
            var revision = CivicStoredState().revision;
            var materials = _view.Economy.Inventory.ToDictionary(item => item.Id, item => BigInteger.Parse(item.Quantity));
            var button = CivicControl<Button>("EconomyDeliver_" + delivery.Id);
            button.GrabFocus(); await CivicFrames();
            AssertFeedbackVisibleInScroll(button, CivicControl<ScrollContainer>("EconomyDeliveryScroll"), "delivery confirmation");
            PressSmokeButton(button, "confirm " + kind + " delivery"); await CivicFrames();
            if (CivicStoredState().revision != revision + 1
                || !_view.Economy.Inventory.Any(item => BigInteger.Parse(item.Quantity) < materials[item.Id]))
                throw new InvalidOperationException("Delivery did not atomically consume materials once: " + kind);
        }
        if (_view.Items.Count != items + 1 || BigInteger.Parse(_view.GuildResources) <= guildGold
            || !_view.Economy.Factions.Any(faction => faction.Trust > 0))
            throw new InvalidOperationException("The three delivery rewards were not applied.");
        GetViewport().GuiReleaseFocus();
        CivicControl<ScrollContainer>("EconomyDeliveryScroll").ScrollVertical = 0; await CivicFrames();
        GD.Print("GUILD_ECONOMY_DELIVERIES_OK kinds=relation,equipment,gold atomic=true equipment=1");
    }

    private async Task SmokeEconomyInsufficientMaterials()
    {
        var before = CivicStateFingerprint();
        PressSmokeButton(CivicControl<Button>("HomeDeliveryTab"), "inspect initially insufficient materials"); await CivicFrames();
        foreach (var delivery in _view!.Economy.Deliveries)
            if (delivery.CanComplete || string.IsNullOrWhiteSpace(delivery.UnavailableReason)
                || !CivicControl<Button>("EconomyDeliver_" + delivery.Id).Disabled)
                throw new InvalidOperationException("A delivery without materials did not show its unavailable reason.");
        AssertCivicState(before, "insufficient-material delivery inspection");
        GD.Print("GUILD_ECONOMY_INSUFFICIENT_OK disabled=13 readOnly=true");
    }

    private async Task SmokeEconomyReturn(bool invasion)
    {
        var absent = _view!.Economy.Dispatch!.ActorIds.ToHashSet(StringComparer.Ordinal);
        var participants = _view.Adventurers.Where(actor => !actor.IsDispatched).ToArray();
        for (var index = 0; index < participants.Length; index++)
        {
            var cell = invasion ? _view.DeploymentCells[index] : new Guild.Domain.Definitions.GridPoint(2 + index * 2, index % 2 == 0 ? 4 : 6);
            CivicCommand(new("Deploy", ActorId: participants[index].Id, X: cell.X, Y: cell.Y));
        }
        RenderScreen(); await CivicFrames();
        PressSmokeButton(_startWaveButton, "begin economy match");
        for (var step = 0; step < 300 && _view!.Screen != "Victory"; step++)
        {
            if (_view.Screen == "Preparation") CivicCommand(new("StartWave"));
            else if (_view.Screen == "LevelChoice") CivicCommand(new("ChooseLevel", ActorId: _view.EventActorId, TargetId: _view.Choices[0].Id));
            else if (_view.Screen == "Battle") { _controller!.Advance(30); _view = _controller.View; }
            else throw new InvalidOperationException("Cannot finish isolated economy battle: " + _view.Screen);
        }
        if (_view?.Screen != "Victory") throw new InvalidOperationException("Economy battle did not reach victory.");
        RenderScreen(); await CivicFrames();
        foreach (var id in absent)
            if (!_view.ResultSummary!.Adventurers.Single(actor => actor.ActorId == id).WasDispatched
                || CivicControl<Label>("ResultActivity_" + id).Text != DisplayText.Get("economy.ui.result_dispatched"))
                throw new InvalidOperationException("The result card did not distinguish dispatch from an ordinary reserve.");
        PressSmokeButton(_returnButton, "settle match and dispatch once"); await CivicFrames();
        if (_view.Screen != "Guild" || _view.Economy.Dispatch is not null || _view.Adventurers.Any(actor => actor.IsDispatched)
            || string.IsNullOrWhiteSpace(_view.Economy.LastReport) || _view.Economy.Inventory.All(item => BigInteger.Parse(item.Quantity) <= 1000))
            throw new InvalidOperationException("Return did not receive materials and release the dispatch.");
        foreach (var actor in _view.Adventurers.Where(actor => absent.Contains(actor.Id)))
            if (actor.Hp != "1100") throw new InvalidOperationException("Dispatch health changed in a combat result.");
        PressSmokeButton(CivicControl<Button>("HomeEconomyReport"), "read dispatch return report"); await CivicFrames();
        if (_activeTab != "deliveries" || _body.FindChild("EconomyLastReport", true, false) is null)
            throw new InvalidOperationException("Return report is not reachable from Home.");
        GD.Print("GUILD_ECONOMY_RETURN_OK mode=" + (invasion ? "invasion" : "defense") + " report=true absentHpPreserved=true");
    }

    private async Task SmokeEconomyReopen()
    {
        var save = _controller!.SavePath;
        var expected = JsonSerializer.Serialize(_view!.Economy);
        var wallet = _view.GuildResources; var missionGold = _view.MatchResources;
        var persisted = CivicStoredState();
        _controller.Dispose(); _controller = new GameController(save, PrepareContent()); _view = _controller.View;
        RenderScreen(); await CivicFrames();
        if (JsonSerializer.Serialize(_view.Economy) != expected || _view.GuildResources != wallet || _view.MatchResources != missionGold
            || CivicStoredState() != persisted || _view.Economy.Dispatch is not { Active: true })
            throw new InvalidOperationException("Reopen changed dispatch, contribution, RNG, or persisted state.");
        GD.Print("GUILD_ECONOMY_REOPEN_OK snapshot=true noAdditionalDebit=true");
    }

    private void AssertEconomyVisible()
    {
        var viewport = GetViewport().GetVisibleRect();
        foreach (var name in new[] { "CivicShell", "CivicBody" })
        {
            var control = CivicControl<Control>(name);
            if (!control.IsVisibleInTree() || !viewport.Grow(2).Encloses(control.GetGlobalRect()))
                throw new InvalidOperationException("Economy shell extends outside the viewport: " + name);
        }
        var scroll = _body.FindChild("EconomyDispatchScroll", true, false) as ScrollContainer
            ?? _body.FindChild("EconomyDeliveryScroll", true, false) as ScrollContainer;
        if (scroll is not null && (!viewport.Grow(2).Encloses(scroll.GetGlobalRect()) || scroll.Size.Y < 50))
            throw new InvalidOperationException("Economy content cannot be reached in its scroll area.");
        if (!_message.IsVisibleInTree() || !viewport.Grow(2).Encloses(_message.GetGlobalRect()))
            throw new InvalidOperationException("Economy footer is outside the viewport.");
    }
}
