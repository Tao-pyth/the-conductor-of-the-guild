using System.Numerics;
using System.Text.Json.Serialization;
using Guild.Domain.Numerics;

namespace Guild.Domain.Definitions;

public enum ParameterValueType { Integer, Rational, Probability, Distance, Duration, Rank }
public enum ModifierStage { Fixed, Additive, Multiplicative }

public sealed record ParameterDefinition
{
    public string Id { get; init; } = "";
    public string Unit { get; init; } = "";
    public ParameterValueType Type { get; init; } = ParameterValueType.Rational;
    public Rational Minimum { get; init; } = Rational.Zero;
    public Rational? Maximum { get; init; }
}

public sealed record ModifierDefinition
{
    public string Id { get; init; } = "";
    public string ParameterId { get; init; } = "";
    public ModifierStage Stage { get; init; }
    public Rational Amount { get; init; }
    public string? SkillId { get; init; }
    public string? Slot { get; init; }
    public string? Condition { get; init; }
}

public sealed record EffectDefinition
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "damage";
    public Rational Amount { get; init; } = Rational.One;
    public string? Status { get; init; }
    public int DurationMs { get; init; }
    public int TickMs { get; init; }
    public bool Derived { get; init; }
}

public sealed record SkillDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsUltimate { get; init; }
    public Rational CooldownMs { get; init; }
    public int CastMs { get; init; }
    public int RecoveryMs { get; init; } = 250;
    public int Priority { get; init; }
    public int RangeCenti { get; init; } = 300;
    public Rational? GenerationCost { get; init; }
    public IReadOnlyList<EffectDefinition> Effects { get; init; } = [];
}

public sealed record WeaponTemplateDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string WeaponType { get; init; } = "sword";
    public Rational BasePower { get; init; }
    public Rational AttackIntervalMs { get; init; } = 1000;
    public int RangeCenti { get; init; } = 150;
    public int ProjectileSpeedCentiPerSecond { get; init; }
    public IReadOnlyList<string> SkillIds { get; init; } = [];
    public IReadOnlyList<string> CompatibleSkillIds { get; init; } = [];
    public Rational Budget { get; init; } = 100;
}

public sealed record BlessingDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Acquisition { get; init; } = "normal";
    public int Rarity { get; init; } = 1;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<ModifierDefinition> Modifiers { get; init; } = [];
}

public sealed record RecipeDefinition
{
    public string Id { get; init; } = "";
    public string FirstBlessingId { get; init; } = "";
    public string SecondBlessingId { get; init; } = "";
    public string ResultBlessingId { get; init; } = "";
}

public sealed record GridPoint(int X, int Y);

public sealed record WaveDefinition
{
    public int Number { get; init; }
    public int EnemyCount { get; init; }
    public int EnemyLevel { get; init; } = 1;
    public Rational EnemyHp { get; init; }
    public Rational EnemyDefense { get; init; }
    public Rational EnemyPower { get; init; }
    public int EnemySpeedCentiPerSecond { get; init; } = 100;
    public int SpawnIntervalMs { get; init; } = 1000;
    public bool AttacksAdventurers { get; init; }
}

public sealed record StageDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public BigInteger Rank { get; init; } = BigInteger.One;
    public BigInteger RewardRank { get; init; } = BigInteger.One;
    public BigInteger InitialResources { get; init; } = 1000;
    public int DefenseHp { get; init; } = 20;
    public int MapWidth { get; init; } = 16;
    public int MapHeight { get; init; } = 10;
    public IReadOnlyList<GridPoint> Path { get; init; } = [];
    public IReadOnlyList<WaveDefinition> Waves { get; init; } = [];
    // Absent fields keep legacy normalized content hashes byte-for-byte stable.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SectionDefinition>? Sections { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GridPoint>? DeploymentCells { get; init; }
}

public sealed record SectionDefinition
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public int TimeLimitMs { get; init; } = 300000;
    public IReadOnlyList<EnemyPlacementDefinition> Enemies { get; init; } = [];
}

public sealed record EnemyPlacementDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Role { get; init; } = "guard";
    public int X { get; init; }
    public int Y { get; init; }
    public int Level { get; init; } = 1;
    public Rational Hp { get; init; }
    public Rational Power { get; init; }
    public Rational Defense { get; init; }
    public int RangeCenti { get; init; } = 150;
    public int MoveSpeedCentiPerSecond { get; init; } = 200;
    public int AttackIntervalMs { get; init; } = 1000;
    public int ProjectileSpeedCentiPerSecond { get; init; }
}
