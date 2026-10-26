using Godot;
using Guild.Application;
using System.Globalization;

namespace Guild.Game;

public partial class Main
{
    // Each section owns its heading and content so labels stay attached to the
    // information they describe when the parent scrolls.
    private static VBoxContainer FeedbackSection(Container parent, string title, string name)
    {
        var frame = new PanelContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", CivicSurface("fffdf6", "c9bfaa", 12));
        parent.AddChild(frame);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        frame.AddChild(content);
        var heading = AddText(content, title, true);
        heading.AddThemeFontSizeOverride("font_size", 25);
        heading.Modulate = new Color("254e69");
        content.AddChild(new HSeparator());
        return content;
    }

    private static string FeedbackMode(string mode) => DisplayText.Get(mode == "invasion" ? "ui.mode.invasion" : "ui.mode.defense");

    private void BuildFeedbackGuild()
    {
        var management = FeedbackSection(_body, DisplayText.Get("ui.home.management"), "HomeManagement");
        var commands = Row(management);
        AddButton(commands, DisplayText.Get("ui.home.expedition"), OpenExpedition).Name = "HomeExpedition";
        AddButton(commands, DisplayText.Get("ui.home.recruit"), () => Dispatch(new("Recruit")));
        AddButton(commands, DisplayText.Get("ui.home.expand"), () => Dispatch(new("ExpandRoster")));
        AddText(commands, DisplayText.Format("ui.home.overview", _view!.Adventurers.Count, _view.OwnedCapacity, _view.PendingEvents), true)
            .SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (_activeTab is not ("dispatch" or "deliveries"))
        {
            BuildEconomyHomeReport(_body);
            BuildGrowthPreview(_body);
        }
        var tabs = Row(_body);
        foreach (var (tab, key) in new[] { ("actors", "ui.tab.actors"), ("equipment", "ui.tab.equipment"), ("history", "ui.tab.history"),
            ("dispatch", "economy.ui.dispatch_tab"), ("deliveries", "economy.ui.delivery_tab") })
        {
            var tabName = tab;
            var button = AddButton(tabs, DisplayText.Get(key), () => { _activeTab = tabName; RenderScreen(); });
            button.Name = tab switch { "history" => "HomeHistoryTab", "equipment" => "HomeEquipmentTab", "dispatch" => "HomeDispatchTab", "deliveries" => "HomeDeliveryTab", _ => "HomeActorsTab" };
            button.Modulate = _activeTab == tab ? new Color("b7dcef") : Colors.White;
        }
        if (_activeTab == "dispatch") { BuildGuildDispatch(_body); return; }
        if (_activeTab == "deliveries") { BuildGuildDeliveries(_body); return; }
        if (_activeTab == "history") { BuildHistory(_body); return; }
        var split = new HBoxContainer { Name = "HomeContent", SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(split);
        var roster = FeedbackSection(split, DisplayText.Get("ui.home.roster"), "HomeRoster");
        roster.GetParent<Control>().CustomMinimumSize = new Vector2(300, 0);
        roster.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.Fill;
        roster.GetParent<Control>().SizeFlagsVertical = SizeFlags.ExpandFill;
        BuildActorList(roster);
        var detailsScroll = DecisionScroll(split); detailsScroll.Name = "HomeDetailsScroll";
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detailsScroll.AddChild(details);
        BuildActorDetails(details);
        var equipment = FeedbackSection(details, DisplayText.Get("ui.section.equipment"), "Section_Equipment");
        if (_activeTab == "equipment")
            AddButton(equipment, DisplayText.Get("ui.home.buy_weapon"), () => Dispatch(new("BuyWeapon")));
        else
        {
            var naming = FeedbackSection(details, DisplayText.Get("ui.section.rename"), "HomeRename");
            var row = Row(naming);
            var input = new LineEdit { Text = SelectedActor()?.Name ?? "", PlaceholderText = DisplayText.Get("ui.home.name_placeholder"), MaxLength = 40, CustomMinimumSize = new Vector2(260, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _renameInput = input; row.AddChild(input);
            Action renameAction = () => Dispatch(new("Rename", ActorId: _selectedActor, Value: input.Text));
            input.TextSubmitted += _ => renameAction();
            _renameButton = AddButton(row, DisplayText.Get("ui.home.rename"), renameAction);
            AddText(naming, DisplayText.Get("ui.home.roles"), true).Modulate = MutedColor;
        }
        BuildItems(equipment, false);
    }

    private void BuildFeedbackActorDetails(Container parent)
    {
        var actor = SelectedActor();
        if (actor is null) return;
        var profile = FeedbackSection(parent, DisplayText.Get("ui.section.profile"), "Section_Profile");
        _actorDetails = AddText(profile, "", true);
        if (_view?.Screen is "Guild" or "Preparation") AppendPerformanceDetails(parent, actor);
        else
        {
            var skills = FeedbackSection(parent, DisplayText.Get("ui.section.skills"), "Section_Skill");
            if (actor.Skills.Count == 0) AddText(skills, DisplayText.Get("ui.skill.none"), true);
            foreach (var skill in actor.Skills)
            {
                AddText(skills, DisplayText.Format("ui.skill.slot", skill.Slot, skill.Name), true);
                var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = skill.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                skills.AddChild(bar); _skillBars[skill.Slot] = bar;
            }
        }
        var blessings = FeedbackSection(parent, DisplayText.Get("ui.section.blessings"), "Section_Blessings");
        if (actor.BlessingDetails.Count == 0) AddText(blessings, DisplayText.Get("ui.blessing.none"), true);
        foreach (var blessing in actor.BlessingDetails)
            AddText(blessings, DisplayText.Format("ui.blessing.owned_line", blessing.Name, FeedbackRoman(blessing.Rank), blessing.Description), true);
    }

    private void AppendFeedbackPerformance(Container parent, AdventurerView actor)
    {
        if (_view?.Screen is not ("Guild" or "Preparation") || actor.Performance is not { } info) return;
        var stats = FeedbackSection(parent, DisplayText.Get("ui.section.performance"), "ActorPerformance");
        AddText(stats, info.Context, true).Modulate = new Color("286d60");
        var columns = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("h_separation", 22); stats.AddChild(columns);
        AddText(columns, DisplayText.Format("ui.performance.attack", info.AttackPower, info.AttackIntervalSeconds), true).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddText(columns, DisplayText.Format("ui.performance.defense", info.PhysicalDefense, info.MagicalDefense), true).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddText(columns, DisplayText.Format("ui.performance.movement", info.MovementTilesPerSecond, info.WeaponRange), true).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddText(columns, DisplayText.Format("ui.performance.weakness", info.Weakness), true).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (!string.IsNullOrEmpty(info.AttributeSummary)) AddText(stats, DisplayText.Format("ui.performance.attributes", info.AttributeSummary), true);
        if (!string.IsNullOrEmpty(info.BaseAttributeSummary)) AddText(stats, DisplayText.Format("ui.performance.base_attributes", info.BaseAttributeSummary), true).Modulate = MutedColor;
        var skills = FeedbackSection(parent, DisplayText.Get("ui.section.skills"), "Section_Skill");
        if (info.SkillDetails.Count == 0) AddText(skills, DisplayText.Get("ui.skill.none"), true);
        foreach (var skill in info.SkillDetails)
        {
            var slot = FeedbackSection(skills, DisplayText.Format("ui.skill.slot", skill.Slot, skill.Name), "Skill_" + skill.Slot);
            AddText(slot, skill.Effect, true);
            AddText(slot, DisplayText.Format("ui.skill.details", skill.Target, skill.Range, skill.CooldownSeconds), true).Modulate = MutedColor;
        }
    }

    private void BuildFeedbackLevelChoice()
    {
        var actor = _view!.Adventurers.FirstOrDefault(a => a.Id == _view.EventActorId) ?? SelectedActor();
        var intro = FeedbackSection(_body, DisplayText.Format("ui.level.title", actor?.Name ?? DisplayText.Get("ui.common.adventurer")), "LevelChoiceContext");
        AddText(intro, DisplayText.Get("ui.level.instruction"), true);
        if (actor?.Incapacitated == true && _view.Mode == "invasion")
        {
            AddText(intro, DisplayText.Get("ui.level.incapacitated"), true);
            _reviveButton = AddButton(intro, DisplayText.Format("ui.level.revive", actor.RevivalCost), () => Dispatch(new("Revive", ActorId: actor.Id)));
        }
        var scroll = DecisionScroll(_body); scroll.Name = "LevelChoiceScroll";
        var grid = new GridContainer { Name = "LevelChoiceGrid", Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 18); scroll.AddChild(grid);
        foreach (var choice in _view.Choices)
        {
            var id = choice.Id;
            var card = FeedbackSection(grid, choice.Name, "LevelChoice_" + id);
            card.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.GetParent<Control>().CustomMinimumSize = new Vector2(0, 330);
            var effect = FeedbackSection(card, DisplayText.Get("ui.level.effect"), "Effect_" + id);
            AddText(effect, choice.Description, true).AddThemeFontSizeOverride("font_size", 28);
            var purpose = AddText(card, DisplayText.Get("ui.level." + id + ".purpose"), true); purpose.Name = "LevelChoicePurpose_" + id;
            purpose.SizeFlagsVertical = SizeFlags.ExpandFill;
            var button = AddButton(card, DisplayText.Get("ui.level.choose"), () => Dispatch(new("ChooseLevel", ActorId: actor?.Id, TargetId: id)));
            button.Name = "ChooseLevel_" + id; _levelChoiceButtons.Add(button);
        }
    }

    private static string FeedbackRoman(int rank) => rank switch { 1 => "I", 2 => "II", 3 => "III", _ => "—" };

    private void BuildFeedbackRankEvent()
    {
        var actorId = _view!.EventActorId;
        var actor = _view.Adventurers.FirstOrDefault(a => a.Id == actorId);
        if (actor is not null) _selectedActor = actor.Id;
        var scroll = DecisionScroll(_body); scroll.Name = "GrowthEventScroll";
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(content);
        var context = FeedbackSection(content, DisplayText.Format("ui.growth.title", actor?.Name ?? DisplayText.Get("ui.common.adventurer")), "GrowthEventContext");
        if (_view.NextGrowthEvent is { } growth)
            AddText(context, DisplayText.Format("ui.growth.context", growth.EventRank, growth.CurrentRank, growth.PendingCount, growth.WeaponName), true);
        AddText(context, DisplayText.Get("ui.growth.persistent"), true).Modulate = MutedColor;
        if (actor is not null) BuildFeedbackOwnedBlessings(content, actor);
        if (string.IsNullOrEmpty(_view.EventKind))
        {
            var row = Row(context);
            foreach (var (kind, key) in new[] { ("blessing", "ui.growth.blessing"), ("transfer", "ui.growth.transfer"), ("fusion", "ui.growth.fusion"), ("random", "ui.growth.random") })
            { var eventKind = kind; AddButton(row, DisplayText.Get(key), () => BeginEvent(eventKind)); }
            return;
        }
        if (_view.EventKind == "transfer") { BuildItems(content, true); return; }
        if (_view.EventKind == "random" && _view.Choices.Count == 0)
        { AddButton(context, DisplayText.Get("ui.growth.resolve_random"), () => Dispatch(new("ResolveRandom", ActorId: actorId))); return; }
        if (_view.EventKind == "random")
        {
            AddText(context, DisplayText.Get("ui.growth.random_blessing"), true);
            AddButton(context, DisplayText.Get("ui.growth.decline"), () => Dispatch(new("DeclineRandom", ActorId: actorId)));
        }
        var candidates = FeedbackSection(content, DisplayText.Get(_view.EventKind == "fusion" ? "ui.growth.fusion_candidates" : "ui.blessing.candidates"), "BlessingCandidates");
        if (_view.EventKind == "blessing")
        {
            var controls = Row(candidates);
            AddButton(controls, DisplayText.Get("ui.blessing.discard"), () => Dispatch(new("DiscardBlessings", ActorId: actorId)));
            AddButton(controls, DisplayText.Get("ui.blessing.reroll"), () => Dispatch(new("RerollBlessings", ActorId: actorId)));
        }
        if ((_view.EventKind is "blessing" or "random") && actor is not null && actor.BlessingDetails.Count >= 6)
        {
            AddText(candidates, DisplayText.Get("ui.blessing.full"), true);
            if (!actor.BlessingDetails.Any(b => b.Id == _selectedBlessing)) _selectedBlessing = null;
            var replace = new OptionButton { Name = "BlessingReplacement", FitToLongestItem = false };
            replace.AddItem(DisplayText.Get("ui.blessing.replace_none"));
            foreach (var blessing in actor.BlessingDetails) replace.AddItem(DisplayText.Format("ui.blessing.replace_item", blessing.Name, FeedbackRoman(blessing.Rank)));
            var initial = actor.BlessingDetails.ToList().FindIndex(b => b.Id == _selectedBlessing); replace.Select(initial + 1);
            var selected = AddText(candidates, "", true); selected.Name = "BlessingReplacementSummary";
            void RefreshReplacement() => selected.Text = _selectedBlessing is null ? DisplayText.Get("ui.blessing.replace_unselected")
                : DisplayText.Format("ui.blessing.replace_selected", actor.BlessingDetails.First(b => b.Id == _selectedBlessing).Name);
            replace.ItemSelected += index => { _selectedBlessing = index == 0 ? null : actor.BlessingDetails[(int)index - 1].Id; RefreshReplacement(); };
            candidates.AddChild(replace); RefreshReplacement();
        }
        if (_view.Choices.Count == 0) AddText(candidates, DisplayText.Get("ui.growth.no_candidates"), true);
        var grid = new GridContainer { Name = "BlessingChoiceGrid", Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 14); candidates.AddChild(grid);
        foreach (var choice in _view.Choices)
        {
            var choiceId = choice.Id;
            var option = FeedbackSection(grid, choice.Name, "BlessingChoice_" + choiceId);
            option.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (choice.IsBlessing)
            {
                var kind = choice.CurrentRank == 0 ? DisplayText.Get("ui.blessing.new")
                    : DisplayText.Format("ui.blessing.evolve", FeedbackRoman(choice.CurrentRank), FeedbackRoman(choice.ResultRank));
                AddText(option, kind, true).Modulate = new Color("246e9e");
            }
            AddText(option, choice.Description, true).SizeFlagsVertical = SizeFlags.ExpandFill;
            var button = AddButton(option, DisplayText.Get(_view.EventKind == "fusion" ? "ui.growth.fuse" : choice.CurrentRank > 0 ? "ui.blessing.choose_evolve" : "ui.blessing.choose_new"),
                () => Dispatch(new(_view.EventKind == "fusion" ? "Fuse" : "ChooseBlessing", ActorId: actorId, TargetId: choiceId, Value: _selectedBlessing)));
            button.Name = "ChooseBlessing_" + choiceId;
        }
    }

    private static void BuildFeedbackOwnedBlessings(Container parent, AdventurerView actor)
    {
        var owned = FeedbackSection(parent, DisplayText.Format("ui.blessing.current", actor.BlessingDetails.Count), "OwnedBlessings");
        var grid = new GridContainer { Name = "CurrentBlessingGrid", Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 10); owned.AddChild(grid);
        for (var i = 0; i < 6; i++)
        {
            var blessing = actor.BlessingDetails.ElementAtOrDefault(i);
            var frame = new PanelContainer { Name = "CurrentBlessingSlot_" + i, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            frame.AddThemeStyleboxOverride("panel", CivicSurface("f1f6fa", "bdcede", 9)); grid.AddChild(frame);
            var slot = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; slot.AddThemeConstantOverride("separation", 4); frame.AddChild(slot);
            AddText(slot, blessing is null ? DisplayText.Format("ui.blessing.slot", i + 1)
                : DisplayText.Format("ui.blessing.owned_title", i + 1, blessing.Name, FeedbackRoman(blessing.Rank)), true).AddThemeFontSizeOverride("font_size", 22);
            AddText(slot, blessing?.Description ?? DisplayText.Get("ui.blessing.empty_slot"), true).AddThemeFontSizeOverride("font_size", 21);
        }
    }

    private void BuildFeedbackResult(bool victory)
    {
        var result = _view!.ResultSummary;
        var overview = FeedbackSection(_body, DisplayText.Format("ui.result.title", FeedbackMode(_view.Mode), DisplayText.Get(victory ? "ui.result.victory" : "ui.result.defeat")), "ResultOverview");
        AddText(overview, DisplayText.Get(victory ? "ui.result.return_victory" : "ui.result.return_defeat"), true);
        if (result is not null)
            AddText(overview, DisplayText.Format("ui.result.reward", result.GuildResourceGain, result.RankExperienceGain), true);
        var actions = Row(overview);
        if (!victory) _retryButton = AddButton(actions, DisplayText.Get("ui.result.retry"), () => Dispatch(new("Retry")));
        _returnButton = AddButton(actions, DisplayText.Get("ui.result.return"), () => Dispatch(new("ReturnToGuild")));
        _returnButton.Name = "ResultReturn";
        var scroll = DecisionScroll(_body); scroll.Name = "ResultScroll";
        var grid = new GridContainer { Name = "ResultGrid", Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 12); scroll.AddChild(grid);
        if (result is null) { AddText(grid, DisplayText.Get("ui.result.unavailable"), true); return; }
        foreach (var actor in result.Adventurers)
        {
            var card = FeedbackSection(grid, actor.Name, "Result_" + actor.ActorId);
            card.AddThemeConstantOverride("separation", 5);
            card.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.GetParent<Control>().CustomMinimumSize = new Vector2(0, 250);
            var state = DisplayText.Get(actor.WasDispatched ? "economy.ui.result_dispatched" : !actor.WasDeployed ? "ui.result.not_deployed" : actor.Incapacitated ? "ui.result.incapacitated" : "ui.result.deployed");
            var activity = AddText(card, state, true); activity.Name = "ResultActivity_" + actor.ActorId;
            activity.Modulate = actor.Incapacitated ? new Color("ac342d") : MutedColor;
            var rank = AddText(card, actor.RankBefore == actor.RankAfter ? DisplayText.Format("ui.result.rank_same", actor.RankAfter)
                : DisplayText.Format("ui.result.rank_up", actor.RankBefore, actor.RankAfter), true);
            if (actor.RankBefore != actor.RankAfter) { rank.Modulate = new Color("236e9c"); rank.AddThemeFontSizeOverride("font_size", 27); }
            AddText(card, DisplayText.Format("ui.result.end_hp", actor.EndHp, actor.EndMaxHp), true);
            var hp = double.Parse(actor.EndHp, CultureInfo.InvariantCulture);
            var maximum = double.Parse(actor.EndMaxHp, CultureInfo.InvariantCulture);
            var ratio = Math.Clamp(hp / Math.Max(1, maximum), 0, 1);
            var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = ratio, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12) };
            bar.AddThemeStyleboxOverride("fill", CivicSurface(ratio <= .25 ? "bf5b4a" : ratio < .6 ? "c5963e" : "65a27d", "ffffff00", 0)); card.AddChild(bar);
            AddText(card, DisplayText.Format("ui.result.damage", Math.Max(0, maximum - hp).ToString("0.##", CultureInfo.InvariantCulture), ((1 - ratio) * 100).ToString("0", CultureInfo.InvariantCulture)), true).Modulate = ratio < .6 ? new Color("a04a32") : MutedColor;
            AddText(card, DisplayText.Format("ui.result.return_hp", actor.ReturnHp, actor.ReturnMaxHp), true);
            AddText(card, DisplayText.Format("ui.result.weakness", actor.WeaknessAfter), true).Modulate = MutedColor;
            foreach (var label in card.GetChildren().OfType<Label>())
                label.AddThemeFontSizeOverride("font_size", label == rank && actor.RankBefore != actor.RankAfter ? 25 : 21);
        }
    }
}
