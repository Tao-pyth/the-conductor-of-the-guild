using System.Numerics;
using Guild.Domain.Numerics;

namespace Guild.Domain.Economy;

public sealed record DispatchMember(string Id, BigInteger Rank, string WeaponType, IReadOnlyList<string> BlessingIds);

public sealed record DispatchEvaluation
{
    public int SuccessPercent { get; init; }
    public Rational RankPoints { get; init; }
    public Rational WeaponPoints { get; init; }
    public Rational BlessingPoints { get; init; }
}

/// <summary>Exact, read-only calculations. Drawing and freezing the one outcome belongs to the caller.</summary>
public static class DispatchCalculator
{
    public static DispatchEvaluation Evaluate(DispatchMissionDefinition mission, IReadOnlyList<DispatchMember> members)
    {
        ValidateMission(mission);
        ArgumentNullException.ThrowIfNull(members);
        ValidateCount(members.Count);
        var actorIds = new HashSet<string>(StringComparer.Ordinal);
        var weapons = new HashSet<string>(StringComparer.Ordinal);
        var blessings = new HashSet<string>(StringComparer.Ordinal);
        var rankTotal = Rational.Zero;
        foreach (var member in members)
        {
            if (member is null || string.IsNullOrWhiteSpace(member.Id) || !actorIds.Add(member.Id)
                || member.Rank < 1 || string.IsNullOrWhiteSpace(member.WeaponType) || member.BlessingIds is null
                || member.BlessingIds.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Dispatch members must be unique adventurers with valid rank, weapon and blessing IDs.", nameof(members));
            rankTotal += member.Rank >= mission.RecommendedRank ? Rational.One : new Rational(member.Rank, mission.RecommendedRank);
            weapons.Add(member.WeaponType);
            blessings.UnionWith(member.BlessingIds);
        }
        var rankPoints = 40 * Rational.Min(1, rankTotal / mission.RecommendedMembers);
        var weaponPoints = 20 * Coverage(mission.WeaponTypes, weapons);
        var blessingPoints = 20 * Coverage(mission.BlessingIds, blessings);
        return new()
        {
            SuccessPercent = (int)Rational.Clamp(20 + rankPoints + weaponPoints + blessingPoints, 0, 100).Floor(),
            RankPoints = rankPoints,
            WeaponPoints = weaponPoints,
            BlessingPoints = blessingPoints
        };
    }

    public static Dictionary<string, BigInteger> Rewards(DispatchMissionDefinition mission, int members, bool success)
    {
        ValidateMission(mission);
        ValidateCount(members);
        var result = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        foreach (var (id, basis) in mission.SuccessMaterials)
        {
            var amount = basis * (members + 1) / 2;
            if (!success) amount /= 4;
            if (amount > 0) result.Add(id, amount);
        }
        if (result.Count == 0) result.Add(mission.SuccessMaterials.First().Key, BigInteger.One);
        return result;
    }

    private static Rational Coverage(IReadOnlyList<string> required, HashSet<string> available)
    {
        var kinds = required.Distinct(StringComparer.Ordinal).ToArray();
        return kinds.Length == 0 ? Rational.One : new Rational(kinds.Count(available.Contains), kinds.Length);
    }

    private static void ValidateCount(int count)
    {
        if (count is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(count), "Dispatches require 1..6 members.");
    }

    private static void ValidateMission(DispatchMissionDefinition mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (mission.RecommendedRank < 1 || mission.RecommendedMembers is < 1 or > 6
            || mission.WeaponTypes is null || mission.BlessingIds is null
            || mission.WeaponTypes.Any(string.IsNullOrWhiteSpace) || mission.BlessingIds.Any(string.IsNullOrWhiteSpace)
            || mission.SuccessMaterials is null || mission.SuccessMaterials.Count == 0
            || mission.SuccessMaterials.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value <= 0))
            throw new ArgumentException("Dispatch mission has invalid requirements or rewards.", nameof(mission));
    }
}
