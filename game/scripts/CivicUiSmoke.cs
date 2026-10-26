using Godot;
using Guild.Application;
using Guild.Domain.Serialization;
using Guild.Persistence;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guild.Game;

// Opt-in integration checks. ConfigureDecisionSmoke requires a bounded smoke
// process and an explicit isolated database before any of these can execute.
public partial class Main
{
    private bool _civicDisplayVerified;
    private bool _civicMaximizedRestoreVerified;
    private bool _civicDetailsVerified;
    private bool _civicGeometryVerified;
    private bool _civicStampVerified;
    private string? _civicFixtureDescription;

    private static bool IsCivicSmokeScenario(string? scenario) => scenario is
        "CivicDefense" or "CivicInvasion" or "CivicDetails" or "CivicDisplay"
        or "CivicFullscreen" or "CivicStampDefense" or "CivicStampInvasion"
        or "CivicLongName" or "CivicMany" or "CivicActionsDefense" or "CivicActionsInvasion";

    private async Task RunCivicUiSmoke(string scenario)
    {
        if (_controller is null || _view is null) throw new InvalidOperationException("公的ギルドUI検証を初期化できません。");
        if (scenario == "CivicMany") BuildCivicManyFixture();
        if (scenario == "CivicLongName")
        {
            var actor = _view!.Adventurers.FirstOrDefault(a => a.Name == "ユイ") ?? _view.Adventurers.Last();
            CivicCommand(new("Rename", ActorId: actor.Id, Value: "アルフォンス・フォン・シュタインベルク"));
        }

        var invasion = scenario is "CivicInvasion" or "CivicStampInvasion" or "CivicActionsInvasion";
        _selectedMode = invasion ? "invasion" : "defense";
        _selectedStageId = null;
        OpenExpedition();
        await CivicFrames();
        PressSmokeButton(_startMatchButton, "公的ギルド準備への出撃");
        await CivicFrames();
        if (_view!.Screen != "Preparation" || _view.Mode != (invasion ? "invasion" : "defense"))
            throw new InvalidOperationException("指定モードの準備画面へ移れません。");

        _selectedActor = scenario == "CivicMany" ? _view.Adventurers.Last().Id
            : _view.Adventurers.FirstOrDefault(a => a.InParty && a.RangeGuide?.SkillRadii.Any(s => s.IsHealing) == true)?.Id
                ?? _view.Adventurers.First(a => !invasion || a.InParty).Id;
        RenderScreen();
        await CivicFrames();
        if (_startWaveButton?.Disabled != true)
            throw new InvalidOperationException("配置0名の朱印開始は無効である必要があります。");

        var selectedId = _selectedActor;
        var beforeSelection = CivicStateFingerprint();
        var inspect = CivicControl<Button>("Inspect_" + selectedId);
        inspect.GrabFocus();
        PressSmokeButton(inspect, "選択中冒険者の再閲覧");
        await CivicFrames();
        AssertCivicState(beforeSelection, "名簿の閲覧");
        if (GetViewport().GuiGetFocusOwner()?.Name.ToString() != "Inspect_" + selectedId)
            throw new InvalidOperationException("名簿の再描画後にフォーカスが失われました。");

        var first = _view.DeploymentCells.Any(c => c.X == 8 && c.Y == 4)
            ? _view.DeploymentCells.First(c => c.X == 8 && c.Y == 4) : _view.DeploymentCells.First();
        var pure = CivicStateFingerprint();
        _map?.HoverCellForSmoke(-1, 4);
        _map?.ClickCellForSmoke(-1, 4);
        if (!invasion)
        {
            _map?.HoverCellForSmoke(8, 5);
            _map?.ClickCellForSmoke(8, 5);
        }
        _map?.HoverCellForSmoke(first.X, first.Y);
        await CivicFrames();
        AssertCivicState(pure, "無効マスと仮配置の表示");
        _map?.ClickCellForSmoke(first.X, first.Y);
        await CivicFrames();
        if (SelectedActor()?.Deployed != true) throw new InvalidOperationException("マップから初配置できませんでした。");

        AssertCivicGeometry();
        AssertCivicLayout();
        if (scenario == "CivicMany")
        {
            var roster = CivicControl<Control>("CivicRoster");
            var count = CountCivicInspectButtons(roster);
            if (_view.Adventurers.Count != 50 || count != 50)
                throw new InvalidOperationException($"50名の候補を保持できません: view={_view.Adventurers.Count}, controls={count}");
            CivicControl<Button>("Inspect_" + _view.Adventurers.Last().Id).GrabFocus();
            await CivicFrames();
            if (GetViewport().GuiGetFocusOwner()?.Name.ToString() != "Inspect_" + _view.Adventurers.Last().Id)
                throw new InvalidOperationException("50人目へフォーカスで到達できません。");
            GD.Print("GUILD_CIVIC_MANY_OK count=50 lastFocusable=true");
        }

        await VerifyCivicDetails();
        if (scenario is "CivicDisplay" or "CivicFullscreen")
            await VerifyCivicDisplay(scenario == "CivicFullscreen");
        if (scenario is "CivicActionsDefense" or "CivicActionsInvasion")
            await VerifyCivicActions();
        if (scenario is "CivicStampDefense" or "CivicStampInvasion")
        {
            var revision = CivicStoredState().revision;
            PressSmokeButton(_startWaveButton, "出撃承認の押印");
            if (_view.Screen != "Battle" || CivicStoredState().revision != revision + 1)
                throw new InvalidOperationException("朱印の一操作で戦闘を一度だけ開始できませんでした。");
            _civicStampVerified = true;
            GD.Print($"GUILD_CIVIC_STAMP_OK mode={_view.Mode} screen={_view.Screen} commits=1");
        }
        else AssertCivicLayout();

        GD.Print($"GUILD_CIVIC_LAYOUT_OK scenario={scenario} mode={_view.Mode} actors={_view.Adventurers.Count} screen={_view.Screen}");
    }

    private void BuildCivicManyFixture()
    {
        var savePath = _controller!.SavePath;
        _controller.Dispose();
        _controller = null;
        // The only synthetic value is funding in this dedicated test database.
        // Expansion, recruitment and generated ownership all use real commands.
        using (var store = new SaveStore(savePath))
        {
            var profile = JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
            profile.Resources = 100000;
            store.Commit(new(store.Revision, "civic-funding-" + Guid.NewGuid().ToString("N"), "civic-fixture",
                "隔離試験DBの50名表示検証用資源。難易度・経済評価には使用しない。",
                new Dictionary<string, string> { ["profile"] = JsonSerializer.Serialize(profile, DomainJson.Options) }, []));
        }
        _controller = new GameController(savePath, PrepareContent());
        while (_controller.View.OwnedCapacity < 50) CivicCommand(new("ExpandRoster"));
        while (_controller.View.Adventurers.Count < 50) CivicCommand(new("Recruit"));
        _view = _controller.View;
        _civicFixtureDescription = "Only isolated profile resources=100000 were seeded; expanded and recruited 50 via real commands. Not a balance test.";
        GD.Print("GUILD_CIVIC_FIXTURE_SYNTHETIC funding=100000 commands=ExpandRoster,Recruit boundary=50");
    }

    private void CivicCommand(GameCommand command)
    {
        var result = _controller!.Execute(command);
        if (!result.Success) throw new InvalidOperationException("UI試験コマンド失敗: " + command.Type + ": " + result.Message);
        _view = _controller.View;
    }

    private async Task VerifyCivicDetails()
    {
        var before = CivicStateFingerprint();
        var details = CivicControl<ScrollContainer>("CivicDetailsScroll");
        if (!details.IsVisibleInTree())
        {
            PressSmokeButton(CivicControl<Button>("CivicDetailsToggle"), "能力・技能詳細を開く");
            await CivicFrames();
            details = CivicControl<ScrollContainer>("CivicDetailsScroll");
        }
        if (!details.IsVisibleInTree()) throw new InvalidOperationException("詳細を開けません。");
        details.GrabFocus();
        await CivicFrames();
        if (GetViewport().GuiGetFocusOwner() != details)
            throw new InvalidOperationException("詳細スクロールへフォーカスできません。");
        var bar = details.GetVScrollBar();
        details.ScrollVertical = (int)bar.MaxValue;
        await CivicFrames();
        if (bar.MaxValue > bar.Page + 1 && details.ScrollVertical <= 0)
            throw new InvalidOperationException("技能の全文末尾へスクロールできません。");
        AssertCivicState(before, "能力詳細の開閉・閲覧");
        PressSmokeButton(CivicControl<Button>("CivicDetailsToggle"), "能力・技能詳細を閉じる");
        await CivicFrames();
        if (CivicControl<ScrollContainer>("CivicDetailsScroll").IsVisibleInTree())
            throw new InvalidOperationException("詳細を閉じられません。");
        AssertCivicState(before, "能力詳細を閉じる");
        _civicDetailsVerified = true;
        GD.Print("GUILD_CIVIC_DETAILS_OK focus=true stateUnchanged=true");
    }

    private async Task VerifyCivicDisplay(bool leaveFullscreen)
    {
        if (IsCivicFullscreen()) { ExitFullscreen(); await CivicFrames(8); }
        var size = DisplayServer.WindowGetSize();
        var position = DisplayServer.WindowGetPosition();
        var before = CivicStateFingerprint();
        PressSmokeButton(CivicControl<Button>("DisplayModeToggle"), "表示ボタンで全画面");
        await CivicFrames(8);
        if (!IsCivicFullscreen()) throw new InvalidOperationException("表示ボタンで全画面へ移れません。");
        AssertCivicState(before, "表示ボタンで全画面");
        AssertCivicLayout();
        CaptureCivicEvidence("fullscreen");
        PushCivicKey(Key.Escape);
        await CivicFrames(8);
        AssertCivicWindowRestored(size, position, "Esc");
        AssertCivicState(before, "Escでウィンドウへ復帰");
        PushCivicKey(Key.F11);
        await CivicFrames(8);
        if (!IsCivicFullscreen()) throw new InvalidOperationException("F11で全画面へ移れません。");
        AssertCivicState(before, "F11で全画面");
        PushCivicKey(Key.F11);
        await CivicFrames(8);
        AssertCivicWindowRestored(size, position, "F11");
        AssertCivicState(before, "F11でウィンドウへ復帰");

        var window = GetWindow();
        window.Mode = Window.ModeEnum.Maximized;
        await CivicFrames(16);
        if (window.Mode != Window.ModeEnum.Maximized || DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Maximized)
            throw new InvalidOperationException("最大化されたゲームウィンドウを検証できません。");
        var maximizedSize = DisplayServer.WindowGetSize();
        var maximizedPosition = DisplayServer.WindowGetPosition();
        AssertCivicState(before, "ゲームウィンドウの最大化");
        PressSmokeButton(CivicControl<Button>("DisplayModeToggle"), "最大化ウィンドウから全画面");
        await CivicFrames(8);
        if (!IsCivicFullscreen()) throw new InvalidOperationException("最大化ウィンドウから全画面へ移れません。");
        AssertCivicState(before, "最大化ウィンドウから全画面");
        PushCivicKey(Key.Escape);
        await CivicFrames(16);
        if (window.Mode != Window.ModeEnum.Maximized || DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Maximized
            || DisplayServer.WindowGetSize() != maximizedSize || (DisplayServer.WindowGetPosition() - maximizedPosition).Length() > 3)
            throw new InvalidOperationException("Escで元の最大化モード・サイズ・位置へ戻りません。");
        AssertCivicState(before, "Escで最大化ウィンドウへ復帰");
        AssertCivicLayout();
        CaptureCivicEvidence("maximized-restored");
        window.Mode = Window.ModeEnum.Windowed;
        await CivicFrames(16);
        AssertCivicWindowRestored(size, position, "最大化から通常ウィンドウへ復帰");
        AssertCivicState(before, "最大化検証後の通常ウィンドウ復帰");
        _civicMaximizedRestoreVerified = true;
        _civicDisplayVerified = true;
        GD.Print($"GUILD_CIVIC_DISPLAY_OK button=true F11=true Escape=true maximizedRestore=true restored={size.X}x{size.Y} stateAndSavedRngUnchanged=true");
        if (leaveFullscreen)
        {
            PressSmokeButton(CivicControl<Button>("DisplayModeToggle"), "撮影用の全画面表示");
            await CivicFrames(8);
            if (!IsCivicFullscreen()) throw new InvalidOperationException("全画面撮影の準備に失敗しました。");
            AssertCivicState(before, "全画面撮影準備");
        }
    }

    private async Task VerifyCivicActions()
    {
        var resources = _view!.MatchResources;
        var id = _selectedActor!;
        var original = SelectedActor()!;
        var nextCell = _view.DeploymentCells.First(c => c.X != original.X || c.Y != original.Y);
        PressSmokeButton(CivicControl<Button>("DecisionReposition"), "無料再配置モード");
        await CivicFrames();
        _map!.ClickCellForSmoke(nextCell.X, nextCell.Y);
        await CivicFrames();
        if (SelectedActor()?.X != nextCell.X || SelectedActor()?.Y != nextCell.Y || _view.MatchResources != resources)
            throw new InvalidOperationException("公的ギルドUIの無料再配置が不整合です。");
        var target = CivicControl<OptionButton>("CivicTargetPriority");
        var targetIndex = (target.Selected + 1) % target.ItemCount;
        target.Select(targetIndex);
        target.EmitSignal(OptionButton.SignalName.ItemSelected, targetIndex);
        await CivicFrames();
        if (SelectedActor()!.TargetPreset == original.TargetPreset)
            throw new InvalidOperationException("標的優先の操作がコマンドへ届きません。");
        var beforeRange = CivicStateFingerprint();
        var ranges = CivicControl<CheckBox>("PreparationRangeToggle");
        ranges.ButtonPressed = false;
        ranges.ButtonPressed = true;
        var skill = CivicControl<OptionButton>("PreparationSkillRange");
        if (skill.ItemCount > 1) { skill.Select(1); skill.EmitSignal(OptionButton.SignalName.ItemSelected, 1); }
        await CivicFrames();
        AssertCivicState(beforeRange, "範囲と技能範囲の切替");
        if (_reviveButton?.Disabled != true) throw new InvalidOperationException("健康な冒険者の復帰操作が無効ではありません。");
        PressSmokeButton(_buyLevelButton, "公的ギルドUIでXP購入");
        if (_view.Screen != "LevelChoice" || _view.EventActorId != id)
            throw new InvalidOperationException("XP購入が選択者のLv選択へ届きません。");
        PressSmokeButton(_levelChoiceButtons.FirstOrDefault(), "購入したLvの強化");
        await CivicFrames();
        if (_view.Screen != "Preparation" || SelectedActor()?.Level != original.Level + 1)
            throw new InvalidOperationException("購入・強化後に準備へ戻れません。");
        GD.Print($"GUILD_CIVIC_ACTIONS_OK mode={_view.Mode} reposition=true priority=true rangesPure=true xp=true");
    }

    private static bool IsCivicFullscreen() => DisplayServer.WindowGetMode() is
        DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;

    private static void AssertCivicWindowRestored(Vector2I size, Vector2I position, string operation)
    {
        var actualSize = DisplayServer.WindowGetSize();
        var actualPosition = DisplayServer.WindowGetPosition();
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed || actualSize != size || (actualPosition - position).Length() > 3)
            throw new InvalidOperationException($"{operation}で元のウィンドウへ戻りません: size={actualSize}, position={actualPosition}");
    }

    private void PushCivicKey(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = key, PhysicalKeycode = key });
        GetViewport().PushInput(new InputEventKey { Pressed = false, Keycode = key, PhysicalKeycode = key });
    }

    private async Task CivicFrames(int count = 3)
    {
        for (var index = 0; index < count; index++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private T CivicControl<T>(string name) where T : Control
        => FindChild(name, true, false) as T ?? throw new InvalidOperationException("UI検証対象がありません: " + name);

    private void AssertCivicLayout()
    {
        var viewport = GetViewport().GetVisibleRect();
        if (Math.Abs(viewport.Size.X - 1920) > 1 || Math.Abs(viewport.Size.Y - 1080) > 1)
            throw new InvalidOperationException("論理表示が1920×1080ではありません: " + viewport.Size);
        foreach (var name in new[] { "CivicRoster", "CivicMap", "CivicDispatch", "CivicCosts", "StampStartWave", "CivicDetailsToggle", "PreparationRangeToggle", "PreparationSkillRange", "CivicTargetPriority" })
        {
            var control = CivicControl<Control>(name);
            var rect = control.GetGlobalRect();
            if (!control.IsVisibleInTree() || rect.Size.X < 1 || rect.Size.Y < 1 || !viewport.Grow(2).Encloses(rect))
                throw new InvalidOperationException($"主要UIが画面内にありません: {name} {rect}");
        }
        if (_startWaveButton is null || _startWaveButton.Name.ToString() != "StampStartWave" || _startWaveButton.Disabled)
            throw new InvalidOperationException("配置後の朱印操作へ到達できません。");
        if (_buyLevelButton is null || _reviveButton is null)
            throw new InvalidOperationException("準備の購入・復帰操作がありません。");
        if (!_message.IsVisibleInTree() || !viewport.Grow(2).Encloses(_message.GetGlobalRect()))
            throw new InvalidOperationException("Preparation footer is clipped outside the viewport: " + _message.GetGlobalRect());
    }

    private void AssertCivicGeometry()
    {
        if (_map is null || _view is null) throw new InvalidOperationException("地図がありません。");
        var cell = _map.BoardCellSizeForSmoke;
        var board = _map.GetBoardRectForSmoke();
        var center = _map.CellCenterForSmoke(1, 1);
        var dx = _map.CellCenterForSmoke(2, 1) - center;
        var dy = _map.CellCenterForSmoke(1, 2) - center;
        if (cell <= 0 || Math.Abs(board.Size.X / _view.MapWidth - cell) > .1
            || Math.Abs(board.Size.Y / _view.MapHeight - cell) > .1
            || Math.Abs(dx.X - cell) > .1 || Math.Abs(dx.Y) > .1
            || Math.Abs(dy.Y - cell) > .1 || Math.Abs(dy.X) > .1)
            throw new InvalidOperationException("描画と入力の正方形マスが一致しません。");
        _civicGeometryVerified = true;
    }

    private static int CountCivicInspectButtons(Node node)
        => (node is Button && node.Name.ToString().StartsWith("Inspect_", StringComparison.Ordinal) ? 1 : 0)
            + node.GetChildren().Sum(CountCivicInspectButtons);

    private (long revision, string documents) CivicStoredState()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _controller!.SavePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var revisionQuery = connection.CreateCommand();
        revisionQuery.CommandText = "SELECT value FROM metadata WHERE key='revision';";
        var revision = long.Parse((string)revisionQuery.ExecuteScalar()!, System.Globalization.CultureInfo.InvariantCulture);
        using var documentsQuery = connection.CreateCommand();
        documentsQuery.CommandText = "SELECT key,value FROM documents ORDER BY key;";
        using var reader = documentsQuery.ExecuteReader();
        var records = new List<string>();
        while (reader.Read()) records.Add(reader.GetString(0) + "=" + reader.GetString(1));
        return (revision, string.Join('\n', records));
    }

    private string CivicStateFingerprint()
    {
        var stored = CivicStoredState();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            DecisionFingerprint() + "\nrevision=" + stored.revision + "\n" + stored.documents)));
    }

    private void AssertCivicState(string expected, string operation)
    {
        if (CivicStateFingerprint() != expected)
            throw new InvalidOperationException(operation + "で表示以外のゲーム状態・保存・乱数が変化しました。");
    }

    private object? CivicBoundary(string name)
    {
        var control = FindChild(name, true, false) as Control;
        if (control is null || !IsInstanceValid(control)) return null;
        var rect = control.GetGlobalRect();
        return new { name, visible = control.IsVisibleInTree(), x = rect.Position.X, y = rect.Position.Y,
            width = rect.Size.X, height = rect.Size.Y, focusable = control.FocusMode != FocusModeEnum.None };
    }

    private object CreateCivicCaptureMetadata() => new
    {
        windowMode = DisplayServer.WindowGetMode().ToString(),
        displayVerified = _civicDisplayVerified, maximizedRestoreVerified = _civicMaximizedRestoreVerified, detailsVerified = _civicDetailsVerified,
        geometryVerified = _civicGeometryVerified, stampVerified = _civicStampVerified,
        fixture = _civicFixtureDescription,
        actors = _view?.Adventurers.Count,
        focus = GetViewport().GuiGetFocusOwner()?.Name.ToString(),
        boundaries = new[] { "CivicRoster", "CivicMap", "CivicDispatch", "CivicCosts", "StampStartWave", "CivicDetailsToggle", "CivicDetailsScroll", "PreparationRangeToggle", "PreparationSkillRange", "CivicTargetPriority", "DisplayModeToggle" }.Select(CivicBoundary).ToArray(),
        map = _map is null ? null : new
        {
            cell = _map.BoardCellSizeForSmoke,
            boardX = _map.GetBoardRectForSmoke().Position.X,
            boardY = _map.GetBoardRectForSmoke().Position.Y,
            boardWidth = _map.GetBoardRectForSmoke().Size.X,
            boardHeight = _map.GetBoardRectForSmoke().Size.Y
        }
    };

    private void CaptureCivicEvidence(string suffix)
    {
        if (string.IsNullOrWhiteSpace(_capturePath)) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(_capturePath))!;
        var prefix = Path.Combine(directory, Path.GetFileNameWithoutExtension(_capturePath) + "." + suffix);
        var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(prefix + ".png") != Error.Ok) throw new IOException("全画面の証拠画像を保存できません。");
        var viewport = GetViewport().GetVisibleRect().Size;
        var window = DisplayServer.WindowGetSize();
        System.IO.File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new
        {
            viewportWidth = viewport.X, viewportHeight = viewport.Y,
            windowWidth = window.X, windowHeight = window.Y,
            pngWidth = image.GetWidth(), pngHeight = image.GetHeight(), civic = CreateCivicCaptureMetadata()
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("GUILD_CIVIC_FULLSCREEN_CAPTURE=" + prefix + ".png");
    }
}
