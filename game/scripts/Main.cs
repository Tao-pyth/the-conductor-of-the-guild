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
    private string? _renameSmokeName;
    private bool _renameSmokeComplete;
    private LineEdit? _renameInput;
    private Button? _renameButton;

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
                _controller.Advance(delta);
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
        if (!_captured && !string.IsNullOrEmpty(_capturePath) && _elapsed > 1)
        {
            _captured = true;
            Callable.From(CaptureScreen).CallDeferred();
        }
        if (_smokeSeconds > 0 && _elapsed >= _smokeSeconds)
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
        _summary = AddText(shell, "読み込み中…");
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
        _screen = _view.Screen;
        _lastChoiceIds = string.Join('|', _view.Choices.Select(x => x.Id));
        if (!_view.Adventurers.Any(a => a.Id == _selectedActor))
            _selectedActor = _view.Adventurers.FirstOrDefault()?.Id;
        switch (_view.Screen)
        {
            case "Guild": BuildGuild(); break;
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
        AddButton(commands, "ステージ1へ出撃", () => Dispatch(new("StartMatch")));
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
        var toolbar = Row(_body);
        if (_view!.Screen == "Preparation")
        {
            AddButton(toolbar, _view.Wave == 0 ? "Wave 1を開始" : "次のWaveを開始", () => Dispatch(new("StartWave")));
            AddButton(toolbar, "不足XPを一括購入", () => Dispatch(new("BuyLevel", ActorId: _selectedActor)));
            AddButton(toolbar, "戦闘不能者を復帰", () => Dispatch(new("Revive", ActorId: _selectedActor)));
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
        AddText(mapArea, _view.Screen == "Preparation" ? "冒険者を選び、青い配置マスをクリック。配置済みの位置変更は再配置モードで行います。" : "橙色の経路を進む敵から防衛対象を守ります。青: 冒険者 ／ 赤: 敵 ／ 黄: 投射物", true);
        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(panel);
        var actorPicker = new OptionButton { CustomMinimumSize = new Vector2(330, 38) };
        var actors = _view.Adventurers.ToArray();
        for (var i = 0; i < actors.Length; i++)
        {
            actorPicker.AddItem(actors[i].Name);
            if (actors[i].Id == _selectedActor) actorPicker.Select(i);
        }
        actorPicker.ItemSelected += index => { _selectedActor = actors[(int)index].Id; RenderScreen(); };
        panel.AddChild(actorPicker);
        BuildActorDetails(panel);
        AddText(panel, "標的の優先順位");
        var target = new OptionButton();
        var targetKeys = new[] { "arrival", "lowest_hp", "highest_hp", "nearest" };
        foreach (var label in new[] { "到達が近い", "現在HPが低い", "現在HPが高い", "自分に近い" }) target.AddItem(label);
        var currentPreset = Array.IndexOf(targetKeys, SelectedActor()?.TargetPreset ?? "arrival");
        target.Select(currentPreset >= 0 ? currentPreset : 0);
        target.ItemSelected += index => Dispatch(new("SetTarget", ActorId: _selectedActor, Value: targetKeys[(int)index]));
        panel.AddChild(target);
        var rosterScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(rosterScroll);
        var roster = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rosterScroll.AddChild(roster);
        foreach (var actor in _view.Adventurers)
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
        AddText(_body, victory ? "防衛成功" : "防衛失敗").AddThemeFontSizeOverride("font_size", 34);
        AddText(_body, victory ? "ギルドに帰還すると報酬・成長・負傷状態をまとめて確定します。" : "敗北は仮の結果です。再挑戦でマッチ開始時へ戻るか、帰還して今回の結果を確定できます。", true);
        var row = Row(_body);
        if (!victory) AddButton(row, "同じ構成で再挑戦", () => Dispatch(new("Retry")));
        AddButton(row, "結果を確定してギルドへ", () => Dispatch(new("ReturnToGuild")));
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
        foreach (var choice in _view.Choices)
        {
            var id = choice.Id;
            AddText(_body, choice.Name);
            AddText(_body, choice.Description, true).Modulate = MutedColor;
            AddButton(_body, "この強化を選ぶ", () => Dispatch(new("ChooseLevel", ActorId: actor?.Id, TargetId: id)));
        }
    }

    private void BeginEvent(string kind) => Dispatch(new("BeginEvent", Value: kind));

    private void MapClicked(int x, int y)
    {
        if (_view?.Screen != "Preparation")
        {
            _message.Text = "配置と位置変更はWave間に行えます。";
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
        _summary.Text = $"ギルド資源 {_view.GuildResources}  ／  マッチ資源 {_view.MatchResources}  ／  Wave {_view.Wave}/{_view.TotalWaves}  ／  防衛耐久 {_view.DefenseHp}  ／  {_view.Speed}倍速{(_view.Paused ? "・一時停止" : "")}";
        _status.Text = _view.Status;
        foreach (var actor in _view.Adventurers)
            if (_actorButtons.TryGetValue(actor.Id, out var button) && IsInstanceValid(button)) button.Text = ActorButtonText(actor);
        var selected = SelectedActor();
        if (_actorDetails is not null && selected is not null)
            _actorDetails.Text = $"{selected.Name}  ·  Rank {selected.Rank}  ·  Lv {selected.Level}\nHP {selected.Hp}/{selected.MaxHp}  ／  {selected.Weapon}\n{(selected.Incapacitated ? "戦闘不能" : selected.Deployed ? $"配置 ({selected.X}, {selected.Y})" : "待機中")}";
        if (selected is not null)
            foreach (var skill in selected.Skills)
                if (_skillBars.TryGetValue(skill.Slot, out var bar) && IsInstanceValid(bar)) bar.Value = skill.Progress;
    }

    private AdventurerView? SelectedActor() => _view?.Adventurers.FirstOrDefault(a => a.Id == _selectedActor);
    private static bool IsGuildItem(ItemView item) => string.IsNullOrEmpty(item.OwnerId) || item.OwnerId is "guild" or "player";
    private static string ActorButtonText(AdventurerView actor) => $"{actor.Name}  R{actor.Rank} / Lv{actor.Level}\nHP {actor.Hp}/{actor.MaxHp}  {(actor.Incapacitated ? "戦闘不能" : actor.Deployed ? "配置済み" : "待機")}";
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
        }
        catch (Exception ex) { GD.PushError("画面保存に失敗: " + ex.Message); }
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
                var color = y == 5 ? new Color("785739") : IsDeploymentCell(x, y) ? new Color("183b50") : new Color("142438");
                DrawRect(rect.Grow(-1), color);
            }
        var defense = origin + new Vector2((view.MapWidth - 0.5f) * cell, 5.5f * cell);
        DrawRect(new Rect2(defense - Vector2.One * cell * 0.32f, Vector2.One * cell * 0.64f), new Color("b993ed"));
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
            DrawCircle(point, cell * (projectile ? 0.08f : 0.24f), projectile ? new Color("f4dc7e") : new Color("eb8281"));
            if (!projectile)
            {
                DrawRect(new Rect2(point + new Vector2(-cell * 0.25f, -cell * 0.36f), new Vector2(cell * 0.5f, 3)), new Color("422932"));
                DrawRect(new Rect2(point + new Vector2(-cell * 0.25f, -cell * 0.36f), new Vector2(cell * 0.5f * (float)Math.Clamp(entity.HpRatio, 0, 1), 3)), new Color("f2b297"));
            }
        }
        if (Font is not null) DrawString(Font, origin + new Vector2(8, cell * 5 + 16), "入口 →", HorizontalAlignment.Left, -1, 12, Colors.White);
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
        if (IsDeploymentCell(x, y) && x < view.MapWidth && y < view.MapHeight) CellSelected?.Invoke(x, y);
        AcceptEvent();
    }
    private static bool IsDeploymentCell(int x, int y) => x >= 0 && x < 16 && y is 3 or 4 or 6 or 7;
}
