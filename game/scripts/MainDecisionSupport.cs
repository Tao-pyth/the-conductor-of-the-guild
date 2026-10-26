using Godot;
using Guild.Application;

namespace Guild.Game;

public partial class Main
{
    private void BuildGrowthPreview(Container parent)
    {
        var preview = _view?.NextGrowthEvent;
        if (preview is null) return;
        var card = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color("e8eee6"), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 };
        card.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(card);
        var content = new VBoxContainer();
        card.AddChild(content);
        AddText(content, DisplayText.Format("ui.decision.001", preview.ActorName, preview.EventRank, preview.CurrentRank, preview.PendingCount), true);
        AddText(content, DisplayText.Format("ui.decision.003", preview.WeaponName, (preview.Blessings.Count == 0 ? DisplayText.Get("ui.decision.002") : string.Join(" ／ ", preview.Blessings))), true).Modulate = MutedColor;
        AddText(content, DisplayText.Get("ui.decision.004"), true).Modulate = MutedColor;
        var options = Row(content);
        foreach (var option in preview.Options)
        {
            var eventId = preview.EventId;
            var actorId = preview.ActorId;
            var kind = option.Kind;
            var button = AddButton(options, option.Name, () => Dispatch(new("BeginEvent", ActorId: actorId, TargetId: eventId, Value: kind)));
            button.Name = "Growth_" + kind;
            button.Disabled = !option.CanBegin;
            button.TooltipText = option.UnavailableReason ?? "";
        }
        var unavailable = preview.Options.Where(option => !option.CanBegin && !string.IsNullOrWhiteSpace(option.UnavailableReason))
            .Select(option => DisplayText.Format("ui.common.reason", option.Name, option.UnavailableReason));
        var reasons = string.Join(" ／ ", unavailable);
        if (reasons.Length > 0) AddText(content, reasons, true).Modulate = MutedColor;
    }

    private bool _showPreparationRanges = true;
    private string _rangeSkillSlot = "";
    private Label? _decisionRangeText;

    private void BuildDecisionExpedition()
    {
        if (_view is null) return;
        var plan = FeedbackSection(_body, DisplayText.Get("ui.decision.005"), "ExpeditionPlan");
        var heading = Row(plan);
        AddButton(heading, DisplayText.Get("ui.decision.006"), () => { _choosingExpedition = false; RenderScreen(); }).Name = "DecisionBack";
        var modes = Row(plan);
        foreach (var (mode, label) in new[] { ("defense", DisplayText.Get("ui.mode.defense")), ("invasion", DisplayText.Get("ui.mode.invasion")) })
        {
            var modeId = mode;
            var button = AddButton(modes, label, () => { _selectedMode = modeId; SelectStage(null); RenderScreen(); });
            button.Name = "Mode_" + mode;
            button.Modulate = _selectedMode == mode ? new Color("a5e4d2") : Colors.White;
            button.Disabled = !_view.Stages.Any(s => s.Mode == mode);
        }
        SelectStage(_selectedStageId);
        var stages = _view.Stages.Where(stage => stage.Mode == _selectedMode).ToArray();
        var picker = new OptionButton { Name = "DecisionStagePicker", FitToLongestItem = false };
        for (var i = 0; i < stages.Length; i++)
        {
            picker.AddItem(stages[i].Name);
            if (stages[i].Id == _selectedStageId) picker.Select(i);
        }
        picker.ItemSelected += index => { SelectStage(stages[(int)index].Id); RenderScreen(); };
        plan.AddChild(picker);
        var stage = stages.FirstOrDefault(candidate => candidate.Id == _selectedStageId);
        if (stage is not null)
            AddText(plan, DisplayText.Format("ui.decision.009", (_selectedMode == "invasion" ? DisplayText.Format("ui.decision.007", stage.TotalSections, stage.TimeLimitMs / 1000) : DisplayText.Format("ui.decision.008", stage.TotalSections)), stage.InitialResources, stage.RewardSummary), true).Modulate = MutedColor;
        AddText(plan, _selectedMode == "invasion"
            ? DisplayText.Get("ui.decision.010")
            : DisplayText.Get("ui.decision.011"), true);
        BuildEconomyContribution(plan);
        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(split);
        var rosterSection = FeedbackSection(split, DisplayText.Get("ui.home.roster"), "ExpeditionRoster");
        rosterSection.GetParent<Control>().CustomMinimumSize = new Vector2(430, 0);
        rosterSection.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.Fill;
        rosterSection.GetParent<Control>().SizeFlagsVertical = SizeFlags.ExpandFill;
        var rosterScroll = DecisionScroll(rosterSection);
        var roster = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rosterScroll.AddChild(roster);
        foreach (var actor in _view.Adventurers)
        {
            var actorId = actor.Id;
            var row = Row(roster);
            if (_selectedMode == "invasion")
            {
                var check = new CheckBox { ButtonPressed = !actor.IsDispatched && _expeditionIds.Contains(actor.Id), Disabled = actor.IsDispatched,
                    TooltipText = actor.IsDispatched ? DisplayText.Get("economy.ui.dispatched_unavailable") : actor.Name + DisplayText.Get("ui.decision.012") };
                check.Name = "DecisionParty_" + actor.Id;
                check.Toggled += selected =>
                {
                    if (selected && _expeditionIds.Count >= 10)
                    {
                        check.SetPressedNoSignal(false);
                        _message.Text = DisplayText.Get("ui.decision.013");
                        return;
                    }
                    if (selected) _expeditionIds.Add(actorId); else _expeditionIds.Remove(actorId);
                    RefreshExpeditionCount();
                };
                row.AddChild(check);
            }
            if (actor.IsDispatched) AddText(row, EconomyDispatchActorStatus()).Modulate = new Color("9b432d");
            var inspect = AddButton(row, DisplayText.Format("ui.decision.014", actor.Name, actor.Rank, actor.Hp, actor.MaxHp, actor.Weapon, actor.DeploymentCost), () => { _selectedActor = actorId; RenderScreen(); });
            inspect.Name = "Inspect_" + actor.Id;
            inspect.Alignment = HorizontalAlignment.Left;
            inspect.ClipText = true;
            inspect.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            inspect.TooltipText = DisplayText.Format("ui.decision.014", actor.Name, actor.Rank, actor.Hp, actor.MaxHp, actor.Weapon, actor.DeploymentCost);
            inspect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            inspect.Modulate = actor.Id == _selectedActor ? new Color("a5e4d2") : Colors.White;
        }
        var detailScroll = DecisionScroll(split);
        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detailScroll.AddChild(detail);
        BuildActorDetails(detail);
        var footer = Row(_body);
        _partyCount = AddText(footer, "", true);
        _partyCount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _startMatchButton = AddButton(footer, _selectedMode == "invasion" ? DisplayText.Get("ui.decision.015") : DisplayText.Get("ui.decision.016"), ConfirmExpedition);
        _startMatchButton.Name = "DecisionStartMatch";
        RefreshExpeditionCount();
        AddText(_body, _selectedMode == "invasion"
            ? DisplayText.Get("ui.decision.017")
            : DisplayText.Get("ui.decision.018"), true).Modulate = MutedColor;
    }

    private static ScrollContainer DecisionScroll(Container parent, float minimumWidth = 0)
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(minimumWidth, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true
        };
        parent.AddChild(scroll);
        return scroll;
    }

    private void AppendPerformanceDetails(Container parent, AdventurerView actor) => AppendFeedbackPerformance(parent, actor);

    private void BuildDecisionPreparation() => BuildCivicPreparation();

    private void UpdateRangeDescription()
    {
        if (_decisionRangeText is null || !IsInstanceValid(_decisionRangeText)) return;
        var guide = SelectedActor()?.RangeGuide;
        if (guide is null) { _decisionRangeText.Text = ""; return; }
        var skill = guide.SkillRadii.FirstOrDefault(item => item.Slot == _rangeSkillSlot);
        var skillText = skill is null ? "" : DisplayText.Format("ui.decision.019", skill.Slot, skill.Name, skill.Radius);
        _decisionRangeText.Text = DisplayText.Format("ui.decision.022", (_showPreparationRanges ? DisplayText.Get("ui.decision.020") : DisplayText.Get("ui.decision.021")), guide.AttackRadius, guide.WatchRadius, guide.ActionRadius, skillText, guide.Explanation);
        if (guide.IsLegacyDefense)
            _decisionRangeText.Text += DisplayText.Get("ui.decision.023");
    }

    private static string PreparationDescription(AdventurerView actor)
    {
        if (actor.PreparationInfo is not { } info) return "";
        var placement = actor.Deployed ? DisplayText.Get("ui.decision.024") : DisplayText.Format("ui.decision.025", info.DeploymentCost);
        if (!actor.Deployed && !info.CanDeploy) placement += "\n" + info.DeployUnavailableReason;
        var growth = info.IsLevelCap ? DisplayText.Get("ui.decision.026")
            : DisplayText.Format("ui.decision.027", info.CurrentLevelXp, info.RequiredLevelXp, info.MissingLevelXp, info.PurchaseCost, info.PurchaseTargetLevel);
        var unavailable = info.CanPurchaseLevel ? "" : "\n" + info.PurchaseUnavailableReason;
        if (actor.Incapacitated)
            return DisplayText.Format("ui.decision.028", placement, info.RevivalCost, growth, unavailable);
        return DisplayText.Format("ui.preparation.combined_costs", placement, growth, unavailable);
    }

    private void BeginEventForDisplayedActor(string kind)
    {
        var preview = _view?.NextGrowthEvent;
        if (preview is null) return;
        Dispatch(new("BeginEvent", ActorId: preview.ActorId, TargetId: preview.EventId, Value: kind));
    }

    private void RestoreDecisionFocus(string? previousName)
    {
        Callable.From(() =>
        {
            Control? target = previousName is not null ? _body.FindChild(previousName, true, false) as Control : null;
            if (target is not null && target.IsVisibleInTree()) target.GrabFocus();
        }).CallDeferred();
    }
}
