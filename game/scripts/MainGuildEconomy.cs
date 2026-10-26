using Godot;
using Guild.Application;
using System.Globalization;
using BigInteger = System.Numerics.BigInteger;

namespace Guild.Game;

public partial class Main
{
    private string? _economyMissionId;
    private readonly HashSet<string> _economyDispatchIds = new(StringComparer.Ordinal);
    private string _economyContribution = "0";
    private Label? _economyContributionSummary;
    private Label? _economyDispatchPreview;
    private Button? _economyReserveButton;

    private string EconomyDispatchActorStatus() => DisplayText.Get(_view?.Economy.Dispatch is { Active: false }
        ? "economy.ui.reserved_badge" : "economy.ui.dispatched_badge");

    private void BuildEconomyHomeReport(Container parent)
    {
        var report = _view?.Economy.LastReport;
        if (string.IsNullOrWhiteSpace(report)) return;
        var row = Row(parent);
        var summary = AddText(row, DisplayText.Get("economy.ui.latest_report"));
        summary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddButton(row, DisplayText.Get("economy.ui.read_report"), () => { _activeTab = "deliveries"; RenderScreen(); }).Name = "HomeEconomyReport";
    }

    private void BuildGuildDispatch(Container parent)
    {
        if (_view is null) return;
        var economy = _view.Economy;
        var status = economy.Dispatch;
        if (_economyMissionId is null)
        {
            _economyMissionId = status?.MissionId ?? economy.Missions.FirstOrDefault()?.Id;
            if (status is not null) foreach (var id in status.ActorIds) _economyDispatchIds.Add(id);
        }
        _economyDispatchIds.RemoveWhere(id => !_view.Adventurers.Any(actor => actor.Id == id));
        var scroll = DecisionScroll(parent); scroll.Name = "EconomyDispatchScroll";
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 14); scroll.AddChild(content);
        var intro = FeedbackSection(content, DisplayText.Get("economy.ui.dispatch_tab"), "EconomyDispatchOverview");
        AddText(intro, DisplayText.Get("economy.ui.dispatch_intro"), true).Modulate = MutedColor;
        if (status is not null)
        {
            var state = status.Active ? DisplayText.Get("economy.ui.dispatch_active") : DisplayText.Get("economy.ui.dispatch_reserved");
            AddText(intro, DisplayText.Format("economy.ui.dispatch_status", state, status.MissionName, string.Join("、", status.ActorNames), status.SuccessPercent), true);
            AddText(intro, DisplayText.Format("economy.ui.dispatch_rewards", status.SuccessRewards, status.FailureRewards), true);
            if (!status.Active)
                AddButton(intro, DisplayText.Get("economy.ui.dispatch_cancel"), () =>
                {
                    _economyDispatchIds.Clear();
                    Dispatch(new("CancelDispatch"));
                }).Name = "EconomyCancelDispatch";
        }
        var split = new HBoxContainer { Name = "EconomyDispatchContent", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 16); content.AddChild(split);
        var missionSection = FeedbackSection(split, DisplayText.Get("economy.ui.mission_heading"), "EconomyMissionSection");
        missionSection.GetParent<Control>().CustomMinimumSize = new Vector2(550, 0);
        var missionPicker = new OptionButton { Name = "EconomyMissionPicker", FitToLongestItem = false, Disabled = status?.Active == true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        for (var index = 0; index < economy.Missions.Count; index++)
        {
            missionPicker.AddItem(economy.Missions[index].Name);
            if (economy.Missions[index].Id == _economyMissionId) missionPicker.Select(index);
        }
        missionPicker.ItemSelected += index => { _economyMissionId = economy.Missions[(int)index].Id; RenderScreen(); };
        missionSection.AddChild(missionPicker);
        var mission = economy.Missions.FirstOrDefault(item => item.Id == _economyMissionId);
        if (mission is not null)
        {
            AddText(missionSection, mission.Description, true);
            AddText(missionSection, DisplayText.Format("economy.ui.mission_rank", mission.RecommendedRank, mission.RecommendedMembers), true);
            AddText(missionSection, DisplayText.Format("economy.ui.mission_weapons", mission.WeaponSummary), true);
            AddText(missionSection, DisplayText.Format("economy.ui.mission_blessings", mission.BlessingSummary), true);
        }
        var previewSection = FeedbackSection(missionSection, DisplayText.Get("economy.ui.preview_heading"), "EconomyPreviewSection");
        _economyDispatchPreview = AddText(previewSection, "", true);
        _economyDispatchPreview.Name = "EconomyDispatchPreview";
        _economyReserveButton = AddButton(previewSection,
            DisplayText.Get(status is { Active: false } ? "economy.ui.dispatch_update" : "economy.ui.dispatch_reserve"),
            () => Dispatch(new("ReserveDispatch", TargetId: _economyMissionId, Value: string.Join(',', _economyDispatchIds.Order(StringComparer.Ordinal)))));
        _economyReserveButton.Name = "EconomyReserveDispatch";
        var members = FeedbackSection(split, DisplayText.Get("economy.ui.members_heading"), "EconomyDispatchRoster");
        foreach (var actor in _view.Adventurers)
        {
            var id = actor.Id;
            var check = new CheckBox
            {
                Name = "EconomyMember_" + id, ButtonPressed = _economyDispatchIds.Contains(id), Disabled = status?.Active == true,
                Text = DisplayText.Format("economy.ui.member", actor.Name, actor.Rank, actor.Weapon),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true,
                TooltipText = DisplayText.Format("economy.ui.member", actor.Name, actor.Rank, actor.Weapon)
                    + "\n" + string.Join("、", actor.Blessings)
            };
            check.Toggled += selected =>
            {
                if (selected && _economyDispatchIds.Count >= 6)
                {
                    check.SetPressedNoSignal(false); _message.Text = DisplayText.Get("economy.ui.member_limit"); return;
                }
                if (selected) _economyDispatchIds.Add(id); else _economyDispatchIds.Remove(id);
                RefreshEconomyDispatchPreview();
            };
            members.AddChild(check);
        }
        AddText(members, DisplayText.Get("economy.ui.member_note"), true).Modulate = MutedColor;
        RefreshEconomyDispatchPreview();
    }

    private void RefreshEconomyDispatchPreview()
    {
        if (_controller is null || _view is null || _economyDispatchPreview is null || _economyReserveButton is null) return;
        var status = _view.Economy.Dispatch;
        if (status?.Active == true)
        {
            _economyDispatchPreview.Text = DisplayText.Format("economy.ui.active_preview", status.SuccessPercent, status.SuccessRewards, status.FailureRewards);
            _economyReserveButton.Disabled = true;
            _economyReserveButton.TooltipText = DisplayText.Get("economy.ui.dispatched_unavailable");
            return;
        }
        if (_economyMissionId is null) { _economyReserveButton.Disabled = true; return; }
        var preview = _controller.PreviewDispatch(_economyMissionId, _economyDispatchIds.Order(StringComparer.Ordinal).ToArray());
        _economyDispatchPreview.Text = DisplayText.Format("economy.ui.preview", _economyDispatchIds.Count,
            preview.SuccessPercent, preview.RankPoints, preview.WeaponPoints, preview.BlessingPoints,
            preview.SuccessRewards, preview.FailureRewards);
        if (!preview.CanReserve) _economyDispatchPreview.Text += "\n" + preview.UnavailableReason;
        _economyReserveButton.Disabled = !preview.CanReserve;
        _economyReserveButton.TooltipText = preview.UnavailableReason;
    }

    private void BuildGuildDeliveries(Container parent)
    {
        if (_view is null) return;
        var economy = _view.Economy;
        var scroll = DecisionScroll(parent); scroll.Name = "EconomyDeliveryScroll";
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 14); scroll.AddChild(content);
        var inventory = FeedbackSection(content, DisplayText.Get("economy.ui.inventory_heading"), "EconomyInventory");
        AddText(inventory, string.Join("  ／  ", economy.Inventory.Select(item => DisplayText.Format("economy.ui.stock", item.Name, item.Quantity))), true);
        AddText(inventory, DisplayText.Get("economy.ui.inventory_note"), true).Modulate = MutedColor;
        var relations = FeedbackSection(content, DisplayText.Get("economy.ui.faction_heading"), "EconomyFactions");
        AddText(relations, string.Join("  ／  ", economy.Factions.Select(faction => DisplayText.Format("economy.ui.faction", faction.Name, faction.Trust, faction.State))), true);
        AddText(relations, DisplayText.Get("economy.ui.faction_note"), true).Modulate = MutedColor;
        if (!string.IsNullOrWhiteSpace(economy.LastReport))
        {
            var report = FeedbackSection(content, DisplayText.Get("economy.ui.latest_report"), "EconomyLastReport");
            AddText(report, economy.LastReport, true);
        }
        foreach (var kind in economy.Deliveries.Select(item => item.Kind).Distinct(StringComparer.Ordinal))
        {
            var heading = kind switch
            {
                "relation" => "economy.ui.kind_relation", "equipment" => "economy.ui.kind_equipment",
                "gold" or "money" => "economy.ui.kind_gold", _ => "economy.ui.delivery_tab"
            };
            var group = FeedbackSection(content, DisplayText.Get(heading), "EconomyDeliveryKind_" + kind);
            var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 14); group.AddChild(grid);
            foreach (var delivery in economy.Deliveries.Where(item => item.Kind == kind))
            {
                var id = delivery.Id;
                var card = FeedbackSection(grid, delivery.Name, "EconomyDelivery_" + id);
                AddText(card, DisplayText.Format("economy.ui.delivery_materials", delivery.RequiredMaterials), true);
                AddText(card, DisplayText.Format("economy.ui.delivery_reward", delivery.RewardDescription), true);
                var button = AddButton(card, DisplayText.Get("economy.ui.deliver"), () => Dispatch(new("Deliver", TargetId: id)));
                button.Name = "EconomyDeliver_" + id; button.Disabled = !delivery.CanComplete;
                button.TooltipText = delivery.UnavailableReason;
                if (!delivery.CanComplete) AddText(card, delivery.UnavailableReason, true).Modulate = MutedColor;
            }
        }
    }

    private void BuildEconomyContribution(Container parent)
    {
        var row = Row(parent); row.Name = "EconomyContributionRow";
        AddText(row, DisplayText.Get("economy.ui.contribution_label"));
        var input = new LineEdit { Name = "EconomyContributionInput", Text = _economyContribution,
            PlaceholderText = "0", CustomMinimumSize = new Vector2(180, 42), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(input);
        _economyContributionSummary = AddText(row, "", true);
        _economyContributionSummary.Name = "EconomyContributionSummary";
        _economyContributionSummary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        input.TextChanged += text => { _economyContribution = text.Trim(); RefreshEconomyContribution(); RefreshExpeditionCount(); };
        AddText(parent, DisplayText.Get("economy.ui.contribution_note"), true).Modulate = MutedColor;
        RefreshEconomyContribution();
    }

    private bool ValidEconomyContribution() => _view is not null
        && BigInteger.TryParse(_economyContribution, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
        && BigInteger.TryParse(_view.GuildResources, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wallet)
        && amount >= 0 && amount <= wallet;

    private void RefreshEconomyContribution()
    {
        if (_economyContributionSummary is null || _view is null) return;
        if (!ValidEconomyContribution())
        {
            _economyContributionSummary.Text = DisplayText.Get("economy.ui.contribution_invalid");
            _economyContributionSummary.Modulate = new Color("a13c30"); return;
        }
        var amount = BigInteger.Parse(_economyContribution, CultureInfo.InvariantCulture);
        var wallet = BigInteger.Parse(_view.GuildResources, CultureInfo.InvariantCulture);
        _economyContributionSummary.Text = DisplayText.Format("economy.ui.contribution_preview", 1000 + amount, wallet - amount);
        _economyContributionSummary.Modulate = MutedColor;
    }
}
