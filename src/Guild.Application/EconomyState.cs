using System.Numerics;
using Guild.Domain.Economy;
using Guild.Domain.Randomness;

namespace Guild.Application;

public sealed class GuildEconomyState
{
    public int Version { get; set; } = 1;
    public Dictionary<string, BigInteger> Materials { get; set; } = [];
    public Dictionary<string, FactionStandingState> Factions { get; set; } = [];
    public DispatchReservationState? Reservation { get; set; }
    public Dictionary<string, WeaponState> OfferedEquipment { get; set; } = [];
    public Dictionary<string, int> DeliveryCompletions { get; set; } = [];
    public PcgState DispatchRng { get; set; } = new Pcg32(20261026, 117).Capture();
    public PcgState OfferRng { get; set; } = new Pcg32(20261026, 119).Capture();
    public string LastReport { get; set; } = "";
}

public sealed class FactionStandingState
{
    public int Trust { get; set; }
    public bool ConflictUnresolved { get; set; }
    public bool RepairInProgress { get; set; }
}

public sealed class DispatchReservationState
{
    public string MissionId { get; set; } = "";
    public List<string> ActorIds { get; set; } = [];
}

public sealed class MatchEconomyState
{
    public int Version { get; set; } = 1;
    public string CatalogVersion { get; set; } = "";
    public BigInteger Contribution { get; set; }
    public Dictionary<string, BigInteger> VictoryMaterials { get; set; } = [];
    public DispatchAssignmentState? Dispatch { get; set; }
}

public sealed class DispatchAssignmentState
{
    public string MatchId { get; set; } = "";
    public DispatchMissionDefinition Mission { get; set; } = new();
    public List<DispatchMemberSnapshot> Members { get; set; } = [];
    public int SuccessPercent { get; set; }
    public int Roll { get; set; }
    public Dictionary<string, BigInteger> RankExperience { get; set; } = [];
}

public sealed class DispatchMemberSnapshot
{
    public string ActorId { get; set; } = "";
    public BigInteger Rank { get; set; }
    public string WeaponType { get; set; } = "";
    public List<string> BlessingIds { get; set; } = [];
    public BigInteger Hp { get; set; }
    public int WeaknessStacks { get; set; }
}
