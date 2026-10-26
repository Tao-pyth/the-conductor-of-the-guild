using Godot;
using Guild.Application;

namespace Guild.Game;

public partial class Main
{
    private Label? _civicResources;
    private Label? _civicPlacementCount;
    private Label? _civicWeapon;
    private Label? _civicActionHint;
    private Label? _civicRangeBrief;
    private Label? _civicLegacyNote;
    private ProgressBar? _civicHp;
    private Control? _civicMapLayer;
    private Control? _civicDetailsLayer;
    private Button? _civicDetailsButton;
    private bool _civicDetailsOpen;

    private void ResetCivicPreparation()
    {
        _civicResources = null; _civicPlacementCount = null; _civicWeapon = null;
        _civicActionHint = null; _civicRangeBrief = null; _civicLegacyNote = null;
        _civicHp = null; _civicMapLayer = null; _civicDetailsLayer = null; _civicDetailsButton = null;
        if (_view?.Screen != "Preparation") _civicDetailsOpen = false;
    }

    private static VBoxContainer CivicPanel(Container parent, string name, float width = 0)
    {
        var frame = new PanelContainer { Name = name, CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", CivicSurface()); parent.AddChild(frame);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        frame.AddChild(content);
        return content;
    }

    private static Label CivicHeading(Container parent, string text, int size = 30)
    {
        var label = AddText(parent, text); label.AddThemeFontSizeOverride("font_size", size); label.AddThemeFontOverride("font", CivicDisplayFont); return label;
    }

    private static TextureRect CivicImage(Container parent, Texture2D? texture, Vector2 minimum)
    {
        var image = new TextureRect
        {
            Texture = texture, CustomMinimumSize = minimum,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        parent.AddChild(image); return image;
    }

    private void BuildCivicPreparation()
    {
        if (_view is null) return;
        var invasion = _view.Mode == "invasion";
        var actors = _view.Adventurers.Where(actor => !actor.IsDispatched && (!invasion || actor.InParty)).ToArray();
        if (!actors.Any(actor => actor.Id == _selectedActor)) _selectedActor = actors.FirstOrDefault()?.Id;
        var actor = SelectedActor();
        var next = _view.NextEncounter;

        var forecastFrame = new PanelContainer { Name = "CivicForecast", CustomMinimumSize = new Vector2(0, 76) };
        forecastFrame.AddThemeStyleboxOverride("panel", CivicSurface("fffdf6", "baa879", 10)); _body.AddChild(forecastFrame);
        var forecast = Row(forecastFrame);
        var description = next is null ? DisplayText.Get("ui.preparation.001") : invasion
            ? DisplayText.Format("ui.preparation.002", next.Number, next.Total, next.Name, next.GuardCount, next.TurretCount, next.GateCount, next.CoreCount, next.TimeLimitSeconds)
            : DisplayText.Format("ui.preparation.005", next.Number, next.Total, next.EnemyCount, next.EnemyHp, _view.DefenseHp, (next.AttacksAdventurers == true ? DisplayText.Get("ui.preparation.003") : DisplayText.Get("ui.preparation.004")));
        var preview = AddText(forecast, description, true); preview.SizeFlagsHorizontal = SizeFlags.ExpandFill; preview.AddThemeFontSizeOverride("font_size", 23);
        CivicImage(forecast, CivicAssets.Texture("guild-panorama"), new Vector2(290, 62));
        _civicResources = AddText(forecast, "", true); _civicResources.CustomMinimumSize = new Vector2(248, 0);
        _civicResources.HorizontalAlignment = HorizontalAlignment.Right;

        var split = new HBoxContainer { Name = "CivicPreparation", SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 16); _body.AddChild(split);
        var roster = CivicPanel(split, "CivicRoster", 300);
        CivicHeading(roster, DisplayText.Get("ui.home.roster"));
        _civicPlacementCount = AddText(roster, ""); _civicPlacementCount.Modulate = MutedColor;
        var rosterScroll = DecisionScroll(roster); rosterScroll.Name = "CivicRosterScroll";
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 4); rosterScroll.AddChild(list);
        foreach (var member in actors)
        {
            var id = member.Id;
            var button = AddButton(list, "", () => { _selectedActor = id; RenderScreen(); });
            button.Name = "Inspect_" + id; button.Alignment = HorizontalAlignment.Left;
            button.ClipText = true; button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            button.TooltipText = ActorButtonText(member) + "\n" + member.Weapon;
            button.CustomMinimumSize = new Vector2(0, 96); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var rowContent = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            rowContent.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            rowContent.OffsetLeft = 10; rowContent.OffsetTop = 8; rowContent.OffsetRight = -10; rowContent.OffsetBottom = -8;
            button.AddChild(rowContent);
            CivicImage(rowContent, CivicAssets.Weapon(member.WeaponType), new Vector2(46, 58));
            var identityText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            identityText.AddThemeConstantOverride("separation", 3); rowContent.AddChild(identityText);
            var nameRow = Row(identityText); nameRow.MouseFilter = MouseFilterEnum.Ignore;
            var name = AddText(nameRow, member.Name); name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.AddThemeFontSizeOverride("font_size", 24); name.MouseFilter = MouseFilterEnum.Ignore;
            var status = AddText(nameRow, member.Incapacitated ? DisplayText.Get("ui.main.055") : member.Deployed ? DisplayText.Get("ui.preparation.006") : DisplayText.Get("ui.main.061"));
            status.AddThemeFontSizeOverride("font_size", 18); status.MouseFilter = MouseFilterEnum.Ignore;
            status.Modulate = member.Deployed ? new Color("a13c30") : MutedColor;
            var health = AddText(identityText, DisplayText.Format("ui.common.hp", member.Hp, member.MaxHp)); health.AddThemeFontSizeOverride("font_size", 20); health.MouseFilter = MouseFilterEnum.Ignore;
            var hpValue = double.TryParse(member.Hp, out var currentHp) ? currentHp : 0;
            var hpMaximum = double.TryParse(member.MaxHp, out var maximumHp) ? maximumHp : 1;
            identityText.AddChild(new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, hpMaximum), Value = hpValue, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore });
            if (member.Id == _selectedActor)
            {
                var selected = CivicSurface("e2efeb", "2f6f73", 12); selected.SetBorderWidthAll(2);
                button.AddThemeStyleboxOverride("normal", selected);
            }
            _actorButtons[id] = button;
        }
        var rosterTotal = AddText(roster, invasion ? DisplayText.Format("ui.preparation.007", actors.Length) : DisplayText.Format("ui.preparation.008", _view.Adventurers.Count), true);
        if (_view.Adventurers.Any(member => member.IsDispatched))
            rosterTotal.Text += "\n" + DisplayText.Format("economy.ui.absent_members", string.Join("、", _view.Adventurers.Where(member => member.IsDispatched).Select(member => member.Name)));
        rosterTotal.Modulate = MutedColor;

        var centerFrame = new PanelContainer { Name = "CivicMapArea", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        centerFrame.AddThemeStyleboxOverride("panel", CivicSurface("fffdf6", "cfbf9b", 10)); split.AddChild(centerFrame);
        var center = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; centerFrame.AddChild(center);
        var mapLayer = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; center.AddChild(mapLayer); _civicMapLayer = mapLayer;
        _map = new GuildMap
        {
            Name = "CivicMap", ReadView = () => _view, SelectedActor = () => _selectedActor, CellSelected = MapClicked,
            ShowPreparationRanges = () => _showPreparationRanges, RepositionPreview = () => _repositionMode,
            SelectedRangeSkill = () => _rangeSkillSlot, CustomMinimumSize = new Vector2(650, 360),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            Font = Theme.DefaultFont, ClipContents = true
        };
        mapLayer.AddChild(_map);
        var controls = Row(mapLayer);
        AddButton(controls, _repositionMode ? DisplayText.Get("ui.preparation.009") : DisplayText.Get("ui.preparation.010"), () => { _repositionMode = !_repositionMode; RenderScreen(); }).Name = "DecisionReposition";
        var rangeToggle = new CheckBox { Name = "PreparationRangeToggle", Text = DisplayText.Get("ui.preparation.011"), ButtonPressed = _showPreparationRanges };
        rangeToggle.Toggled += value => { _showPreparationRanges = value; _map?.QueueRedraw(); UpdateRangeDescription(); RefreshCivicValues(); }; controls.AddChild(rangeToggle);
        var skillPicker = new OptionButton { Name = "PreparationSkillRange", FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        skillPicker.AddItem(DisplayText.Get("ui.preparation.012"));
        var skills = actor?.RangeGuide?.SkillRadii.ToArray() ?? [];
        for (var i = 0; i < skills.Length; i++) { skillPicker.AddItem(DisplayText.Format("ui.skill.slot", skills[i].Slot, skills[i].Name)); if (skills[i].Slot == _rangeSkillSlot) skillPicker.Select(i + 1); }
        skillPicker.ItemSelected += index => { _rangeSkillSlot = index == 0 ? "" : skills[(int)index - 1].Slot; _map?.QueueRedraw(); UpdateRangeDescription(); RefreshCivicValues(); }; controls.AddChild(skillPicker);
        AddButton(controls, DisplayText.Get("ui.preparation.013"), () => SetCivicDetails(true)).Name = "CivicRangeConditions";
        var instruction = AddText(mapLayer, invasion ? DisplayText.Get("ui.preparation.014") : DisplayText.Get("ui.preparation.015"), true);
        instruction.AddThemeFontSizeOverride("font_size", 20);
        _civicRangeBrief = AddText(mapLayer, "", true); _civicRangeBrief.AddThemeFontSizeOverride("font_size", 20); _civicRangeBrief.Modulate = MutedColor;
        _civicLegacyNote = AddText(mapLayer, "", true); _civicLegacyNote.AddThemeFontSizeOverride("font_size", 20); _civicLegacyNote.Modulate = new Color("9b432f");

        var detailsLayer = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false }; center.AddChild(detailsLayer); _civicDetailsLayer = detailsLayer;
        var detailsHeader = Row(detailsLayer);
        CivicHeading(detailsHeader, DisplayText.Get("ui.preparation.016"), 28).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddButton(detailsHeader, DisplayText.Get("ui.preparation.017"), () => SetCivicDetails(false)).Name = "CivicDetailsClose";
        var detailsScroll = DecisionScroll(detailsLayer); detailsScroll.Name = "CivicDetailsScroll"; detailsScroll.FocusMode = FocusModeEnum.All;
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; detailsScroll.AddChild(details);
        if (actor is not null)
        {
            var profile = FeedbackSection(details, DisplayText.Get("ui.section.profile"), "Section_Profile");
            AddText(profile, DisplayText.Format("ui.preparation.018", actor.Name, actor.Rank, actor.Level, actor.Weapon), true);
            AppendPerformanceDetails(details, actor);
            var blessings = FeedbackSection(details, DisplayText.Get("ui.section.blessings"), "Section_Blessings");
            if (actor.BlessingDetails.Count == 0) AddText(blessings, DisplayText.Get("ui.blessing.none"), true);
            foreach (var blessing in actor.BlessingDetails)
                AddText(blessings, DisplayText.Format("ui.blessing.owned_line", blessing.Name, FeedbackRoman(blessing.Rank), blessing.Description), true);
        }
        _decisionRangeText = AddText(details, "", true); _decisionRangeText.AddThemeFontSizeOverride("font_size", 22);
        SetCivicDetails(_civicDetailsOpen);

        var dispatch = CivicPanel(split, "CivicDispatch", 420); dispatch.AddThemeConstantOverride("separation", 7);
        var slipHeader = Row(dispatch); CivicHeading(slipHeader, DisplayText.Get("ui.preparation.019")).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var state = AddText(slipHeader, actor?.Incapacitated == true ? DisplayText.Get("ui.main.055") : actor?.Deployed == true ? DisplayText.Get("ui.preparation.006") : DisplayText.Get("ui.main.061"));
        state.Modulate = actor?.Deployed == true ? new Color("b43b2f") : MutedColor;
        var picker = new OptionButton { Name = "DecisionActorPicker", FitToLongestItem = false };
        for (var i = 0; i < actors.Length; i++) { picker.AddItem(actors[i].Name); if (actors[i].Id == _selectedActor) picker.Select(i); }
        picker.ItemSelected += index => { _selectedActor = actors[(int)index].Id; RenderScreen(); }; dispatch.AddChild(picker);
        var identity = Row(dispatch);
        CivicImage(identity, actor is null ? null : CivicAssets.Weapon(actor.WeaponType), new Vector2(52, 60));
        _actorDetails = AddText(identity, "", true); _actorDetails.SizeFlagsHorizontal = SizeFlags.ExpandFill; _actorDetails.AddThemeFontSizeOverride("font_size", 23);
        _civicWeapon = AddText(dispatch, actor?.Weapon ?? "", true); _civicWeapon.AddThemeFontSizeOverride("font_size", 22);
        _civicHp = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14) }; dispatch.AddChild(_civicHp);
        _preparationCosts = AddText(dispatch, "", true); _preparationCosts.Name = "CivicCosts"; _preparationCosts.AddThemeFontSizeOverride("font_size", 23);
        var actions = Row(dispatch);
        _buyLevelButton = AddButton(actions, DisplayText.Get("ui.preparation.020"), () => Dispatch(new("BuyLevel", ActorId: _selectedActor)));
        _buyLevelButton.SizeFlagsHorizontal = SizeFlags.ExpandFill; _buyLevelButton.ClipText = true;
        _reviveButton = AddButton(actions, DisplayText.Get("ui.main.088"), () => Dispatch(new("Revive", ActorId: _selectedActor)));
        _reviveButton.SizeFlagsHorizontal = SizeFlags.ExpandFill; _reviveButton.ClipText = true;
        _civicActionHint = AddText(dispatch, "", true); _civicActionHint.AddThemeFontSizeOverride("font_size", 19); _civicActionHint.Modulate = MutedColor;
        if (actor?.Performance is { } performance)
        {
            AddText(dispatch, DisplayText.Format("ui.preparation.022", performance.WeaponRange)).AddThemeFontSizeOverride("font_size", 22);
            var skillFrame = new PanelContainer { Name = "PreparationSkillSummary" };
            skillFrame.AddThemeStyleboxOverride("panel", CivicSurface("f1f6fa", "bdcede", 8)); dispatch.AddChild(skillFrame);
            var skillSummary = new VBoxContainer(); skillSummary.AddThemeConstantOverride("separation", 3); skillFrame.AddChild(skillSummary);
            AddText(skillSummary, DisplayText.Get("ui.section.skills")).AddThemeFontSizeOverride("font_size", 21);
            if (performance.SkillDetails.Count == 0) AddText(skillSummary, DisplayText.Get("ui.skill.none"), true).AddThemeFontSizeOverride("font_size", 20);
            foreach (var skill in performance.SkillDetails)
            {
                var label = AddText(skillSummary, DisplayText.Format("ui.preparation.023", skill.Slot, skill.Name, skill.Range), true); label.AddThemeFontSizeOverride("font_size", 21);
            }
        }
        _civicDetailsButton = AddButton(dispatch, _civicDetailsOpen ? DisplayText.Get("ui.preparation.017") : DisplayText.Get("ui.preparation.024"), () => SetCivicDetails(!_civicDetailsOpen)); _civicDetailsButton.Name = "CivicDetailsToggle";
        AddText(dispatch, DisplayText.Get("ui.preparation.025")).AddThemeFontSizeOverride("font_size", 21);
        var target = new OptionButton { Name = "CivicTargetPriority", FitToLongestItem = false };
        var keys = invasion ? new[] { "nearest", "low-hp", "high-hp", "structures" } : new[] { "arrival", "low-hp", "high-hp", "nearest" };
        foreach (var label in invasion ? new[] { DisplayText.Get("ui.main.041"), DisplayText.Get("ui.preparation.026"), DisplayText.Get("ui.preparation.027"), DisplayText.Get("ui.main.044") } : new[] { DisplayText.Get("ui.main.045"), DisplayText.Get("ui.preparation.026"), DisplayText.Get("ui.preparation.027"), DisplayText.Get("ui.main.046") }) target.AddItem(label);
        target.Select(Math.Max(0, Array.IndexOf(keys, actor?.TargetPreset)));
        target.ItemSelected += index => Dispatch(new("SetTarget", ActorId: _selectedActor, Value: keys[(int)index])); dispatch.AddChild(target);
        dispatch.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        BuildCivicStamp(dispatch, invasion);
        UpdateRangeDescription(); RefreshCivicValues();
    }

    private void BuildCivicStamp(Container parent, bool invasion)
    {
        var frame = new PanelContainer { Name = "CivicStamp", CustomMinimumSize = new Vector2(0, 116) };
        frame.AddThemeStyleboxOverride("panel", CivicSurface("fffdf6", "bc9a70", 10)); parent.AddChild(frame);
        var row = Row(frame);
        var margin = AddText(row, DisplayText.Get("ui.preparation.028")); margin.AddThemeFontSizeOverride("font_size", 18); margin.Modulate = MutedColor;
        var actions = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; actions.AddThemeConstantOverride("separation", 5); row.AddChild(actions);
        _startWaveButton = AddButton(actions, DisplayText.Get("ui.preparation.029"), StampStartEncounter); _startWaveButton.Name = "StampStartWave";
        _startWaveButton.AddThemeFontSizeOverride("font_size", 44); _startWaveButton.AddThemeFontOverride("font", CivicDisplayFont); _startWaveButton.AddThemeColorOverride("font_color", new Color("b43b2f"));
        _startWaveButton.AddThemeColorOverride("font_hover_color", new Color("9e3026")); _startWaveButton.AddThemeColorOverride("font_pressed_color", new Color("9e3026"));
        var seal = CivicSurface("fffdf6", "b43b2f", 8); seal.SetBorderWidthAll(3); _startWaveButton.AddThemeStyleboxOverride("normal", seal);
        var pressed = (StyleBoxFlat)seal.Duplicate(); pressed.BgColor = new Color("f8e4db"); _startWaveButton.AddThemeStyleboxOverride("pressed", pressed);
        var hover = (StyleBoxFlat)seal.Duplicate(); hover.BgColor = new Color("fff0e8"); _startWaveButton.AddThemeStyleboxOverride("hover", hover);
        var number = Math.Min(_view!.Wave + 1, _view.TotalWaves);
        var hint = AddText(actions, invasion ? DisplayText.Format("ui.preparation.030", number) : DisplayText.Format("ui.preparation.031", number), true); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.AddThemeFontSizeOverride("font_size", 20);
        CivicImage(row, CivicAssets.Texture("wooden-stamp"), new Vector2(58, 94));
        // Match the existing StartWave rule: only invasion requires a surviving deployed ally.
        _startWaveButton.Disabled = !_view.Adventurers.Any(item => item.Deployed && (!invasion || !item.Incapacitated));
        _startWaveButton.TooltipText = _startWaveButton.Disabled ? DisplayText.Get("ui.preparation.032") : DisplayText.Get("ui.preparation.033");
    }

    private void SetCivicDetails(bool open)
    {
        _civicDetailsOpen = open;
        if (_civicMapLayer is not null) _civicMapLayer.Visible = !open;
        if (_civicDetailsLayer is not null) _civicDetailsLayer.Visible = open;
        if (_civicDetailsButton is not null) _civicDetailsButton.Text = open ? DisplayText.Get("ui.preparation.017") : DisplayText.Get("ui.preparation.024");
    }

    private void RefreshCivicValues()
    {
        if (_view?.Screen != "Preparation") return;
        if (_civicResources is not null) _civicResources.Text = DisplayText.Format("ui.preparation.036", _view.MatchResources, (_view.Mode == "invasion" ? DisplayText.Get("ui.preparation.034") : DisplayText.Get("ui.preparation.035")));
        if (_civicPlacementCount is not null) _civicPlacementCount.Text = DisplayText.Format("ui.preparation.037", _view.Adventurers.Count(item => item.Deployed));
        var actor = SelectedActor(); if (actor is null) return;
        if (_actorDetails is not null) _actorDetails.Text = DisplayText.Format("ui.preparation.038", actor.Rank, actor.Level, actor.Hp, actor.MaxHp);
        if (_civicHp is not null && double.TryParse(actor.Hp, out var hp) && double.TryParse(actor.MaxHp, out var maxHp)) _civicHp.Value = maxHp <= 0 ? 0 : hp / maxHp;
        if (actor.PreparationInfo is { } info)
        {
            if (_preparationCosts is not null) _preparationCosts.Text = (actor.Deployed ? DisplayText.Get("ui.preparation.039") : DisplayText.Format("ui.preparation.040", info.DeploymentCost))
                + (info.IsLevelCap ? DisplayText.Get("ui.preparation.041") : DisplayText.Format("ui.preparation.042", info.MissingLevelXp, info.PurchaseCost));
            if (_buyLevelButton is not null) _buyLevelButton.Text = info.IsLevelCap ? DisplayText.Get("ui.decision.026") : DisplayText.Format("ui.preparation.043", info.PurchaseCost);
            if (_reviveButton is not null) _reviveButton.Text = actor.Incapacitated ? DisplayText.Format("ui.preparation.044", info.RevivalCost) : DisplayText.Get("ui.preparation.045");
            if (_civicActionHint is not null)
            {
                var reason = !actor.Deployed && !info.CanDeploy ? info.DeployUnavailableReason
                    : actor.Incapacitated ? DisplayText.Format("ui.preparation.046", info.ReviveUnavailableReason) : !info.CanPurchaseLevel ? info.PurchaseUnavailableReason : DisplayText.Get("ui.preparation.047");
                _civicActionHint.Text = reason ?? ""; _civicActionHint.Visible = !string.IsNullOrEmpty(reason);
            }
        }
        var guide = actor.RangeGuide;
        if (_civicRangeBrief is not null && guide is not null)
        {
            var skill = guide.SkillRadii.FirstOrDefault(item => item.Slot == _rangeSkillSlot);
            _civicRangeBrief.Text = DisplayText.Format("ui.preparation.048", (_showPreparationRanges ? DisplayText.Get("ui.decision.020") : DisplayText.Get("ui.decision.021")), guide.AttackRadius, guide.WatchRadius, guide.ActionRadius)
                + (skill is null ? "" : DisplayText.Format("ui.preparation.049", skill.Slot, skill.Radius)) + DisplayText.Get("ui.preparation.050");
        }
        if (_civicLegacyNote is not null)
        {
            _civicLegacyNote.Visible = guide?.IsLegacyDefense == true;
            _civicLegacyNote.Text = DisplayText.Get("ui.preparation.051");
        }
    }
}
