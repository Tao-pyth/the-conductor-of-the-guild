using Guild.Domain.Definitions;
using System.Text.Json.Serialization;

namespace Guild.Application;

public sealed record GameCommand(string Type, string? ActorId = null, string? TargetId = null,
    int X = 0, int Y = 0, string? Value = null, string? CommandId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Contribution { get; init; }
}
public sealed record CommandResult(bool Success, string Message);
public sealed record AdventurerView(string Id, string Name, string Rank, int Level, string Hp,
    string MaxHp, string Weapon, bool Deployed, bool Incapacitated, int X, int Y,
    IReadOnlyList<string> Blessings, IReadOnlyList<SkillView> Skills, string TargetPreset = "arrival")
{
    public bool InParty { get; init; } = true;
    public bool IsDispatched { get; init; }
    public string Activity { get; init; } = DisplayText.Get("app.activity.idle");
    public string ActivityCode { get; init; } = "idle";
    public string WeaponType { get; init; } = "";
    public string DeploymentCost { get; init; } = "0";
    public string PlacementCost => DeploymentCost;
    public string RevivalCost { get; init; } = "0";
    public string LevelPurchaseCost { get; init; } = "0";
    public AdventurerPerformanceView? Performance { get; init; }
    public PreparationInfoView? PreparationInfo { get; init; }
    public RangeGuideView? RangeGuide { get; init; }
    public IReadOnlyList<BlessingDetailView> BlessingDetails { get; init; } = [];
}
public sealed record SkillView(string Slot, string Name, double Progress);
public sealed record MapEntityView(string Id, string Kind, double X, double Y, double HpRatio);
public sealed record ChoiceView(string Id, string Name, string Description)
{
    public bool IsBlessing { get; init; }
    public int CurrentRank { get; init; }
    public int ResultRank { get; init; }
}
public sealed record ItemView(string Id, string Name, string OwnerId, bool Equipped);
public sealed record HistoryView(long Sequence, string Text);
public sealed record StageView(string Id, string Name, string Mode, int TotalSections, string RewardRank,
    long TimeLimitMs, string RewardSummary)
{
    public int Sections => TotalSections;
    public string Rank { get; init; } = "1";
    public string InitialResources { get; init; } = "0";
}
public sealed partial record GameView(string Screen, string Status, string GuildResources, string MatchResources,
    int Wave, int TotalWaves, int DefenseHp, int Speed, bool Paused, int OwnedCapacity,
    int PendingEvents, string? EventActorId, string? EventKind,
    IReadOnlyList<AdventurerView> Adventurers, IReadOnlyList<MapEntityView> Entities,
    IReadOnlyList<ChoiceView> Choices, IReadOnlyList<ItemView> Items,
    IReadOnlyList<HistoryView> History, int MapWidth = 16, int MapHeight = 10)
{
    public string Mode { get; init; } = "defense";
    public string StageId { get; init; } = "";
    public string StageName { get; init; } = "";
    public IReadOnlyList<StageView> Stages { get; init; } = [];
    public IReadOnlyList<string> PartyIds { get; init; } = [];
    public long RemainingTimeMs { get; init; }
    public IReadOnlyList<GridPoint> Path { get; init; } = [];
    public IReadOnlyList<GridPoint> DeploymentCells { get; init; } = [];
    public string DefeatReason { get; init; } = "";
    public int RemainingEnemies { get; init; }
    public string ObjectiveHp { get; init; } = "0";
    public string ObjectiveMaxHp { get; init; } = "0";
    public NextEncounterView? NextEncounter { get; init; }
    public MatchResultView? ResultSummary { get; init; }
    public GuildEconomyView Economy { get; init; } = new();
}

public sealed record BlessingDetailView(string Id, string Name, int Rank, string Description);

public sealed record MatchResultView(string MatchId, string Mode, bool Victory, int Wave, int TotalWaves,
    string GuildResourceGain, string RankExperienceGain, IReadOnlyList<AdventurerResultView> Adventurers);

public sealed record AdventurerResultView(string ActorId, string Name, string WeaponType, bool WasDeployed,
    bool Incapacitated, string StartHp, string EndHp, string EndMaxHp, string ReturnHp, string ReturnMaxHp,
    string RankBefore, string RankAfter, int WeaknessAfter)
{
    public bool WasDispatched { get; init; }
}
