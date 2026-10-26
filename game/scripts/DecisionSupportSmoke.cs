using Godot;
using Guild.Application;
using System.Text.Json;

namespace Guild.Game;

// Explicit test-only scenarios exercise the same controls as the player.
// They require an isolated save path and a bounded --smoke-test invocation.
public partial class Main
{
    private string? _decisionSmokeScenario;
    private bool _decisionSmokePending;
    private bool _decisionSmokeRunning;
    private bool _decisionSmokeFailed;

    private void ConfigureDecisionSmoke(string[] args)
    {
        _decisionSmokeScenario = ReadArgument(args, "--decision-scenario");
        if (_decisionSmokeScenario is null) return;
        if (_smokeSeconds <= 0 || ReadArgument(args, "--save-path") is null
            || args.Contains("--invasion-scenario") || args.Contains("--smoke-screen") || args.Contains("--smoke-battle"))
            throw new InvalidOperationException("判断材料の検証には専用の検証saveとsmoke-testだけを指定してください。");
        _decisionSmokePending = true;
    }

    private string DecisionFingerprint() => JsonSerializer.Serialize(_controller!.View);

    private async void RunDecisionSmoke()
    {
        _decisionSmokePending = false;
        _decisionSmokeRunning = true;
        try
        {
            if (_controller is null || _view is null) throw new InvalidOperationException("初期化されていません。");
            var scenario = _decisionSmokeScenario;
            if (IsFeedbackSmokeScenario(scenario))
            {
                await RunFeedbackUiSmoke(scenario!);
                return;
            }
            if (!IsCivicSmokeScenario(scenario) && scenario is not ("ComparisonDefense" or "ComparisonInvasion" or "Budget" or "Hover" or "Ranges" or "RangesDetails" or "RangeOff" or "Reposition" or "Growth" or "GrowthChoose" or "DefenseDefeatRetry" or "DefenseDefeatReturn"))
                throw new ArgumentException("未対応の判断材料シナリオです: " + scenario);
            if (_view.Screen != "Guild") throw new InvalidOperationException("検証は拠点の試験保存から開始します。");
            if (IsCivicSmokeScenario(scenario))
            {
                await RunCivicUiSmoke(scenario!);
                return;
            }
            if (scenario is "Growth" or "GrowthChoose")
            {
                var expected = _view.NextGrowthEvent ?? throw new InvalidOperationException("成長イベントのある試験保存が必要です。");
                _selectedActor = _view.Adventurers.FirstOrDefault(a => a.Id != expected.ActorId)?.Id ?? expected.ActorId;
                RenderScreen();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var before = DecisionFingerprint();
                RenderScreen();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (DecisionFingerprint() != before) throw new InvalidOperationException("成長表示でゲーム状態が変化しました。");
                GD.Print($"GUILD_DECISION_GROWTH_PREVIEW_OK recipient={expected.ActorId} inspected={_selectedActor} event={expected.EventId}");
                if (scenario == "GrowthChoose")
                {
                    var button = _body.FindChild("Growth_blessing", true, false) as Button;
                    PressSmokeButton(button, "表示対象の加護イベント");
                    if (_view.Screen != "RankEvent" || _view.EventActorId != expected.ActorId
                        || _view.NextGrowthEvent?.EventId != expected.EventId)
                        throw new InvalidOperationException("表示と成長の実行対象が一致しません。");
                    GD.Print("GUILD_DECISION_GROWTH_BOUND_OK");
                }
                return;
            }
            _selectedMode = scenario is "ComparisonInvasion" or "Budget" ? "invasion" : "defense";
            _selectedStageId = null;
            OpenExpedition();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (scenario is "ComparisonDefense" or "ComparisonInvasion")
            {
                var selectedBefore = _expeditionIds.OrderBy(x => x).ToArray();
                var before = DecisionFingerprint();
                var inspectId = _view.Adventurers.Last().Id;
                var button = _body.FindChild("Inspect_" + inspectId, true, false) as Button;
                button?.GrabFocus();
                PressSmokeButton(button, "能力の比較");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!_expeditionIds.OrderBy(x => x).SequenceEqual(selectedBefore) || DecisionFingerprint() != before)
                    throw new InvalidOperationException("比較閲覧で編成かゲーム状態が変わりました。");
                if (GetViewport().GuiGetFocusOwner()?.Name.ToString() != "Inspect_" + inspectId)
                    throw new InvalidOperationException("比較後にフォーカスが失われました。");
                GD.Print($"GUILD_DECISION_COMPARISON_OK mode={_selectedMode} focus={GetViewport().GuiGetFocusOwner()?.Name}");
                return;
            }
            if (scenario == "Budget")
            {
                if (_partyCount is null || !_partyCount.Text.Contains("1500", StringComparison.Ordinal) || _startMatchButton?.Disabled != false)
                    throw new InvalidOperationException("参考合計1500の超過編成を確認できません。");
                GD.Print("GUILD_DECISION_BUDGET_REFERENCE_OK total=1500");
            }
            PressSmokeButton(_startMatchButton, "準備画面への出撃");
            if (_view.Screen != "Preparation") throw new InvalidOperationException("出撃できませんでした。");
            if (scenario == "Budget")
            {
                if (_view.Mode != "invasion" || _view.PartyIds.Count != 6 || _view.MatchResources != "1000")
                    throw new InvalidOperationException("参考合計超過の出撃が課金/制限されました。");
                GD.Print("GUILD_DECISION_BUDGET_OK selected=6 resources=1000");
                return;
            }
            if (scenario is "DefenseDefeatRetry" or "DefenseDefeatReturn")
            {
                _selectedActor = _view.Adventurers[0].Id;
                RenderScreen();
                _map?.ClickCellForSmoke(1, 1);
                for (var step = 0; step < 100 && _view.Screen != "Defeat"; step++)
                {
                    if (_view.Screen == "Preparation") PressSmokeButton(_startWaveButton, "防衛開始");
                    else if (_view.Screen == "LevelChoice") PressSmokeButton(_levelChoiceButtons.FirstOrDefault(), "防衛強化");
                    else if (_view.Screen == "Battle") { _controller.Advance(120); _view = _controller.View; RenderScreen(); }
                    else throw new InvalidOperationException("防衛敗北試験の予期しない画面: " + _view.Screen);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                if (_view.Screen != "Defeat") throw new InvalidOperationException("防衛敗北に到達しません。");
                GD.Print("GUILD_DECISION_DEFENSE_DEFEAT_OK");
                if (scenario == "DefenseDefeatRetry")
                {
                    PressSmokeButton(_retryButton, "防衛再挑戦");
                    if (_view.Screen != "Preparation" || _view.Wave != 0) throw new InvalidOperationException("防衛再挑戦で開始点へ戻りません。");
                    GD.Print("GUILD_DECISION_DEFENSE_RETRY_OK");
                }
                else
                {
                    PressSmokeButton(_returnButton, "防衛敗北の精算");
                    if (_view.Screen != "Guild" || _view.GuildResources != "500") throw new InvalidOperationException("防衛敗北の精算が一致しません。");
                    GD.Print("GUILD_DECISION_DEFENSE_RETURN_OK resources=500");
                }
                return;
            }
            _selectedActor = _view.Adventurers.FirstOrDefault(a => a.RangeGuide?.SkillRadii.Any(s => s.IsHealing) == true)?.Id
                ?? _view.Adventurers[0].Id;
            RenderScreen();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var fingerprint = DecisionFingerprint();
            _map?.HoverCellForSmoke(-1, 4);
            _map?.ClickCellForSmoke(-1, 4);
            _map?.HoverCellForSmoke(8, 5);
            _map?.ClickCellForSmoke(8, 5);
            if (DecisionFingerprint() != fingerprint) throw new InvalidOperationException("無効マスで状態が変化しました。");
            _map?.HoverCellForSmoke(8, 4);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DecisionFingerprint() != fingerprint) throw new InvalidOperationException("候補位置の閲覧で状態が変化しました。");
            GD.Print("GUILD_DECISION_HOVER_PURE_OK");
            if (scenario == "Hover") return;
            _map?.ClickCellForSmoke(8, 4);
            if (SelectedActor()?.Deployed != true) throw new InvalidOperationException("初配置できませんでした。");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var skillPicker = _body.FindChild("PreparationSkillRange", true, false) as OptionButton;
            var radii = SelectedActor()!.RangeGuide!.SkillRadii.ToArray();
            var healingIndex = Array.FindIndex(radii, skill => skill.IsHealing);
            if (skillPicker is not null && healingIndex >= 0)
            {
                skillPicker.Select(healingIndex + 1);
                skillPicker.EmitSignal(OptionButton.SignalName.ItemSelected, healingIndex + 1);
            }
            var toggle = _body.FindChild("PreparationRangeToggle", true, false) as CheckBox
                ?? throw new InvalidOperationException("範囲切替がありません。");
            fingerprint = DecisionFingerprint();
            toggle.GrabFocus();
            GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = Key.Tab });
            GetViewport().PushInput(new InputEventKey { Pressed = false, Keycode = Key.Tab });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GetViewport().GuiGetFocusOwner() is null) throw new InvalidOperationException("フォーカス移動できません。");
            toggle.ButtonPressed = false;
            if (_showPreparationRanges) throw new InvalidOperationException("範囲OFFが反映されません。");
            if (scenario != "RangeOff") toggle.ButtonPressed = true;
            if (DecisionFingerprint() != fingerprint) throw new InvalidOperationException("表示切替で状態が変化しました。");
            GD.Print("GUILD_DECISION_RANGE_TOGGLE_PURE_OK");
            if (scenario == "RangesDetails")
            {
                skillPicker?.GrabFocus();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (GetViewport().GuiGetFocusOwner() != skillPicker)
                    throw new InvalidOperationException("技能範囲の選択へフォーカスできません。");
                GD.Print("GUILD_DECISION_DETAILS_FOCUS_OK");
            }
            if (scenario == "Reposition")
            {
                var oldX = SelectedActor()!.X;
                var resources = _view.MatchResources;
                _map?.ClickCellForSmoke(10, 4);
                if (SelectedActor()!.X != oldX || _view.MatchResources != resources)
                    throw new InvalidOperationException("再配置OFFなのに移動しました。");
                var reposition = _body.FindChild("DecisionReposition", true, false) as Button;
                PressSmokeButton(reposition, "再配置モード");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                fingerprint = DecisionFingerprint();
                _map?.HoverCellForSmoke(10, 4);
                if (DecisionFingerprint() != fingerprint) throw new InvalidOperationException("再配置の仮表示で状態が変化しました。");
                _map?.ClickCellForSmoke(10, 4);
                if (SelectedActor()!.X != 10 || _view.MatchResources != resources)
                    throw new InvalidOperationException("無料再配置できませんでした。");
                GD.Print("GUILD_DECISION_REPOSITION_OK");
            }
        }
        catch (Exception exception)
        {
            _decisionSmokeFailed = true;
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            _decisionSmokeRunning = false;
            GD.Print($"GUILD_DECISION_SCENARIO_{(_decisionSmokeFailed ? "FAILED" : "READY")} scenario={_decisionSmokeScenario} screen={_view?.Screen}");
        }
    }
}
