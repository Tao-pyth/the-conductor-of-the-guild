using Godot;
using Guild.Application;
using System.Globalization;
using System.IO;

namespace Guild.Game;

/// <summary>Presentation adapter. All game changes pass through GameController commands.</summary>
public partial class Main : Control
{
    private static readonly Color TextColor = new("273c3c");
    private static readonly Color MutedColor = new("566b60");
    private GameController? _controller;
    private GameView? _view;
    private VBoxContainer _body = null!;
    private Label _status = null!;
    private Label _summary = null!;
    private Label _message = null!;
    private Label? _actorDetails;
    private GuildMap? _map;
    private readonly Dictionary<string, Button> _actorButtons = new();
    private readonly Dictionary<string, ProgressBar> _skillBars = new();
    private string? _selectedActor;
    private string? _selectedBlessing;
    private string _activeTab = "actors";
    private string _screen = "";
    private string _lastChoiceIds = "";
    private bool _repositionMode;
    private double _refreshElapsed;
    private double _elapsed;
    private double _smokeSeconds;
    private string? _capturePath;
    private bool _captured;
    private bool _captureComplete;
    private bool _captureFailed;
    private int _captureDelayFrames;
    private string? _renameSmokeName;
    private bool _renameSmokeComplete;
    private LineEdit? _renameInput;
    private Button? _renameButton;
    private bool _choosingExpedition;
    private string _selectedMode = "defense";
    private string? _selectedStageId;
    private readonly HashSet<string> _expeditionIds = new(StringComparer.Ordinal);
    private Button? _startMatchButton;
    private Button? _startWaveButton;
    private Button? _retryButton;
    private Button? _returnButton;
    private Button? _buyLevelButton;
    private Button? _reviveButton;
    private readonly List<Button> _levelChoiceButtons = [];
    private Label? _partyCount;
    private Label? _preparationCosts;
    private string? _invasionSmokeScenario;
    private bool _invasionSmokePending;
    private bool _invasionSmokeRunning;
    private bool _invasionSmokeFailed;

    public override void _Ready()
    {
        Name = "GuildApplication";
        Theme = CreateTheme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildShell();
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _smokeSeconds = ReadDoubleArgument(args, "--smoke-test");
            _capturePath = ReadArgument(args, "--capture");
            var savePath = ReadArgument(args, "--save-path") ?? ProjectSettings.GlobalizePath("user://guild-save.sqlite3");
            var contentPath = PrepareContent();
            _controller = new GameController(savePath, contentPath);
            _view = _controller.View;
            _selectedActor = _view.Adventurers.FirstOrDefault()?.Id;
            ConfigureDecisionSmoke(args);
            ConfigureEconomySmoke(args);
            if (args.Contains("--invasion-demo") || args.Contains("--invasion-scenario"))
            {
                if (_smokeSeconds <= 0 || ReadArgument(args, "--save-path") is null)
                    throw new InvalidOperationException(DisplayText.Get("ui.main.001"));
                _invasionSmokeScenario = ReadArgument(args, "--invasion-scenario") ?? "Battle";
                _invasionSmokePending = true;
            }
            if (args.Contains("--smoke-rename"))
            {
                if (_smokeSeconds <= 0 || ReadArgument(args, "--save-path") is null || _view.Screen != "Guild")
                    throw new InvalidOperationException(DisplayText.Get("ui.main.002"));
                _renameSmokeName = ReadArgument(args, "--smoke-rename") ?? DisplayText.Get("ui.main.003");
                if (SelectedActor()?.Name == _renameSmokeName)
                {
                    _renameSmokeComplete = true;
                    GD.Print($"GUILD_RENAME_REOPEN_OK name={_renameSmokeName}");
                }
            }
            var smokeScreen = ReadArgument(args, "--smoke-screen");
            if (args.Contains("--smoke-battle") || smokeScreen is not null)
            {
                if (_smokeSeconds <= 0 || ReadArgument(args, "--save-path") is null)
                    throw new InvalidOperationException(DisplayText.Get("ui.main.004"));
                PrepareBattleSmoke();
                if (smokeScreen is not null) PrepareSmokeScreen(smokeScreen);
            }
            RenderScreen();
            if (_invasionSmokePending) Callable.From(PrepareInvasionSmoke).CallDeferred();
            if (_decisionSmokePending) Callable.From(RunDecisionSmoke).CallDeferred();
            if (_economySmokePending) Callable.From(RunEconomySmoke).CallDeferred();
            GD.Print($"GUILD_SCENE_READY screen={_view.Screen} actors={_view.Adventurers.Count} save={savePath}");
        }
        catch (Exception ex)
        {
            GD.PushError(ex.ToString());
            _status.Text = DisplayText.Get("ui.main.005");
            _message.Text = ex.Message;
            AddText(_body, DisplayText.Get("ui.main.006"), true);
            if (_smokeSeconds > 0) GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_controller is not null)
        {
            try
            {
                if (!_invasionSmokeRunning && !_invasionSmokePending && !_decisionSmokeRunning && !_decisionSmokePending && !_economySmokeRunning && !_economySmokePending) _controller.Advance(delta);
                _view = _controller.View;
                var choiceIds = string.Join('|', _view.Choices.Select(x => x.Id));
                if (_screen != _view.Screen || (_view.Screen is "LevelChoice" or "RankEvent" && _lastChoiceIds != choiceIds))
                    RenderScreen();
                _refreshElapsed += delta;
                if (_refreshElapsed >= 0.1)
                {
                    RefreshValues();
                    _refreshElapsed = 0;
                }
                _map?.QueueRedraw();
                if (_renameSmokeName is not null && !_renameSmokeComplete && _elapsed > 0.5) RunRenameSmoke();
            }
            catch (Exception ex)
            {
                _message.Text = DisplayText.Get("ui.main.007") + ex.Message;
                GD.PushError(ex.ToString());
                SetProcess(false);
                if (_smokeSeconds > 0) GetTree().Quit(1);
            }
        }
        if (_captureDelayFrames > 0) _captureDelayFrames--;
        if (!_captured && _captureDelayFrames == 0 && !_invasionSmokePending && !_invasionSmokeRunning && !_decisionSmokePending && !_decisionSmokeRunning && !_economySmokePending && !_economySmokeRunning && !string.IsNullOrEmpty(_capturePath) && _elapsed > 1)
        {
            _captured = true;
            Callable.From(CaptureScreen).CallDeferred();
        }
        if (_smokeSeconds > 0 && !_captureFailed && !_invasionSmokeFailed && !_invasionSmokePending && !_invasionSmokeRunning
            && !_decisionSmokeFailed && !_decisionSmokePending && !_decisionSmokeRunning
            && !_economySmokeFailed && !_economySmokePending && !_economySmokeRunning
            && (string.IsNullOrEmpty(_capturePath) || _captureComplete) && _elapsed >= _smokeSeconds)
        {
            GD.Print($"GUILD_SMOKE_OK elapsed={_elapsed:F2} screen={_view?.Screen}");
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        _controller?.Dispose();
        _controller = null;
    }

    private static Theme CreateTheme() => CreateCivicTheme();

    private void BuildShell() => BuildCivicShell();

    private void RenderScreen()
    {
        if (_view is null) return;
        var focus = GetViewport().GuiGetFocusOwner();
        var previousFocusName = focus is not null && _body.IsAncestorOf(focus) ? focus.Name.ToString() : null;
        // A capture reads the previous rendered frame. Give a rebuilt screen a
        // complete frame to reach the viewport before requesting its image.
        _captureDelayFrames = 2;
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        ResetCivicPreparation();
        _actorButtons.Clear();
        _skillBars.Clear();
        _actorDetails = null;
        _map = null;
        _renameInput = null;
        _renameButton = null;
        _startMatchButton = null;
        _startWaveButton = null;
        _retryButton = null;
        _returnButton = null;
        _buyLevelButton = null;
        _reviveButton = null;
        _levelChoiceButtons.Clear();
        _partyCount = null;
        _preparationCosts = null;
        _decisionRangeText = null;
        _screen = _view.Screen;
        _lastChoiceIds = string.Join('|', _view.Choices.Select(x => x.Id));
        if (!_view.Adventurers.Any(a => a.Id == _selectedActor))
            _selectedActor = _view.Adventurers.FirstOrDefault()?.Id;
        switch (_view.Screen)
        {
            case "Guild":
                if (_choosingExpedition) BuildExpedition(); else BuildGuild();
                break;
            case "Preparation":
            case "Battle": BuildBattle(); break;
            case "Defeat": BuildResult(false); break;
            case "Victory": BuildResult(true); break;
            case "RankEvent": BuildRankEvent(); break;
            case "LevelChoice": BuildLevelChoice(); break;
            default: AddText(_body, DisplayText.Get("ui.main.008") + _view.Screen); break;
        }
        RefreshValues();
        RestoreDecisionFocus(previousFocusName);
    }

    private void BuildGuild() => BuildFeedbackGuild();

    private void OpenExpedition()
    {
        if (_view?.Screen != "Guild") return;
        _choosingExpedition = true;
        _expeditionIds.RemoveWhere(id => !_view.Adventurers.Any(a => a.Id == id && !a.IsDispatched));
        if (_expeditionIds.Count == 0)
            foreach (var actor in _view.Adventurers.Where(a => !a.IsDispatched).Take(10)) _expeditionIds.Add(actor.Id);
        SelectStage(_selectedStageId);
        RenderScreen();
    }

    private void SelectStage(string? id)
    {
        if (_view is null) return;
        var stage = _view.Stages.FirstOrDefault(s => s.Id == id && s.Mode == _selectedMode)
            ?? _view.Stages.FirstOrDefault(s => s.Mode == _selectedMode);
        _selectedStageId = stage?.Id;
    }

    private void BuildExpedition() => BuildDecisionExpedition();

    private void RefreshExpeditionCount()
    {
        if (_partyCount is not null && _view is not null)
        {
            var total = _view.Adventurers.Where(a => _expeditionIds.Contains(a.Id)).Aggregate(System.Numerics.BigInteger.Zero,
                (sum, actor) => sum + System.Numerics.BigInteger.Parse(actor.DeploymentCost, CultureInfo.InvariantCulture));
            _partyCount.Text = _selectedMode == "invasion"
                ? DisplayText.Format("ui.main.009", _expeditionIds.Count, total)
                : DisplayText.Format("ui.main.010", _view.Adventurers.Count(actor => !actor.IsDispatched));
        }
        if (_startMatchButton is not null)
            _startMatchButton.Disabled = (_selectedMode == "invasion" && _expeditionIds.Count is < 1 or > 10) || _selectedStageId is null || !ValidEconomyContribution();
    }

    private void ConfirmExpedition()
    {
        if (_view is null || _selectedStageId is null) return;
        var ids = _view.Adventurers.Where(a => !a.IsDispatched && _expeditionIds.Contains(a.Id)).Select(a => a.Id).ToArray();
        Dispatch(new("StartMatch", TargetId: _selectedStageId, Value: string.Join(',', ids)) { Contribution = _economyContribution });
        if (_view.Screen != "Guild") { _choosingExpedition = false; _economyContribution = "0"; }
    }

    private void BuildActorList(Container parent)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(255, 0), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        foreach (var actor in _view!.Adventurers)
        {
            var actorId = actor.Id;
            var button = AddButton(list, ActorButtonText(actor), () => { _selectedActor = actorId; _selectedBlessing = null; RenderScreen(); });
            button.Name = "Inspect_" + actorId;
            button.ClipText = true;
            button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            button.TooltipText = ActorButtonText(actor);
            button.Alignment = HorizontalAlignment.Left;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Modulate = actor.Id == _selectedActor ? new Color("a5e4d2") : Colors.White;
            _actorButtons[actorId] = button;
        }
    }

    private void BuildActorDetails(Container parent) => BuildFeedbackActorDetails(parent);

    private void BuildItems(Container parent, bool transfer)
    {
        var actor = SelectedActor();
        AddText(parent, transfer ? DisplayText.Get("ui.main.011") : DisplayText.Get("ui.main.012"));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 90), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        var items = _view!.Items.Where(i => transfer ? IsGuildItem(i) : IsGuildItem(i) || i.OwnerId == actor?.Id).ToArray();
        if (items.Length == 0) AddText(list, DisplayText.Get("ui.main.013"), true);
        foreach (var item in items)
        {
            var itemId = item.Id;
            var row = Row(list);
            AddText(row, DisplayText.Format("ui.main.017", item.Name, (IsGuildItem(item) ? DisplayText.Get("ui.main.014") : item.Equipped ? DisplayText.Get("ui.main.015") : DisplayText.Get("ui.main.016"))), true).SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (transfer)
                AddButton(row, DisplayText.Get("ui.main.018"), () => Dispatch(new("TransferItem", ActorId: _view.EventActorId ?? _selectedActor, TargetId: itemId)));
            else if (item.OwnerId == actor?.Id && !item.Equipped)
                AddButton(row, DisplayText.Get("ui.main.019"), () => Dispatch(new("EquipItem", ActorId: _selectedActor, TargetId: itemId)));
            else if (IsGuildItem(item))
                AddText(row, DisplayText.Get("ui.main.020"));
        }
    }

    private void BuildHistory(Container parent)
    {
        var section = FeedbackSection(parent, DisplayText.Get("ui.tab.history"), "GrowthHistory");
        section.GetParent<Control>().SizeFlagsVertical = SizeFlags.ExpandFill;
        AddText(section, DisplayText.Get("ui.main.021"), true);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.Name = "GrowthHistoryScroll"; section.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        if (_view!.History.Count == 0) AddText(list, DisplayText.Get("ui.main.022"));
        var displayIndex = 0;
        foreach (var entry in _view.History)
        {
            var record = new PanelContainer(); record.AddThemeStyleboxOverride("panel", CivicSurface("f4f6f3", "d5dace", 10)); list.AddChild(record);
            AddText(record, DisplayText.Format("ui.history.entry", ++displayIndex, entry.Text), true);
        }
    }

    private void BuildBattle()
    {
        if (_view!.Screen == "Preparation") { BuildDecisionPreparation(); return; }
        var invasion = _view!.Mode == "invasion";
        var toolbar = Row(_body);
        if (_view.Screen == "Preparation")
        {
            _startWaveButton = AddButton(toolbar, invasion ? DisplayText.Format("ui.main.023", Math.Min(_view.Wave + 1, _view.TotalWaves)) : _view.Wave == 0 ? DisplayText.Get("ui.main.024") : DisplayText.Get("ui.main.025"), () => Dispatch(new("StartWave")));
            _buyLevelButton = AddButton(toolbar, DisplayText.Get("ui.main.026"), () => Dispatch(new("BuyLevel", ActorId: _selectedActor)));
            _reviveButton = AddButton(toolbar, DisplayText.Get("ui.main.027"), () => Dispatch(new("Revive", ActorId: _selectedActor)));
            AddButton(toolbar, _repositionMode ? DisplayText.Get("ui.main.028") : DisplayText.Get("ui.main.029"), () => { _repositionMode = !_repositionMode; RenderScreen(); });
        }
        else
            AddButton(toolbar, _view.Paused ? DisplayText.Get("ui.main.030") : DisplayText.Get("ui.main.031"), () => Dispatch(new("TogglePause")));
        AddButton(toolbar, DisplayText.Get("ui.main.032"), () => Dispatch(new("SetSpeed", Value: "1")));
        AddButton(toolbar, DisplayText.Get("ui.main.033"), () => Dispatch(new("SetSpeed", Value: "2")));
        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(split);
        var mapArea = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(mapArea);
        _map = new GuildMap { ReadView = () => _view, SelectedActor = () => _selectedActor, CellSelected = MapClicked, CustomMinimumSize = new Vector2(610, 370), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Font = Theme.DefaultFont };
        mapArea.AddChild(_map);
        var instruction = _view.Screen == "Preparation"
            ? invasion ? DisplayText.Get("ui.main.034") : DisplayText.Get("ui.main.035")
            : invasion ? DisplayText.Get("ui.main.036") : DisplayText.Get("ui.main.037");
        AddText(mapArea, instruction, true);
        if (invasion)
            AddText(mapArea, _view.Screen == "Preparation" ? DisplayText.Get("ui.main.038") : DisplayText.Get("ui.main.039"), true).Modulate = MutedColor;
        var panelFrame = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(panelFrame);
        var panelScroll = DecisionScroll(panelFrame); panelScroll.Name = "BattleDetailsScroll";
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; panelScroll.AddChild(panel);
        var actorPicker = new OptionButton { CustomMinimumSize = new Vector2(330, 38) };
        var actors = _view.Adventurers.Where(a => !a.IsDispatched && (!invasion || a.InParty)).ToArray();
        if (actors.Length > 0 && !actors.Any(a => a.Id == _selectedActor)) _selectedActor = actors[0].Id;
        for (var i = 0; i < actors.Length; i++)
        {
            actorPicker.AddItem(actors[i].Name);
            if (actors[i].Id == _selectedActor) actorPicker.Select(i);
        }
        actorPicker.ItemSelected += index => { _selectedActor = actors[(int)index].Id; RenderScreen(); };
        panel.AddChild(actorPicker);
        BuildActorDetails(panel);
        if (_view.Screen == "Preparation") _preparationCosts = AddText(panel, "", true);
        AddText(panel, DisplayText.Get("ui.main.040"));
        var target = new OptionButton();
        var targetKeys = invasion ? new[] { "nearest", "low-hp", "high-hp", "structures" } : new[] { "arrival", "low-hp", "high-hp", "nearest" };
        foreach (var label in invasion ? new[] { DisplayText.Get("ui.main.041"), DisplayText.Get("ui.main.042"), DisplayText.Get("ui.main.043"), DisplayText.Get("ui.main.044") } : new[] { DisplayText.Get("ui.main.045"), DisplayText.Get("ui.main.042"), DisplayText.Get("ui.main.043"), DisplayText.Get("ui.main.046") }) target.AddItem(label);
        var currentPreset = Array.IndexOf(targetKeys, SelectedActor()?.TargetPreset ?? "arrival");
        target.Select(currentPreset >= 0 ? currentPreset : 0);
        target.ItemSelected += index => Dispatch(new("SetTarget", ActorId: _selectedActor, Value: targetKeys[(int)index]));
        panel.AddChild(target);
        var rosterScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 190), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(rosterScroll);
        var roster = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rosterScroll.AddChild(roster);
        foreach (var actor in actors)
        {
            var actorId = actor.Id;
            var button = AddButton(roster, ActorButtonText(actor), () => { _selectedActor = actorId; RenderScreen(); });
            button.Alignment = HorizontalAlignment.Left;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _actorButtons[actorId] = button;
        }
    }

    private void BuildResult(bool victory) => BuildFeedbackResult(victory);

    private void BuildRankEvent() => BuildFeedbackRankEvent();

    private void BuildLevelChoice() => BuildFeedbackLevelChoice();

    private void BeginEvent(string kind) => BeginEventForDisplayedActor(kind);

    private void MapClicked(int x, int y)
    {
        if (_view?.Screen != "Preparation")
        {
            _message.Text = _view?.Mode == "invasion" ? DisplayText.Get("ui.main.047") : DisplayText.Get("ui.main.048");
            return;
        }
        var actor = SelectedActor();
        if (actor is null) return;
        if (actor.Deployed && !_repositionMode)
        {
            _message.Text = DisplayText.Get("ui.main.049");
            return;
        }
        Dispatch(new(actor.Deployed && _repositionMode ? "Reposition" : "Deploy", ActorId: actor.Id, X: x, Y: y));
    }

    private void Dispatch(GameCommand command)
    {
        if (_controller is null) return;
        try
        {
            var result = _controller.Execute(command);
            _message.Text = result.Message;
            _message.Modulate = result.Success ? new Color("286d60") : new Color("a13c30");
            _view = _controller.View;
            if (result.Success && command.Type == "ReturnToGuild")
            {
                _activeTab = "actors"; _choosingExpedition = false;
                if (!string.IsNullOrWhiteSpace(_view.Economy.LastReport)) _message.Text = DisplayText.Get("economy.ui.return_status");
            }
            RenderScreen();
        }
        catch (Exception ex)
        {
            _message.Text = DisplayText.Get("ui.main.050") + ex.Message;
            _message.Modulate = new Color("a13c30");
            GD.PushError(ex.ToString());
        }
    }

    private void RefreshValues()
    {
        if (_view is null) return;
        if (_view.Screen == "Guild")
            _summary.Text = DisplayText.Format("ui.main.051", _view.GuildResources, _view.Adventurers.Count, _view.OwnedCapacity);
        else if (_view.Mode == "invasion")
        {
            var section = _view.Screen is "Preparation" or "LevelChoice" ? Math.Min(_view.Wave + 1, _view.TotalWaves) : _view.Wave;
            var seconds = Math.Max(0, (_view.RemainingTimeMs + 999) / 1000);
            _summary.Text = DisplayText.Format("ui.main.053", section, _view.TotalWaves, seconds / 60, seconds % 60, _view.RemainingEnemies, _view.ObjectiveHp, _view.ObjectiveMaxHp, _view.MatchResources, _view.GuildResources, _view.Speed, (_view.Paused ? DisplayText.Get("ui.main.052") : ""));
        }
        else
            _summary.Text = DisplayText.Format("ui.main.054", _view.GuildResources, _view.MatchResources, _view.Wave, _view.TotalWaves, _view.DefenseHp, _view.Speed, (_view.Paused ? DisplayText.Get("ui.main.052") : ""));
        _status.Text = _view.Screen == "Guild" && !string.IsNullOrWhiteSpace(_view.Economy.LastReport)
            && _view.Status.Contains(_view.Economy.LastReport, StringComparison.Ordinal)
            ? DisplayText.Get("economy.ui.return_status") : _view.Status;
        foreach (var actor in _view.Adventurers)
            if (_actorButtons.TryGetValue(actor.Id, out var button) && IsInstanceValid(button)) button.Text = _view.Screen == "Preparation" ? "" : ActorButtonText(actor);
        var selected = SelectedActor();
        if (_actorDetails is not null && selected is not null)
            _actorDetails.Text = DisplayText.Format("ui.main.058", selected.Name, selected.Rank, selected.Level, selected.Hp, selected.MaxHp, selected.Weapon, (string.IsNullOrEmpty(selected.Activity) ? selected.Incapacitated ? DisplayText.Get("ui.main.055") : selected.Deployed ? DisplayText.Format("ui.main.056", selected.X, selected.Y) : DisplayText.Get("ui.main.057") : selected.Activity));
        if (_preparationCosts is not null && selected is not null)
            _preparationCosts.Text = PreparationDescription(selected);
        if (selected?.PreparationInfo is { } preparation && _view.Screen == "Preparation")
        {
            if (_buyLevelButton is not null) { _buyLevelButton.Disabled = !preparation.CanPurchaseLevel; _buyLevelButton.TooltipText = preparation.PurchaseUnavailableReason ?? ""; }
            if (_reviveButton is not null) { _reviveButton.Disabled = !preparation.CanRevive; _reviveButton.TooltipText = preparation.ReviveUnavailableReason ?? ""; }
        }
        if (selected is not null)
            foreach (var skill in selected.Skills)
                if (_skillBars.TryGetValue(skill.Slot, out var bar) && IsInstanceValid(bar)) bar.Value = skill.Progress;
        UpdateRangeDescription();
        RefreshCivicShell();
        RefreshCivicValues();
    }

    private AdventurerView? SelectedActor() => _view?.Adventurers.FirstOrDefault(a => a.Id == _selectedActor);
    private static bool IsGuildItem(ItemView item) => string.IsNullOrEmpty(item.OwnerId) || item.OwnerId is "guild" or "player";
    private string ActorButtonText(AdventurerView actor) => DisplayText.Format("ui.main.063", actor.Name, actor.Rank, actor.Level, actor.Hp, actor.MaxHp, (actor.IsDispatched ? EconomyDispatchActorStatus() : actor.Incapacitated ? DisplayText.Get("ui.main.055") : actor.Deployed ? DisplayText.Get("ui.main.059") : _view?.Screen == "Guild" ? DisplayText.Get("ui.main.060") : actor.InParty ? DisplayText.Get("ui.main.061") : DisplayText.Get("ui.main.062")));
    private static HBoxContainer Row(Container parent)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        return row;
    }
    private static Label AddText(Container parent, string text, bool wrap = false)
    {
        var label = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        parent.AddChild(label);
        return label;
    }
    private static Button AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.All };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }
    private static string? ReadArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : null;
    }
    private static double ReadDoubleArgument(string[] args, string name)
    {
        var value = ReadArgument(args, name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : args.Contains(name) ? 3 : 0;
    }
    private static string PrepareContent()
    {
        const string resource = "res://content/tech-content.json";
        using var file = Godot.FileAccess.Open(resource, Godot.FileAccess.ModeFlags.Read);
        if (file is null) throw new IOException(DisplayText.Get("ui.main.064") + resource);
        var directory = ProjectSettings.GlobalizePath("user://cache");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tech-content.json");
        System.IO.File.WriteAllText(path, file.GetAsText());
        return path;
    }
    private void CaptureScreen()
    {
        try
        {
            var path = Path.GetFullPath(_capturePath!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"GUILD_CAPTURE path={path} result={error}");
            if (error != Error.Ok) throw new IOException(DisplayText.Get("ui.main.065") + error);
            var viewport = GetViewport().GetVisibleRect().Size;
            var window = DisplayServer.WindowGetSize();
            var capture = GetViewport().GetTexture().GetImage();
            System.IO.File.WriteAllText(Path.ChangeExtension(path, ".capture.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                version = ProjectSettings.GetSetting("application/config/version").AsString(), screen = _view?.Screen,
                mode = _view?.Mode, scenario = _economySmokeScenario ?? _decisionSmokeScenario ?? _invasionSmokeScenario,
                viewportWidth = viewport.X, viewportHeight = viewport.Y,
                windowWidth = window.X, windowHeight = window.Y, pngWidth = capture.GetWidth(), pngHeight = capture.GetHeight(),
                assembly = typeof(GameController).Assembly.GetName().Version?.ToString(), capturedUtc = DateTime.UtcNow,
                selectedActor = SelectedActor()?.Name, growthRecipient = _view?.NextGrowthEvent?.ActorName,
                rangeSkill = _rangeSkillSlot, rangesVisible = _showPreparationRanges,
                legacyDefense = SelectedActor()?.RangeGuide?.IsLegacyDefense, encounter = _view?.NextEncounter,
                rangeLegend = _decisionRangeText is not null && IsInstanceValid(_decisionRangeText) ? _decisionRangeText.Text : null,
                attackRadius = SelectedActor()?.RangeGuide?.AttackRadius, selectedActorId = _selectedActor,
                civic = CreateCivicCaptureMetadata()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            _captureComplete = true;
        }
        catch (Exception ex)
        {
            _captureFailed = true;
            GD.PushError(DisplayText.Get("ui.main.066") + ex.Message);
            if (_smokeSeconds > 0) GetTree().Quit(1);
        }
    }

    private async void PrepareInvasionSmoke()
    {
        _invasionSmokePending = false;
        _invasionSmokeRunning = true;
        try
        {
            if (_controller is null || _view is null) throw new InvalidOperationException(DisplayText.Get("ui.main.067"));
            var scenario = _invasionSmokeScenario ?? "Battle";
            if (scenario is not ("Expedition" or "Preparation" or "Battle" or "LevelChoice" or "Victory" or "Defeat" or "DefeatRetry" or "ReturnGuild" or "Reopen"))
                throw new ArgumentException(DisplayText.Get("ui.main.068") + scenario);
            if (scenario == "Reopen")
            {
                if (_view.Mode != "invasion" || _view.Screen == "Guild")
                    throw new InvalidOperationException(DisplayText.Get("ui.main.069"));
                GD.Print($"GUILD_INVASION_REOPEN_OK screen={_view.Screen} section={_view.Wave} remainingMs={_view.RemainingTimeMs}");
                return;
            }
            if (_view.Screen != "Guild")
                throw new InvalidOperationException(DisplayText.Get("ui.main.070"));
            _selectedMode = "invasion";
            _selectedStageId = null;
            OpenExpedition();
            _expeditionIds.Clear();
            foreach (var actor in _view.Adventurers.Take(scenario is "Defeat" or "DefeatRetry" ? 1 : 6))
                _expeditionIds.Add(actor.Id);
            RenderScreen();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (scenario == "Expedition") return;
            PressSmokeButton(_startMatchButton, DisplayText.Get("ui.main.071"));
            if (_view.Mode != "invasion" || _view.Screen != "Preparation")
                throw new InvalidOperationException(DisplayText.Get("ui.main.072") + _view.Status);
            var ids = _view.Adventurers.Where(a => a.InParty).Select(a => a.Id).ToArray();
            var cells = _view.DeploymentCells.ToArray();
            if (cells.Length < ids.Length) throw new InvalidOperationException(DisplayText.Get("ui.main.073"));
            for (var i = 0; i < ids.Length; i++)
            {
                _selectedActor = ids[i];
                RenderScreen();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _map?.ClickCellForSmoke(cells[i].X, cells[i].Y);
                if (!_view.Adventurers.Single(a => a.Id == ids[i]).Deployed)
                    throw new InvalidOperationException(DisplayText.Get("ui.main.074") + _view.Status);
            }
            GD.Print($"GUILD_INVASION_UI_DEPLOY_OK actors={ids.Length} mode={_view.Mode}");
            if (scenario == "Preparation") return;
            if (scenario == "LevelChoice")
            {
                _selectedActor = ids[0];
                RenderScreen();
                PressSmokeButton(_buyLevelButton, DisplayText.Get("ui.main.075"));
                if (_view.Screen != "LevelChoice") throw new InvalidOperationException(DisplayText.Get("ui.main.076"));
                return;
            }
            PressSmokeButton(_startWaveButton, DisplayText.Get("ui.main.077"));
            if (_view.Screen != "Battle") throw new InvalidOperationException(DisplayText.Get("ui.main.078"));
            if (scenario == "Battle") return;
            for (var i = 0; i < 120 && _view.Screen is not ("Victory" or "Defeat"); i++)
            {
                if (_view.Screen == "Battle")
                {
                    _controller.Advance(30);
                    _view = _controller.View;
                    RenderScreen();
                }
                else if (_view.Screen == "LevelChoice")
                    PressSmokeButton(_levelChoiceButtons.FirstOrDefault(), DisplayText.Get("ui.main.079"));
                else if (_view.Screen == "Preparation")
                {
                    PrepareInvasionPartyForSmoke();
                    if (_view.Screen == "Preparation") PressSmokeButton(_startWaveButton, DisplayText.Get("ui.main.080"));
                }
                else throw new InvalidOperationException(DisplayText.Get("ui.main.081") + _view.Screen);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (scenario is "Defeat" or "DefeatRetry")
            {
                if (_view.Screen != "Defeat") throw new InvalidOperationException(DisplayText.Get("ui.main.082") + _view.Screen);
                GD.Print($"GUILD_INVASION_DEFEAT_OK section={_view.Wave} reason={_view.DefeatReason}");
                if (scenario == "DefeatRetry")
                {
                    PressSmokeButton(_retryButton, DisplayText.Get("ui.main.083"));
                    if (_view.Screen != "Preparation" || _view.Wave != 0)
                        throw new InvalidOperationException(DisplayText.Get("ui.main.084"));
                    GD.Print("GUILD_INVASION_RETRY_OK section=0");
                }
            }
            else
            {
                if (_view.Screen != "Victory") throw new InvalidOperationException(DisplayText.Get("ui.main.085") + _view.Screen + " / " + _view.Status);
                GD.Print($"GUILD_INVASION_VICTORY_OK sections={_view.Wave}");
                if (scenario == "ReturnGuild")
                {
                    PressSmokeButton(_returnButton, DisplayText.Get("ui.main.086"));
                    if (_view.Screen != "Guild") throw new InvalidOperationException(DisplayText.Get("ui.main.087"));
                    GD.Print($"GUILD_INVASION_RETURN_OK resources={_view.GuildResources} pendingEvents={_view.PendingEvents}");
                }
            }
        }
        catch (Exception exception)
        {
            _invasionSmokeFailed = true;
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            _invasionSmokeRunning = false;
            GD.Print($"GUILD_INVASION_SCENARIO_{(_invasionSmokeFailed ? "FAILED" : "READY")} scenario={_invasionSmokeScenario} screen={_view?.Screen} section={_view?.Wave}");
        }
    }

    private void PrepareInvasionPartyForSmoke()
    {
        if (_view is null) return;
        foreach (var id in _view.Adventurers.Where(a => a.InParty && a.Deployed).Select(a => a.Id).ToArray())
        {
            var actor = _view.Adventurers.Single(a => a.Id == id);
            _selectedActor = id;
            RenderScreen();
            if (actor.Incapacitated && CanAfford(actor.RevivalCost))
                PressSmokeButton(_reviveButton, DisplayText.Get("ui.main.088"));
            actor = _view.Adventurers.Single(a => a.Id == id);
            if (!actor.Incapacitated && actor.Level < 3 && CanAfford(actor.LevelPurchaseCost))
            {
                PressSmokeButton(_buyLevelButton, DisplayText.Get("ui.main.089"));
                if (_view.Screen == "LevelChoice") return;
            }
        }
    }

    private bool CanAfford(string cost)
        => _view is not null
        && System.Numerics.BigInteger.TryParse(cost, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
        && amount >= 0
        && System.Numerics.BigInteger.TryParse(_view.MatchResources, NumberStyles.Integer, CultureInfo.InvariantCulture, out var available)
        && amount <= available;

    private static void PressSmokeButton(Button? button, string operation)
    {
        if (button is null || !IsInstanceValid(button) || button.Disabled)
            throw new InvalidOperationException(DisplayText.Get("ui.main.090") + operation);
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void PrepareBattleSmoke()
    {
        if (_controller is null) return;
        if (_controller.View.Screen == "Guild")
        {
            var started = _controller.Execute(new("StartMatch"));
            if (!started.Success) throw new InvalidOperationException(started.Message);
        }
        if (_controller.View.Screen == "Preparation")
        {
            var actors = _controller.View.Adventurers.Take(6).ToArray();
            for (var i = 0; i < actors.Length; i++)
            {
                if (actors[i].Deployed) continue;
                var deployed = _controller.Execute(new("Deploy", ActorId: actors[i].Id, X: 2 + i * 2, Y: i % 2 == 0 ? 4 : 6));
                if (!deployed.Success) throw new InvalidOperationException(deployed.Message);
            }
            var wave = _controller.Execute(new("StartWave"));
            if (!wave.Success) throw new InvalidOperationException(wave.Message);
        }
        _view = _controller.View;
        GD.Print($"GUILD_BATTLE_SMOKE_READY screen={_view.Screen}");
    }

    private void PrepareSmokeScreen(string expectedScreen)
    {
        if (_controller is null) return;
        if (expectedScreen is not ("Battle" or "Preparation" or "LevelChoice" or "Victory" or "RankEvent" or "Guild"))
            throw new ArgumentException(DisplayText.Get("ui.main.091") + expectedScreen);
        for (var i = 0; i < 250 && _controller.View.Screen != expectedScreen; i++)
        {
            var view = _controller.View;
            GameCommand? command = view.Screen switch
            {
                "Preparation" => new("StartWave"),
                "LevelChoice" => new("ChooseLevel", ActorId: view.EventActorId, TargetId: view.Choices.First().Id),
                "Victory" => new("ReturnToGuild"),
                "Guild" when expectedScreen == "RankEvent" => new("BeginEvent", Value: "blessing"),
                _ => null
            };
            if (command is not null)
            {
                var result = _controller.Execute(command);
                if (!result.Success) throw new InvalidOperationException(result.Message);
            }
            else if (view.Screen == "Battle") _controller.Advance(120);
            else throw new InvalidOperationException(DisplayText.Format("ui.main.092", view.Screen));
        }
        _view = _controller.View;
        if (_view.Screen != expectedScreen) throw new InvalidOperationException(DisplayText.Get("ui.main.093") + expectedScreen);
        GD.Print($"GUILD_SCREEN_SCENARIO_READY screen={_view.Screen}");
    }

    private void RunRenameSmoke()
    {
        if (_renameInput is null || _renameButton is null || _renameSmokeName is null || _controller is null)
            throw new InvalidOperationException(DisplayText.Get("ui.main.094"));
        _renameInput.GrabFocus();
        _renameInput.SelectAll();
        GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = Key.Backspace });
        GetViewport().PushInput(new InputEventKey { Pressed = false, Keycode = Key.Backspace });
        foreach (var character in _renameSmokeName)
        {
            GetViewport().PushInput(new InputEventKey { Pressed = true, Unicode = character });
            GetViewport().PushInput(new InputEventKey { Pressed = false, Unicode = character });
        }
        if (_renameInput.Text != _renameSmokeName)
            throw new InvalidOperationException(DisplayText.Get("ui.main.095") + _renameInput.Text);
        _renameButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_controller.View.Adventurers.First(a => a.Id == _selectedActor).Name != _renameSmokeName)
            throw new InvalidOperationException(DisplayText.Get("ui.main.096"));
        _renameSmokeComplete = true;
        GD.Print($"GUILD_RENAME_UI_OK name={_renameSmokeName}");
    }
}
