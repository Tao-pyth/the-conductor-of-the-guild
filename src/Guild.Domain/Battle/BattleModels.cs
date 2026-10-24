using System.Numerics;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;

namespace Guild.Domain.Battle;

public sealed record BattleStats(BigInteger Str, BigInteger Vit, BigInteger Dex, BigInteger Agi, BigInteger Int)
{
    public static BattleStats Standard => new(10, 10, 10, 10, 10);
}

public sealed record BattleUnitSetup
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public BattleStats Stats { get; init; } = BattleStats.Standard;
    public string WeaponTemplateId { get; init; } = "";
    public Rational? BasePower { get; init; }
    public Rational? AttackIntervalMs { get; init; }
    public int? RangeCenti { get; init; }
    public List<string>? SkillIds { get; init; }
    public BigInteger? CurrentHp { get; init; }
}

public sealed record BattleSetup
{
    public string MatchId { get; init; } = "";
    public string AttemptId { get; init; } = "";
    public ulong Seed { get; init; } = 42;
    public ContentBundle Content { get; init; } = new();
    public List<BattleUnitSetup> Units { get; init; } = [];
    public int DefenseHp { get; init; } = 20;
    public BigInteger InitialResources { get; init; } = 1000;
    public string StageId { get; init; } = "tech-first-defense";
}

public sealed class BattleSnapshot
{
    public int SimulationVersion { get; set; } = BattleSimulation.SimulationVersion;
    public BattleSetup Setup { get; set; } = new();
    public long TimeMs { get; set; }
    public int Wave { get; set; }
    public BigInteger Resources { get; set; }
    public int DefenseHp { get; set; }
    public bool Running { get; set; }
    public bool WaveCompleted { get; set; }
    public bool Defeated { get; set; }
    public bool Victory { get; set; }
    public int SpawnedInWave { get; set; }
    public Rational NextSpawnAtMs { get; set; }
    public long NextEntitySequence { get; set; } = 1;
    public long NextCastSequence { get; set; } = 1;
    public long NextProjectileSequence { get; set; } = 1;
    public long NextEventSequence { get; set; } = 1;
    public int LastExperienceAwardedWave { get; set; }
    public PcgState Rng { get; set; } = new(0, 1);
    public List<CombatantState> Units { get; set; } = [];
    public List<ProjectileState> Projectiles { get; set; } = [];
    public HashSet<string> RefundGuards { get; set; } = [];
}

public sealed class CombatantState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsEnemy { get; set; }
    public long SpawnSequence { get; set; }
    public BattleStats Stats { get; set; } = BattleStats.Standard;
    public string WeaponType { get; set; } = "sword";
    public string WeaponTemplateId { get; set; } = "";
    public Rational BasePower { get; set; } = 10;
    public Rational BaseAttackIntervalMs { get; set; } = 1000;
    public Rational? PreparedMaxHp { get; set; }
    public Rational? PreparedAttackPower { get; set; }
    public Rational? PreparedAttackIntervalMs { get; set; }
    public Rational? PreparedMoveCentiPerSecond { get; set; }
    public int RangeCenti { get; set; } = 100;
    public int ProjectileSpeedCentiPerSecond { get; set; }
    public Rational EnemySpeedCentiPerSecond { get; set; }
    public bool AttacksAdventurers { get; set; }
    public BigInteger Hp { get; set; }
    public BigInteger MaxHp { get; set; }
    public Rational PhysicalDefense { get; set; }
    public Rational MagicalDefense { get; set; }
    public int Level { get; set; } = 1;
    public int EnemyLevel { get; set; } = 1;
    public BigInteger LevelExperience { get; set; }
    public int PendingLevelChoices { get; set; }
    public bool Deployed { get; set; }
    public bool EverDeployed { get; set; }
    public int RevivalCount { get; set; }
    public Rational X { get; set; }
    public Rational Y { get; set; }
    public int AnchorX { get; set; }
    public int AnchorY { get; set; }
    public int PathIndex { get; set; }
    public string TargetPreset { get; set; } = "arrival";
    public string? CurrentTargetId { get; set; }
    public Rational AttackReadyAtMs { get; set; }
    public Rational HardLockUntilMs { get; set; }
    public PendingAction? Action { get; set; }
    public Rational AttackMultiplier { get; set; } = Rational.One;
    public Rational IntervalMultiplier { get; set; } = Rational.One;
    public Rational MoveMultiplier { get; set; } = Rational.One;
    public Rational RangeMultiplier { get; set; } = Rational.One;
    public Rational DefenseMultiplier { get; set; } = Rational.One;
    public Rational MaxHpMultiplier { get; set; } = Rational.One;
    public Rational LevelAttackMultiplier { get; set; } = Rational.One;
    public Rational LevelIntervalMultiplier { get; set; } = Rational.One;
    public Rational LevelMoveMultiplier { get; set; } = Rational.One;
    public Rational LevelRangeMultiplier { get; set; } = Rational.One;
    public Rational LevelDefenseMultiplier { get; set; } = Rational.One;
    public Rational LevelMaxHpMultiplier { get; set; } = Rational.One;
    public List<SkillSlotState> Skills { get; set; } = [];
    public List<StatusState> Statuses { get; set; } = [];
    public List<SkillModifier> SkillModifiers { get; set; } = [];
}

public sealed class SkillSlotState
{
    public string Slot { get; set; } = "S1";
    public string SkillId { get; set; } = "";
    public Rational Progress { get; set; }
    public Rational EffectiveCooldownMs { get; set; }
    public Rational ReadySinceMs { get; set; }
    public CooldownModifiers Cooldown { get; set; } = new();
}

public sealed record CooldownModifiers
{
    public Rational FixedReductionMs { get; init; }
    public Rational FixedExtensionMs { get; init; }
    public Rational ShorteningRate { get; init; }
    public Rational ExtensionRate { get; init; }
    public Rational RecoveryDelta { get; init; }
}

public sealed record SkillModifier
{
    public string Id { get; init; } = "";
    public string? SkillId { get; init; }
    public string? Slot { get; init; }
    public Rational PowerMultiplier { get; init; } = Rational.One;
    public bool AttackOnly { get; init; }
}

public sealed class PendingAction
{
    public string Kind { get; set; } = "attack";
    public string CastId { get; set; } = "";
    public string? SkillId { get; set; }
    public string? Slot { get; set; }
    public List<string> TargetIds { get; set; } = [];
    public Rational ReleaseAtMs { get; set; }
    public Rational AttackSnapshot { get; set; }
    public Rational HealingSnapshot { get; set; }
    public Rational CritChance { get; set; }
    public Rational PowerMultiplier { get; set; } = Rational.One;
    public Rational HealingPowerMultiplier { get; set; } = Rational.One;
    public HashSet<string> AppliedComponents { get; set; } = [];
}

public sealed class StatusState
{
    public string Kind { get; set; } = "";
    public Rational Strength { get; set; }
    public Rational ExpiresAtMs { get; set; }
    public Rational NextTickAtMs { get; set; }
    public Rational TickIntervalMs { get; set; }
    public string SourceId { get; set; } = "";
    public string CastId { get; set; } = "";
}

public sealed class ProjectileState
{
    public string Id { get; set; } = "";
    public long Sequence { get; set; }
    public string SourceId { get; set; } = "";
    public string CastId { get; set; } = "";
    public string ComponentId { get; set; } = "";
    public bool IsEnemy { get; set; }
    public bool Magical { get; set; }
    public Rational X { get; set; }
    public Rational Y { get; set; }
    public Rational VelocityX { get; set; }
    public Rational VelocityY { get; set; }
    public Rational Speed { get; set; }
    public Rational RemainingRange { get; set; }
    public Rational ExpiresAtMs { get; set; }
    public Rational AttackSnapshot { get; set; }
    public Rational CritChance { get; set; }
}

public sealed record BattleEvent(long Sequence, long TimeMs, string Kind, string SourceId,
    string TargetId, string Text, BigInteger Amount, string CastId = "");
