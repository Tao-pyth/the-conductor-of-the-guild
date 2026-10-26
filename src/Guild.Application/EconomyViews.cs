namespace Guild.Application;

public sealed record MaterialStockView(string Id, string Name, string Quantity);
public sealed record FactionRelationView(string Id, string Name, int Trust, string State);
public sealed record DispatchMissionView(string Id, string Name, string Description,
    string RecommendedRank, int RecommendedMembers, string WeaponSummary, string BlessingSummary);
public sealed record DeliveryView(string Id, string Name, string Kind, string RequiredMaterials,
    string RewardDescription, bool CanComplete, string UnavailableReason);
public sealed record DispatchPreviewView(string MissionId, int SuccessPercent, string RankPoints,
    string WeaponPoints, string BlessingPoints, string SuccessRewards, string FailureRewards,
    bool CanReserve, string UnavailableReason);
public sealed record DispatchStatusView(string MissionId, string MissionName,
    IReadOnlyList<string> ActorIds, IReadOnlyList<string> ActorNames, bool Active,
    int SuccessPercent, string SuccessRewards, string FailureRewards);
public sealed record GuildEconomyView
{
    public IReadOnlyList<MaterialStockView> Inventory { get; init; } = [];
    public IReadOnlyList<FactionRelationView> Factions { get; init; } = [];
    public IReadOnlyList<DispatchMissionView> Missions { get; init; } = [];
    public IReadOnlyList<DeliveryView> Deliveries { get; init; } = [];
    public DispatchStatusView? Dispatch { get; init; }
    public string LastReport { get; init; } = "";
    public string Contribution { get; init; } = "0";
    public bool LegacyMatch { get; init; }
}
