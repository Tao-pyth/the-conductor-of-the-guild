namespace Guild.Application;

/// <summary>Read-only, presentation-ready values. No generated content or saved state is exposed.</summary>
public sealed record AdventurerPerformanceView(string Context, string AttackPower,
    string PhysicalDefense, string MagicalDefense, string AttackIntervalSeconds,
    string MovementTilesPerSecond, string WeaponRange, int Weakness,
    IReadOnlyList<SkillDetailView> SkillDetails)
{
    public string AttributeSummary { get; init; } = "";
    public string BaseAttributeSummary { get; init; } = "";
}

public sealed record SkillDetailView(string Slot, string SkillId, string Name, string Target,
    string Effect, string Range, string CooldownSeconds, bool IsUltimate);

public sealed record PreparationInfoView(string DeploymentCost, bool CanDeploy,
    string? DeployUnavailableReason, string RevivalCost, bool CanRevive,
    string? ReviveUnavailableReason, string CurrentLevelXp, string RequiredLevelXp,
    string MissingLevelXp, string? PurchaseCost, int? PurchaseTargetLevel,
    bool CanPurchaseLevel, string? PurchaseUnavailableReason, bool IsLevelCap);

public sealed record SkillRangeView(string Slot, string Name, string Target, double Radius,
    bool IsHealing);

public sealed record RangeGuideView(bool Available, string Mode, string? RuleSetId,
    bool IsLegacyDefense, double ActualX, double ActualY, double AnchorX, double AnchorY,
    double WatchRadius, double ActionRadius, double AttackRadius,
    IReadOnlyList<SkillRangeView> SkillRadii, string Explanation);

public sealed record NextEncounterView(string Mode, int Number, int Total, string Name,
    int EnemyCount, string? EnemyHp, bool? AttacksAdventurers, int GuardCount,
    int TurretCount, int GateCount, int CoreCount, string? TimeLimitSeconds);
