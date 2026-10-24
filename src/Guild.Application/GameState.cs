using System.Numerics;
using Guild.Domain.Battle;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;

namespace Guild.Application;

public sealed class PlayerProfile
{
    public int FormatVersion { get; set; } = 1;
    public BigInteger Resources { get; set; }
    public int Capacity { get; set; } = 10;
    public int ExpansionCount { get; set; }
    public int RerollTickets { get; set; }
    public int PurificationDrops { get; set; }
    public bool FirstClear { get; set; }
    public long NextEventSequence { get; set; } = 1;
    public PcgState GrowthRng { get; set; } = new(0, 1);
    public PcgState GenerationRng { get; set; } = new(0, 3);
    public List<AdventurerState> Adventurers { get; set; } = [];
    public List<WeaponState> Weapons { get; set; } = [];
    public List<RankEventState> PendingEvents { get; set; } = [];
    public HashSet<string> DiscoveredRecipes { get; set; } = [];
}

public sealed class AdventurerState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public BigInteger Rank { get; set; } = BigInteger.One;
    public BigInteger Experience { get; set; }
    public BattleStats Stats { get; set; } = BattleStats.Standard;
    public BattleStats Growth { get; set; } = new(0, 0, 0, 0, 0);
    public BigInteger Hp { get; set; } = 200;
    public int WeaknessStacks { get; set; }
    public string WeaponId { get; set; } = "";
    public int DiscardBonus { get; set; }
    public List<OwnedBlessing> Blessings { get; set; } = [];
    public Dictionary<string, int> NextMatchChanges { get; set; } = [];
}

public sealed class WeaponState
{
    public string Id { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "guild";
    public Rational BasePower { get; set; }
    public Rational AttackIntervalMs { get; set; }
    public int RangeCenti { get; set; }
    public List<string> SkillIds { get; set; } = [];
    public ulong Seed { get; set; }
    public string GeneratorVersion { get; set; } = "tech-generator-1";
    public string DefinitionVersion { get; set; } = "";
}

public sealed class OwnedBlessing
{
    public string Id { get; set; } = "";
    public int Rank { get; set; } = 1;
    // Fused components preserve the actual parent III values and exact fractions.
    public Dictionary<string, Rational>? InheritedModifiers { get; set; }
}

public sealed class RankEventState
{
    public string Id { get; set; } = "";
    public long Sequence { get; set; }
    public string ActorId { get; set; } = "";
    public BigInteger Rank { get; set; }
    public string? Kind { get; set; }
    public bool Rerolled { get; set; }
    public string? DefinitionJson { get; set; }
    public List<string> Candidates { get; set; } = [];
    public string? FixedRandomResult { get; set; }
    public bool RandomRevealed { get; set; }
}

public sealed class MatchSession
{
    public int FormatVersion { get; set; } = 1;
    public string MatchId { get; set; } = "";
    public string AttemptId { get; set; } = "";
    public string Phase { get; set; } = "Preparation";
    public string ContentHash { get; set; } = "";
    public BattleSnapshot Current { get; set; } = new();
    public BattleSnapshot? MatchStart { get; set; }
    public BattleSnapshot? WaveStart { get; set; }
    public Dictionary<string, List<string>> LevelChoices { get; set; } = [];
    public Dictionary<string, BigInteger> StartingRanks { get; set; } = [];
}
