using Godot;
using Guild.Application;
using System.Globalization;
using System.IO;

namespace Guild.Game;

/// <summary>Presentation adapter. All game changes pass through GameController commands.</summary>
public partial class Main : Control
{
    private static readonly Color TextColor = new("e4eaf5");
    private static readonly Color MutedColor = new("9baac2");
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
    private string _activeTab = "冒険者";
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
            if (args.Contains("--invasion-demo") || args.Contains("--invasion-scenario"))
            {
                if (_smokeSeconds <= 0 || ReadArgument(args, "--save-path") is null)
                    throw new InvalidOperationException("侵攻検証には--smoke-testと検証用--save-pathを指定してください。");
                _invasionSmokeScenario = ReadArgument(args, "--invasion-scenario") ?? "Battle";
                _invasionSmokePending = true;
            }
            if (args.Contains("--smoke-rename"))
            {
                if (_smokeSeconds <= 0 || ReadArgument(args, "--save-path") is null || _view.Screen != "Guild")
                    throw new InvalidOperationException("名前入力検証には--smoke-testとギルド画面の検証用--save-pathを指定してください。");
                _renameSmokeName = ReadArgument(args, "--smoke-rename") ?? "UI検証アオイ";
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
                    throw new InvalidOperationException("画面シナリオ検証には--smoke-testと検証用--save-pathを指定してください。");
                PrepareBattleSmoke();
                if (smokeScreen is not null) PrepareSmokeScreen(smokeScreen);
            }
            RenderScreen();
            if (_invasionSmokePending) Callable.From(PrepareInvasionSmoke).CallDeferred();
            GD.Print($"GUILD_SCENE_READY screen={_view.Screen} actors={_view.Adventurers.Count} save={savePath}");
        }
        catch (Exception ex)
        {
            GD.PushError(ex.ToString());
            _status.Text = "起動できませんでした";
            _message.Text = ex.Message;
            AddText(_body, "保存データと同梱データを確認してください。詳細は実行ログに記録しました。", true);
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
                if (!_invasionSmokeRunning && !_invasionSmokePending) _controller.Advance(delta);
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
                _message.Text = "処理を続けられません: " + ex.Message;
                GD.PushError(ex.ToString());
                SetProcess(false);
                if (_smokeSeconds > 0) GetTree().Quit(1);
            }
        }
        if (_captureDelayFrames > 0) _captureDelayFrames--;
        if (!_captured && _captureDelayFrames == 0 && !_invasionSmokePending && !_invasionSmokeRunning && !string.IsNullOrEmpty(_capturePath) && _elapsed > 1)
        {
            _captured = true;
            Callable.From(CaptureScreen).CallDeferred();
        }
        if (_smokeSeconds > 0 && !_captureFailed && !_invasionSmokeFailed && !_invasionSmokePending && !_invasionSmokeRunning
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

    private static Theme CreateTheme()
    {
        var font = new SystemFont
        {
            FontNames = new[] { "Yu Gothic UI", "Meiryo", "Noto Sans CJK JP", "sans-serif" },
            AllowSystemFallback = true
        };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 16 };
        theme.SetColor("font_color", "Label", TextColor);
        theme.SetColor("font_color", "Button", TextColor);
        theme.SetColor("font_color", "LineEdit", TextColor);
        theme.SetConstant("separation", "VBoxContainer", 9);
        theme.SetConstant("separation", "HBoxContainer", 9);
        var normal = new StyleBoxFlat { BgColor = new Color("172238"), BorderColor = new Color("334663"), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5, ContentMarginLeft = 13, ContentMarginRight = 13, ContentMarginTop = 9, ContentMarginBottom = 9 };
        normal.SetBorderWidthAll(1);
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color("263c5c");
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color("315372");
        theme.SetStylebox("normal", "Button", normal);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", pressed);
        return theme;
    }

    private void BuildShell()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 22);
        margin.AddThemeConstantOverride("margin_top", 15);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_bottom", 15);
        AddChild(margin);
        var shell = new VBoxContainer();
        margin.AddChild(shell);
        var header = new HBoxContainer();
        shell.AddChild(header);
        var title = AddText(header, "THE CONDUCTOR OF THE GUILD");
        title.AddThemeFontSizeOverride("font_size", 23);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var badge = AddText(header, "技術検証版 " + ProjectSettings.GetSetting("application/config/version").AsString());
        badge.Modulate = MutedColor;
        _summary = AddText(shell, "読み込み中…", true);
        _status = AddText(shell, "", true);
        _body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        shell.AddChild(_body);
        _message = AddText(shell, "操作の結果をここに表示します。", true);
        _message.Modulate = new Color("9cd6c8");
        _message.CustomMinimumSize = new Vector2(0, 32);
    }

    private void RenderScreen()
    {
        if (_view is null) return;
        // A capture reads the previous rendered frame. Give a rebuilt screen a
        // complete frame to reach the viewport before requesting its image.
        _captureDelayFrames = 2;
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
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
            default: AddText(_body, "準備しています。" + _view.Screen); break;
        }
        RefreshValues();
    }

    private void BuildGuild()
    {
        var commands = Row(_body);
        AddButton(commands, "出撃先と編成を選ぶ", OpenExpedition);
        AddButton(commands, "冒険者を募集 · 100", () => Dispatch(new("Recruit")));
        AddButton(commands, "所有枠を5拡張", () => Dispatch(new("ExpandRoster")));
        AddText(commands, $"所有 {_view!.Adventurers.Count}/{_view.OwnedCapacity}名  ／  未処理のRankイベント {_view.PendingEvents}回");
        if (_view.PendingEvents > 0)
        {
            var events = Row(_body);
            AddText(events, "次の成長イベント:");
            AddButton(events, "加護を取得", () => BeginEvent("blessing"));
            AddButton(events, "装備を譲渡", () => BeginEvent("transfer"));
            AddButton(events, "加護を合成", () => BeginEvent("fusion"));
            AddButton(events, "ランダムイベント", () => BeginEvent("random"));
        }
        var tabs = Row(_body);
        foreach (var tab in new[] { "冒険者", "装備", "成長履歴" })
        {
            var tabName = tab;
            var button = AddButton(tabs, tab, () => { _activeTab = tabName; RenderScreen(); });
            button.Modulate = _activeTab == tab ? new Color("a5e4d2") : Colors.White;
        }
        if (_activeTab == "成長履歴")
        {
            BuildHistory(_body);
            return;
        }
        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(split);
        BuildActorList(split);
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(details);
        BuildActorDetails(details);
        if (_activeTab == "装備")
        {
            AddButton(details, "武器を購入 · 1000", () => Dispatch(new("BuyWeapon")));
            BuildItems(details, false);
        }
        else
        {
            var rename = Row(details);
            var input = new LineEdit { Text = SelectedActor()?.Name ?? "", PlaceholderText = "冒険者の名前", MaxLength = 40, CustomMinimumSize = new Vector2(260, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _renameInput = input;
            rename.AddChild(input);
            Action renameAction = () => Dispatch(new("Rename", ActorId: _selectedActor, Value: input.Text));
            input.TextSubmitted += _ => renameAction();
            _renameButton = AddButton(rename, "名前を変更", renameAction);
            AddText(details, "装備と加護で役割が変わります。固定ジョブはありません。", true);
            BuildItems(details, false);
        }
    }

    private void OpenExpedition()
    {
        if (_view?.Screen != "Guild") return;
        _choosingExpedition = true;
        _expeditionIds.RemoveWhere(id => !_view.Adventurers.Any(a => a.Id == id));
        if (_expeditionIds.Count == 0)
            foreach (var actor in _view.Adventurers.Take(10)) _expeditionIds.Add(actor.Id);
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

    private void BuildExpedition()
    {
        if (_view is null) return;
        var heading = Row(_body);
        AddText(heading, "出撃先と遠征編成").AddThemeFontSizeOverride("font_size", 25);
        AddButton(heading, "拠点へ戻る", () => { _choosingExpedition = false; RenderScreen(); });
        var modes = Row(_body);
        foreach (var (key, label) in new[] { ("defense", "防衛"), ("invasion", "侵攻") })
        {
            var mode = key;
            var button = AddButton(modes, label, () => { _selectedMode = mode; SelectStage(null); RenderScreen(); });
            button.Modulate = _selectedMode == mode ? new Color("a5e4d2") : Colors.White;
            button.Disabled = !_view.Stages.Any(s => s.Mode == mode);
        }
        SelectStage(_selectedStageId);
        var stages = _view.Stages.Where(s => s.Mode == _selectedMode).ToArray();
        var stagePicker = new OptionButton { CustomMinimumSize = new Vector2(430, 40) };
        for (var i = 0; i < stages.Length; i++)
        {
            stagePicker.AddItem(stages[i].Name);
            if (stages[i].Id == _selectedStageId) stagePicker.Select(i);
        }
        stagePicker.ItemSelected += index => { SelectStage(stages[(int)index].Id); RenderScreen(); };
        _body.AddChild(stagePicker);
        var stage = stages.FirstOrDefault(s => s.Id == _selectedStageId);
        if (stage is not null)
        {
            var description = stage.Mode == "invasion"
                ? $"{stage.TotalSections}区画の守備隊と門を突破し、最奥の司令核を攻略します。各区画{stage.TimeLimitMs / 1000}秒。"
                : $"{stage.TotalSections}Waveの侵攻から防衛対象を守ります。";
            AddText(_body, description + $"  報酬用Rank {stage.RewardRank}  ／  {stage.RewardSummary}", true);
        }
        AddText(_body, "所有している冒険者から1〜10名を選びます。装備・加護・成長は両モードで共通です。", true);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 180) };
        _body.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        foreach (var actor in _view.Adventurers)
        {
            var actorId = actor.Id;
            var choice = new CheckBox { Text = $"{actor.Name}  ·  Rank {actor.Rank}  ·  HP {actor.Hp}/{actor.MaxHp}  ·  {actor.Weapon}", ButtonPressed = _expeditionIds.Contains(actorId) };
            choice.Toggled += selected =>
            {
                if (selected)
                {
                    if (_expeditionIds.Count >= 10)
                    {
                        choice.SetPressedNoSignal(false);
                        _message.Text = "遠征編成は10名までです。別の冒険者を外してから選んでください。";
                    }
                    else _expeditionIds.Add(actorId);
                }
                else _expeditionIds.Remove(actorId);
                RefreshExpeditionCount();
            };
            list.AddChild(choice);
        }
        var footer = Row(_body);
        _partyCount = AddText(footer, "");
        _partyCount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _startMatchButton = AddButton(footer, "この編成で出撃を確定", ConfirmExpedition);
        RefreshExpeditionCount();
        AddText(_body, _selectedMode == "invasion"
            ? "出撃後は構成を固定します。区画間に配置・復帰できます。戦闘中は控えの救援がなく、部隊全滅で敗北します。途中撤退はありません。"
            : "出撃後は構成を固定します。配置はWave間に行い、勝敗後の帰還で報酬を確定します。途中撤退はありません。", true).Modulate = MutedColor;
    }

    private void RefreshExpeditionCount()
    {
        if (_partyCount is not null) _partyCount.Text = $"遠征編成 {_expeditionIds.Count}/10名";
        if (_startMatchButton is not null)
            _startMatchButton.Disabled = _expeditionIds.Count is < 1 or > 10 || _selectedStageId is null;
    }

    private void ConfirmExpedition()
    {
        if (_view is null || _selectedStageId is null) return;
        var ids = _view.Adventurers.Where(a => _expeditionIds.Contains(a.Id)).Select(a => a.Id).ToArray();
        Dispatch(new("StartMatch", TargetId: _selectedStageId, Value: string.Join(',', ids)));
        if (_view.Screen != "Guild") _choosingExpedition = false;
    }

    private void BuildActorList(Container parent)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(255, 0), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        foreach (var actor in _view!.Adventurers)
        {
            var actorId = actor.Id;
            var button = AddButton(list, ActorButtonText(actor), () => { _selectedActor = actorId; _selectedBlessing = null; RenderScreen(); });
            button.Alignment = HorizontalAlignment.Left;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Modulate = actor.Id == _selectedActor ? new Color("a5e4d2") : Colors.White;
            _actorButtons[actorId] = button;
        }
    }

    private void BuildActorDetails(Container parent)
    {
        _actorDetails = AddText(parent, "", true);
        var actor = SelectedActor();
        if (actor is null) return;
        if (actor.Blessings.Count > 0)
            AddText(parent, "加護: " + string.Join(" ／ ", actor.Blessings), true);
        foreach (var skill in actor.Skills)
        {
            var row = Row(parent);
            var label = AddText(row, $"{skill.Slot} · {skill.Name}");
            label.CustomMinimumSize = new Vector2(210, 0);
            var progress = new ProgressBar { MinValue = 0, MaxValue = 1, Value = skill.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(120, 15), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(progress);
            _skillBars[skill.Slot] = progress;
        }
    }

    private void BuildItems(Container parent, bool transfer)
    {
        var actor = SelectedActor();
        AddText(parent, transfer ? "ギルドの装備から1個を譲渡" : "装備一覧（ギルド／選択した冒険者）");
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 90), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        var items = _view!.Items.Where(i => transfer ? IsGuildItem(i) : IsGuildItem(i) || i.OwnerId == actor?.Id).ToArray();
        if (items.Length == 0) AddText(list, "対象の装備はありません。", true);
        foreach (var item in items)
        {
            var itemId = item.Id;
            var row = Row(list);
            AddText(row, $"{item.Name}  ·  {(IsGuildItem(item) ? "ギルド所有" : item.Equipped ? "装備中" : "本人所有")}", true).SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (transfer)
                AddButton(row, "この装備を譲渡", () => Dispatch(new("TransferItem", ActorId: _view.EventActorId ?? _selectedActor, TargetId: itemId)));
            else if (item.OwnerId == actor?.Id && !item.Equipped)
                AddButton(row, "装備する", () => Dispatch(new("EquipItem", ActorId: _selectedActor, TargetId: itemId)));
            else if (IsGuildItem(item))
                AddText(row, "Rankイベントで譲渡");
        }
    }

    private void BuildHistory(Container parent)
    {
        AddText(parent, "成長・選択・不可逆な変更を実行順で記録します。", true);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        if (_view!.History.Count == 0) AddText(list, "まだ履歴はありません。");
        foreach (var entry in _view.History)
            AddText(list, $"{entry.Sequence:D4}  {entry.Text}", true);
    }

    private void BuildBattle()
    {
        var invasion = _view!.Mode == "invasion";
        var toolbar = Row(_body);
        if (_view.Screen == "Preparation")
        {
            _startWaveButton = AddButton(toolbar, invasion ? $"区画 {Math.Min(_view.Wave + 1, _view.TotalWaves)} の侵攻を開始" : _view.Wave == 0 ? "Wave 1を開始" : "次のWaveを開始", () => Dispatch(new("StartWave")));
            _buyLevelButton = AddButton(toolbar, "不足XPを一括購入", () => Dispatch(new("BuyLevel", ActorId: _selectedActor)));
            _reviveButton = AddButton(toolbar, "戦闘不能者を復帰", () => Dispatch(new("Revive", ActorId: _selectedActor)));
            AddButton(toolbar, _repositionMode ? "再配置モード: ON" : "再配置モード", () => { _repositionMode = !_repositionMode; RenderScreen(); });
        }
        else
            AddButton(toolbar, _view.Paused ? "戦闘を再開" : "一時停止", () => Dispatch(new("TogglePause")));
        AddButton(toolbar, "通常速度", () => Dispatch(new("SetSpeed", Value: "1")));
        AddButton(toolbar, "2倍速", () => Dispatch(new("SetSpeed", Value: "2")));
        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(split);
        var mapArea = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(mapArea);
        _map = new GuildMap { ReadView = () => _view, SelectedActor = () => _selectedActor, CellSelected = MapClicked, CustomMinimumSize = new Vector2(610, 370), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Font = Theme.DefaultFont };
        mapArea.AddChild(_map);
        var instruction = _view.Screen == "Preparation"
            ? invasion ? "冒険者を選び、青い出撃マスをクリック。右列が前方、左列が後方です。位置変更は再配置モードで行います。" : "冒険者を選び、青い出撃マスをクリック。配置済みの位置変更は再配置モードで行います。"
            : invasion ? "冒険者は自動で前進・交戦します。青: 冒険者 ／ 赤: 守備兵 ／ 橙: 砲台 ／ 紫: 門・司令核" : "橙色の経路を進む敵から防衛対象を守ります。青: 冒険者 ／ 赤: 敵 ／ 黄: 投射物";
        AddText(mapArea, instruction, true);
        if (invasion)
            AddText(mapArea, _view.Screen == "Preparation" ? "準備: 復帰 → Lv購入 → 強化選択 → 配置確認。戦闘中は控えが救援できず、全滅すると敗北します。" : "すべての守備兵・砲台と門を破壊すると突破します。戦闘中の追加出撃・復帰はできません。", true).Modulate = MutedColor;
        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(panel);
        var actorPicker = new OptionButton { CustomMinimumSize = new Vector2(330, 38) };
        var actors = _view.Adventurers.Where(a => !invasion || a.InParty).ToArray();
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
        AddText(panel, "標的の優先順位");
        var target = new OptionButton();
        var targetKeys = invasion ? new[] { "nearest", "low-hp", "high-hp", "structures" } : new[] { "arrival", "low-hp", "high-hp", "nearest" };
        foreach (var label in invasion ? new[] { "近い敵", "現在HPが低い", "現在HPが高い", "設備優先" } : new[] { "到達が近い", "現在HPが低い", "現在HPが高い", "自分に近い" }) target.AddItem(label);
        var currentPreset = Array.IndexOf(targetKeys, SelectedActor()?.TargetPreset ?? "arrival");
        target.Select(currentPreset >= 0 ? currentPreset : 0);
        target.ItemSelected += index => Dispatch(new("SetTarget", ActorId: _selectedActor, Value: targetKeys[(int)index]));
        panel.AddChild(target);
        var rosterScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
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

    private void BuildResult(bool victory)
    {
        var invasion = _view!.Mode == "invasion";
        AddText(_body, invasion ? victory ? "司令核の攻略に成功" : "侵攻失敗" : victory ? "防衛成功" : "防衛失敗").AddThemeFontSizeOverride("font_size", 34);
        if (invasion)
        {
            var cleared = victory ? _view.TotalWaves : Math.Max(0, _view.Wave - 1);
            AddText(_body, $"突破区画 {cleared}/{_view.TotalWaves}" + (victory ? "" : "  ／  " + _view.DefeatReason), true);
            AddText(_body, $"帰還時の拠点資源 +{(victory ? "300" : (100 * cleared).ToString(CultureInfo.InvariantCulture))}。Rank経験値は実出撃者へ配分し、残ったマッチ資源は持ち帰りません。", true);
        }
        AddText(_body, victory ? "ギルドに帰還すると報酬・成長・負傷状態をまとめて確定します。" : "敗北は仮の結果です。再挑戦でマッチ開始時へ戻るか、帰還して今回の結果を確定できます。", true);
        var row = Row(_body);
        if (!victory) _retryButton = AddButton(row, "同じ構成で再挑戦", () => Dispatch(new("Retry")));
        _returnButton = AddButton(row, "結果を確定してギルドへ", () => Dispatch(new("ReturnToGuild")));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.AddChild(scroll);
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(details);
        foreach (var actor in _view!.Adventurers)
            AddText(details, ActorButtonText(actor), true);
    }

    private void BuildRankEvent()
    {
        var actorId = _view!.EventActorId;
        var actor = _view.Adventurers.FirstOrDefault(a => a.Id == actorId);
        if (actor is not null) _selectedActor = actor.Id;
        AddText(_body, $"Rank成長イベント · {actor?.Name ?? "冒険者"}").AddThemeFontSizeOverride("font_size", 24);
        AddText(_body, "選択したイベントは完了まで継続します。アプリを閉じても同じ途中状態から再開します。", true);
        if (string.IsNullOrEmpty(_view.EventKind))
        {
            var row = Row(_body);
            AddButton(row, "加護を取得", () => BeginEvent("blessing"));
            AddButton(row, "装備を譲渡", () => BeginEvent("transfer"));
            AddButton(row, "加護を合成", () => BeginEvent("fusion"));
            AddButton(row, "ランダムイベント", () => BeginEvent("random"));
            return;
        }
        if (_view.EventKind == "transfer")
        {
            BuildItems(_body, true);
            return;
        }
        if (_view.EventKind == "random" && _view.Choices.Count == 0)
        {
            AddButton(_body, "ランダムイベントを実行", () => Dispatch(new("ResolveRandom", ActorId: actorId)));
            return;
        }
        if (_view.EventKind == "random")
        {
            AddText(_body, "ランダムイベントで加護が見つかりました。取得・進化、または取得辞退を選んでください。", true);
            AddButton(_body, "取得を辞退する", () => Dispatch(new("DeclineRandom", ActorId: actorId)));
        }
        if (_view.EventKind is "blessing" or "random")
        {
            if (_view.EventKind == "blessing")
            {
                var controls = Row(_body);
                AddButton(controls, "全候補を破棄", () => Dispatch(new("DiscardBlessings", ActorId: actorId)));
                AddButton(controls, "再選の札で再抽選", () => Dispatch(new("RerollBlessings", ActorId: actorId)));
            }
            if (actor is not null && actor.Blessings.Count >= 6)
            {
                AddText(_body, "満枠です。新しい加護を取得するときに入れ替える加護を選択してください。", true);
                _selectedBlessing = null;
                var replace = new OptionButton();
                replace.AddItem("入れ替えなし（既存加護の進化）");
                foreach (var blessing in actor.Blessings) replace.AddItem(blessing);
                replace.ItemSelected += index => _selectedBlessing = index == 0 ? null : actor.Blessings[(int)index - 1];
                _body.AddChild(replace);
            }
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.AddChild(scroll);
        var options = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(options);
        if (_view.Choices.Count == 0) AddText(options, "現在選択できる候補はありません。", true);
        foreach (var choice in _view.Choices)
        {
            var choiceId = choice.Id;
            var option = new VBoxContainer();
            options.AddChild(option);
            AddText(option, choice.Name);
            AddText(option, choice.Description, true).Modulate = MutedColor;
            AddButton(option, _view.EventKind == "fusion" ? "この素材で合成" : "この加護を取得", () => Dispatch(new(_view.EventKind == "fusion" ? "Fuse" : "ChooseBlessing", ActorId: actorId, TargetId: choiceId, Value: _selectedBlessing)));
        }
    }

    private void BuildLevelChoice()
    {
        var actor = _view!.Adventurers.FirstOrDefault(a => a.Id == _view.EventActorId) ?? SelectedActor();
        AddText(_body, $"Lvアップ · {actor?.Name ?? "冒険者"}").AddThemeFontSizeOverride("font_size", 25);
        AddText(_body, "今回のマッチで使う一時強化を1つ選んでください。", true);
        if (actor?.Incapacitated == true && _view.Mode == "invasion")
        {
            AddText(_body, "この冒険者は戦闘不能です。強化を選んでも復活しません。復帰すると未選択候補・一時強化・LvXPをリセットします。", true);
            _reviveButton = AddButton(_body, $"復帰して候補をリセット · {actor.RevivalCost}", () => Dispatch(new("Revive", ActorId: actor.Id)));
        }
        foreach (var choice in _view.Choices)
        {
            var id = choice.Id;
            AddText(_body, choice.Name);
            AddText(_body, choice.Description, true).Modulate = MutedColor;
            _levelChoiceButtons.Add(AddButton(_body, "この強化を選ぶ", () => Dispatch(new("ChooseLevel", ActorId: actor?.Id, TargetId: id))));
        }
    }

    private void BeginEvent(string kind) => Dispatch(new("BeginEvent", Value: kind));

    private void MapClicked(int x, int y)
    {
        if (_view?.Screen != "Preparation")
        {
            _message.Text = _view?.Mode == "invasion" ? "配置と位置変更は区画間の準備中に行えます。" : "配置と位置変更はWave間に行えます。";
            return;
        }
        var actor = SelectedActor();
        if (actor is null) return;
        Dispatch(new(actor.Deployed && _repositionMode ? "Reposition" : "Deploy", ActorId: actor.Id, X: x, Y: y));
    }

    private void Dispatch(GameCommand command)
    {
        if (_controller is null) return;
        try
        {
            var result = _controller.Execute(command);
            _message.Text = result.Message;
            _message.Modulate = result.Success ? new Color("9cd6c8") : new Color("f2b2a8");
            _view = _controller.View;
            RenderScreen();
        }
        catch (Exception ex)
        {
            _message.Text = "操作を完了できません: " + ex.Message;
            _message.Modulate = new Color("f2b2a8");
            GD.PushError(ex.ToString());
        }
    }

    private void RefreshValues()
    {
        if (_view is null) return;
        if (_view.Screen == "Guild")
            _summary.Text = $"ギルド資源 {_view.GuildResources}  ／  所有 {_view.Adventurers.Count}/{_view.OwnedCapacity}名";
        else if (_view.Mode == "invasion")
        {
            var section = _view.Screen is "Preparation" or "LevelChoice" ? Math.Min(_view.Wave + 1, _view.TotalWaves) : _view.Wave;
            var seconds = Math.Max(0, (_view.RemainingTimeMs + 999) / 1000);
            _summary.Text = $"侵攻 · 区画 {section}/{_view.TotalWaves}  ／  残り {seconds / 60}:{seconds % 60:00}  ／  残敵 {_view.RemainingEnemies}  ／  門・拠点HP {_view.ObjectiveHp}/{_view.ObjectiveMaxHp}\n資源 {_view.MatchResources}（ギルド {_view.GuildResources}）  ／  {_view.Speed}倍速{(_view.Paused ? "・一時停止" : "")}";
        }
        else
            _summary.Text = $"ギルド資源 {_view.GuildResources}  ／  マッチ資源 {_view.MatchResources}  ／  Wave {_view.Wave}/{_view.TotalWaves}  ／  防衛耐久 {_view.DefenseHp}  ／  {_view.Speed}倍速{(_view.Paused ? "・一時停止" : "")}";
        _status.Text = _view.Status;
        foreach (var actor in _view.Adventurers)
            if (_actorButtons.TryGetValue(actor.Id, out var button) && IsInstanceValid(button)) button.Text = ActorButtonText(actor);
        var selected = SelectedActor();
        if (_actorDetails is not null && selected is not null)
            _actorDetails.Text = $"{selected.Name}  ·  Rank {selected.Rank}  ·  Lv {selected.Level}\nHP {selected.Hp}/{selected.MaxHp}  ／  {selected.Weapon}\n{(string.IsNullOrEmpty(selected.Activity) ? selected.Incapacitated ? "戦闘不能" : selected.Deployed ? $"配置 ({selected.X}, {selected.Y})" : "待機中" : selected.Activity)}";
        if (_preparationCosts is not null && selected is not null)
            _preparationCosts.Text = selected.Incapacitated
                ? $"復帰費用 {selected.RevivalCost}  ／  復帰HPは最大HPの10%\n復帰するとLv以外の一時強化・LvXP・状態異常をリセットします。"
                : $"{(selected.Deployed ? "再配置は無料" : "初配置 " + selected.DeploymentCost)}  ／  次Lvの購入 {selected.LevelPurchaseCost}";
        if (selected is not null)
            foreach (var skill in selected.Skills)
                if (_skillBars.TryGetValue(skill.Slot, out var bar) && IsInstanceValid(bar)) bar.Value = skill.Progress;
    }

    private AdventurerView? SelectedActor() => _view?.Adventurers.FirstOrDefault(a => a.Id == _selectedActor);
    private static bool IsGuildItem(ItemView item) => string.IsNullOrEmpty(item.OwnerId) || item.OwnerId is "guild" or "player";
    private static string ActorButtonText(AdventurerView actor) => $"{actor.Name}  R{actor.Rank} / Lv{actor.Level}\nHP {actor.Hp}/{actor.MaxHp}  {(actor.Incapacitated ? "戦闘不能" : actor.Deployed ? "配置済み" : actor.Activity == "拠点待機" ? "待機" : actor.InParty ? "控え" : "遠征外")}";
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
        if (file is null) throw new IOException("同梱の技術検証データを読み込めません: " + resource);
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
            if (error != Error.Ok) throw new IOException("画面を保存できませんでした: " + error);
            _captureComplete = true;
        }
        catch (Exception ex)
        {
            _captureFailed = true;
            GD.PushError("画面保存に失敗: " + ex.Message);
            if (_smokeSeconds > 0) GetTree().Quit(1);
        }
    }

    private async void PrepareInvasionSmoke()
    {
        _invasionSmokePending = false;
        _invasionSmokeRunning = true;
        try
        {
            if (_controller is null || _view is null) throw new InvalidOperationException("侵攻検証を初期化できません。");
            var scenario = _invasionSmokeScenario ?? "Battle";
            if (scenario is not ("Expedition" or "Preparation" or "Battle" or "LevelChoice" or "Victory" or "Defeat" or "DefeatRetry" or "ReturnGuild" or "Reopen"))
                throw new ArgumentException("未対応の侵攻検証シナリオです: " + scenario);
            if (scenario == "Reopen")
            {
                if (_view.Mode != "invasion" || _view.Screen == "Guild")
                    throw new InvalidOperationException("保存した侵攻マッチが再開されていません。");
                GD.Print($"GUILD_INVASION_REOPEN_OK screen={_view.Screen} section={_view.Wave} remainingMs={_view.RemainingTimeMs}");
                return;
            }
            if (_view.Screen != "Guild")
                throw new InvalidOperationException("新しい侵攻シナリオには拠点画面の検証用セーブを指定してください。");
            _selectedMode = "invasion";
            _selectedStageId = null;
            OpenExpedition();
            _expeditionIds.Clear();
            foreach (var actor in _view.Adventurers.Take(scenario is "Defeat" or "DefeatRetry" ? 1 : 6))
                _expeditionIds.Add(actor.Id);
            RenderScreen();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (scenario == "Expedition") return;
            PressSmokeButton(_startMatchButton, "出撃確定");
            if (_view.Mode != "invasion" || _view.Screen != "Preparation")
                throw new InvalidOperationException("出撃確定ボタンから侵攻準備へ進めませんでした: " + _view.Status);
            var ids = _view.Adventurers.Where(a => a.InParty).Select(a => a.Id).ToArray();
            var cells = _view.DeploymentCells.ToArray();
            if (cells.Length < ids.Length) throw new InvalidOperationException("検証に必要な出撃マスがありません。");
            for (var i = 0; i < ids.Length; i++)
            {
                _selectedActor = ids[i];
                RenderScreen();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _map?.ClickCellForSmoke(cells[i].X, cells[i].Y);
                if (!_view.Adventurers.Single(a => a.Id == ids[i]).Deployed)
                    throw new InvalidOperationException("マップ操作で冒険者を配置できませんでした: " + _view.Status);
            }
            GD.Print($"GUILD_INVASION_UI_DEPLOY_OK actors={ids.Length} mode={_view.Mode}");
            if (scenario == "Preparation") return;
            if (scenario == "LevelChoice")
            {
                _selectedActor = ids[0];
                RenderScreen();
                PressSmokeButton(_buyLevelButton, "Lv購入");
                if (_view.Screen != "LevelChoice") throw new InvalidOperationException("Lv購入から選択画面へ進めませんでした。");
                return;
            }
            PressSmokeButton(_startWaveButton, "侵攻開始");
            if (_view.Screen != "Battle") throw new InvalidOperationException("侵攻開始ボタンから戦闘へ進めませんでした。");
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
                    PressSmokeButton(_levelChoiceButtons.FirstOrDefault(), "Lv強化");
                else if (_view.Screen == "Preparation")
                {
                    PrepareInvasionPartyForSmoke();
                    if (_view.Screen == "Preparation") PressSmokeButton(_startWaveButton, "次区画開始");
                }
                else throw new InvalidOperationException("侵攻シナリオの予期しない画面: " + _view.Screen);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (scenario is "Defeat" or "DefeatRetry")
            {
                if (_view.Screen != "Defeat") throw new InvalidOperationException("単独部隊の敗北シナリオへ到達しませんでした: " + _view.Screen);
                GD.Print($"GUILD_INVASION_DEFEAT_OK section={_view.Wave} reason={_view.DefeatReason}");
                if (scenario == "DefeatRetry")
                {
                    PressSmokeButton(_retryButton, "再挑戦");
                    if (_view.Screen != "Preparation" || _view.Wave != 0)
                        throw new InvalidOperationException("再挑戦で最初の区画準備へ戻りませんでした。");
                    GD.Print("GUILD_INVASION_RETRY_OK section=0");
                }
            }
            else
            {
                if (_view.Screen != "Victory") throw new InvalidOperationException("侵攻3区画の勝利へ到達しませんでした: " + _view.Screen + " / " + _view.Status);
                GD.Print($"GUILD_INVASION_VICTORY_OK sections={_view.Wave}");
                if (scenario == "ReturnGuild")
                {
                    PressSmokeButton(_returnButton, "帰還確定");
                    if (_view.Screen != "Guild") throw new InvalidOperationException("侵攻の結果精算後に拠点へ戻りませんでした。");
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
                PressSmokeButton(_reviveButton, "復帰");
            actor = _view.Adventurers.Single(a => a.Id == id);
            if (!actor.Incapacitated && actor.Level < 3 && CanAfford(actor.LevelPurchaseCost))
            {
                PressSmokeButton(_buyLevelButton, "区画間Lv購入");
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
            throw new InvalidOperationException("検証する操作ボタンがありません: " + operation);
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
        if (expectedScreen is not ("Battle" or "Preparation" or "LevelChoice" or "Victory" or "RankEvent"))
            throw new ArgumentException("未対応の検証画面です: " + expectedScreen);
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
            else throw new InvalidOperationException($"画面検証を続けられません: {view.Screen}");
        }
        _view = _controller.View;
        if (_view.Screen != expectedScreen) throw new InvalidOperationException("検証画面へ到達しませんでした: " + expectedScreen);
        GD.Print($"GUILD_SCREEN_SCENARIO_READY screen={_view.Screen}");
    }

    private void RunRenameSmoke()
    {
        if (_renameInput is null || _renameButton is null || _renameSmokeName is null || _controller is null)
            throw new InvalidOperationException("名前入力欄を検証できません。");
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
            throw new InvalidOperationException("日本語Unicode入力が名前欄へ反映されませんでした: " + _renameInput.Text);
        _renameButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_controller.View.Adventurers.First(a => a.Id == _selectedActor).Name != _renameSmokeName)
            throw new InvalidOperationException("名前変更ボタンからの保存に失敗しました。");
        _renameSmokeComplete = true;
        GD.Print($"GUILD_RENAME_UI_OK name={_renameSmokeName}");
    }
}

/// <summary>Read-only map renderer; clicks are converted to application commands by Main.</summary>
public partial class GuildMap : Control
{
    public Func<GameView?> ReadView { get; set; } = () => null;
    public Func<string?> SelectedActor { get; set; } = () => null;
    public Action<int, int>? CellSelected { get; set; }
    public Font? Font { get; set; }

    public override void _Draw()
    {
        var view = ReadView();
        if (view is null) return;
        var cell = Math.Min(Size.X / view.MapWidth, Size.Y / view.MapHeight);
        var origin = new Vector2((Size.X - cell * view.MapWidth) / 2, (Size.Y - cell * view.MapHeight) / 2);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0a1321"));
        for (var y = 0; y < view.MapHeight; y++)
            for (var x = 0; x < view.MapWidth; x++)
            {
                var rect = new Rect2(origin + new Vector2(x * cell, y * cell), new Vector2(cell, cell));
                var color = IsDeploymentCell(view, x, y) ? new Color("183b50") : view.Path.Any(p => p.X == x && p.Y == y) ? new Color("785739") : new Color("142438");
                DrawRect(rect.Grow(-1), color);
            }
        if (view.Mode != "invasion" && view.Path.Count > 0)
        {
            var goal = view.Path[^1];
            var defense = origin + new Vector2((goal.X + 0.5f) * cell, (goal.Y + 0.5f) * cell);
            DrawRect(new Rect2(defense - Vector2.One * cell * 0.32f, Vector2.One * cell * 0.64f), new Color("b993ed"));
        }
        foreach (var actor in view.Adventurers.Where(a => a.Deployed))
        {
            var point = origin + new Vector2((actor.X + 0.5f) * cell, (actor.Y + 0.5f) * cell);
            var entity = view.Entities.FirstOrDefault(e => e.Id == actor.Id);
            if (entity is not null) point = origin + new Vector2(((float)entity.X + 0.5f) * cell, ((float)entity.Y + 0.5f) * cell);
            var selected = actor.Id == SelectedActor();
            if (selected) DrawArc(point, cell * 0.40f, 0, Mathf.Tau, 28, new Color("d9fff1"), 2);
            DrawCircle(point, cell * 0.28f, actor.Incapacitated ? new Color("5d6573") : new Color("65c7d7"));
            if (Font is not null) DrawString(Font, point + new Vector2(-cell * 0.22f, -cell * 0.38f), actor.Name, HorizontalAlignment.Center, cell, 12, new Color("e8f1ff"));
        }
        foreach (var entity in view.Entities)
        {
            if (view.Adventurers.Any(a => a.Id == entity.Id)) continue;
            var point = origin + new Vector2(((float)entity.X + 0.5f) * cell, ((float)entity.Y + 0.5f) * cell);
            var projectile = entity.Kind.Contains("projectile", StringComparison.OrdinalIgnoreCase);
            if (entity.Kind is "gate" or "core")
            {
                DrawRect(new Rect2(point - Vector2.One * cell * 0.32f, Vector2.One * cell * 0.64f), entity.Kind == "core" ? new Color("d6a7ff") : new Color("a58bc4"));
                if (Font is not null) DrawString(Font, point + new Vector2(-cell * 0.38f, cell * 0.57f), entity.Kind == "core" ? "司令核" : "門", HorizontalAlignment.Center, cell, 12, Colors.White);
            }
            else if (entity.Kind == "turret")
                DrawRect(new Rect2(point - Vector2.One * cell * 0.25f, Vector2.One * cell * 0.50f), new Color("e6ad61"));
            else
                DrawCircle(point, cell * (projectile ? 0.08f : 0.24f), projectile ? new Color("f4dc7e") : new Color("eb8281"));
            if (!projectile)
            {
                DrawRect(new Rect2(point + new Vector2(-cell * 0.25f, -cell * 0.36f), new Vector2(cell * 0.5f, 3)), new Color("422932"));
                DrawRect(new Rect2(point + new Vector2(-cell * 0.25f, -cell * 0.36f), new Vector2(cell * 0.5f * (float)Math.Clamp(entity.HpRatio, 0, 1), 3)), new Color("f2b297"));
            }
        }
        if (Font is not null && view.Path.Count > 0)
        {
            var entrance = view.Path[0];
            DrawString(Font, origin + new Vector2(entrance.X * cell + 5, entrance.Y * cell + 16), "入口 →", HorizontalAlignment.Left, -1, 12, Colors.White);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        var view = ReadView();
        if (view is null) return;
        var cell = Math.Min(Size.X / view.MapWidth, Size.Y / view.MapHeight);
        var origin = new Vector2((Size.X - cell * view.MapWidth) / 2, (Size.Y - cell * view.MapHeight) / 2);
        var x = (int)Math.Floor((click.Position.X - origin.X) / cell);
        var y = (int)Math.Floor((click.Position.Y - origin.Y) / cell);
        if (IsDeploymentCell(view, x, y) && x >= 0 && y >= 0 && x < view.MapWidth && y < view.MapHeight) CellSelected?.Invoke(x, y);
        AcceptEvent();
    }

    public void ClickCellForSmoke(int x, int y)
    {
        var view = ReadView() ?? throw new InvalidOperationException("マップの状態がありません。");
        var cell = Math.Min(Size.X / view.MapWidth, Size.Y / view.MapHeight);
        var origin = new Vector2((Size.X - cell * view.MapWidth) / 2, (Size.Y - cell * view.MapHeight) / 2);
        _GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = origin + new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell) });
    }

    private static bool IsDeploymentCell(GameView view, int x, int y) => view.DeploymentCells.Any(p => p.X == x && p.Y == y);
}
