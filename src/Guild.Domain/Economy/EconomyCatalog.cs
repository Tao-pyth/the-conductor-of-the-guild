using System.Numerics;
using System.Text.Json;
using Guild.Domain.Serialization;

namespace Guild.Domain.Economy;

public sealed record MaterialDefinition(string Id, string Name);

public sealed record DispatchMissionDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public BigInteger RecommendedRank { get; init; } = BigInteger.One;
    public int RecommendedMembers { get; init; } = 1;
    public IReadOnlyList<string> WeaponTypes { get; init; } = [];
    public IReadOnlyList<string> BlessingIds { get; init; } = [];
    // The first entry is the mission's primary material for the minimum failure reward.
    public Dictionary<string, BigInteger> SuccessMaterials { get; init; } = new(StringComparer.Ordinal);
}

public sealed record DeliveryDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? FactionId { get; init; }
    public Dictionary<string, BigInteger> Materials { get; init; } = new(StringComparer.Ordinal);
    public BigInteger Gold { get; init; }
    public int Trust { get; init; }
    public string? WeaponType { get; init; }
}

/// <summary>Economy definitions are independent of saved battle content and its hash.</summary>
public sealed record EconomyCatalog
{
    public const int SupportedSchemaVersion = 1;
    private static readonly Lazy<EconomyCatalog> Embedded = new(LoadEmbedded);
    private static readonly HashSet<string> SupportedWeapons = new(["sword", "spear", "hammer", "bow", "staff"], StringComparer.Ordinal);
    private static readonly HashSet<string> SupportedFactions = new(["C", "M", "K", "T"], StringComparer.Ordinal);

    public static EconomyCatalog Default => Embedded.Value;
    public int SchemaVersion { get; init; } = SupportedSchemaVersion;
    public string Version { get; init; } = "";
    public IReadOnlyList<MaterialDefinition> Materials { get; init; } = [];
    public IReadOnlyList<DispatchMissionDefinition> Missions { get; init; } = [];
    public IReadOnlyList<DeliveryDefinition> Deliveries { get; init; } = [];

    public static EconomyCatalog FromJson(string json)
    {
        var catalog = JsonSerializer.Deserialize<EconomyCatalog>(json, DomainJson.Options)
            ?? throw new JsonException("Economy catalog is null.");
        catalog.EnsureValid();
        return catalog;
    }

    public string ToJson() => JsonSerializer.Serialize(this, DomainJson.Options);

    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count != 0) throw new InvalidDataException("Invalid economy catalog: " + string.Join("; ", errors));
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != SupportedSchemaVersion) errors.Add("Unsupported economy schema.");
        if (string.IsNullOrWhiteSpace(Version)) errors.Add("Economy version is missing.");
        if (Materials is null || Missions is null || Deliveries is null)
        {
            errors.Add("Economy definition lists cannot be null.");
            return errors;
        }
        CheckIds(Materials.Select(x => x?.Id), "material", errors);
        CheckIds(Missions.Select(x => x?.Id), "mission", errors);
        CheckIds(Deliveries.Select(x => x?.Id), "delivery", errors);
        if (Materials.Count == 0) errors.Add("Materials cannot be empty.");
        foreach (var material in Materials.Where(x => x is not null))
            if (string.IsNullOrWhiteSpace(material.Name)) errors.Add($"{material.Id}: material name is missing.");
        var materialIds = Materials.Where(x => x is not null).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var mission in Missions.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(mission.Name) || string.IsNullOrWhiteSpace(mission.Description))
                errors.Add($"{mission.Id}: mission name/description is missing.");
            if (mission.RecommendedRank < 1 || mission.RecommendedMembers is < 1 or > 6)
                errors.Add($"{mission.Id}: invalid recommended rank/member count.");
            CheckIds(mission.WeaponTypes, mission.Id + " weapon", errors);
            CheckIds(mission.BlessingIds, mission.Id + " blessing", errors);
            if (mission.WeaponTypes is null || mission.WeaponTypes.Count == 0
                || mission.WeaponTypes.Any(x => !SupportedWeapons.Contains(x)))
                errors.Add($"{mission.Id}: invalid weapon conditions.");
            if (mission.BlessingIds is null || mission.BlessingIds.Count == 0)
                errors.Add($"{mission.Id}: blessing conditions are missing.");
            CheckMaterials(mission.SuccessMaterials, mission.Id, materialIds, errors);
        }
        foreach (var delivery in Deliveries.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(delivery.Name)) errors.Add($"{delivery.Id}: delivery name is missing.");
            CheckMaterials(delivery.Materials, delivery.Id, materialIds, errors);
            if (delivery.Gold < 0 || delivery.Trust < 0) errors.Add($"{delivery.Id}: negative reward.");
            switch (delivery.Kind)
            {
                case "relation":
                    if (delivery.FactionId is null || !SupportedFactions.Contains(delivery.FactionId)
                        || delivery.Trust <= 0 || delivery.WeaponType is not null)
                        errors.Add($"{delivery.Id}: invalid relation reward.");
                    break;
                case "equipment":
                    if (delivery.WeaponType is null || !SupportedWeapons.Contains(delivery.WeaponType)
                        || delivery.FactionId is not null || delivery.Trust != 0 || delivery.Gold != 0)
                        errors.Add($"{delivery.Id}: invalid equipment reward.");
                    break;
                case "gold":
                    if (delivery.Gold <= 0 || delivery.FactionId is not null || delivery.Trust != 0 || delivery.WeaponType is not null)
                        errors.Add($"{delivery.Id}: invalid gold reward.");
                    break;
                default:
                    errors.Add($"{delivery.Id}: unsupported delivery kind.");
                    break;
            }
        }
        return errors;
    }

    private static void CheckIds(IEnumerable<string?>? ids, string label, List<string> errors)
    {
        if (ids is null) { errors.Add($"{label}: IDs cannot be null."); return; }
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (string.IsNullOrWhiteSpace(id)) errors.Add($"{label}: ID is missing.");
            else if (!found.Add(id)) errors.Add($"{label}: duplicate ID {id}.");
    }

    private static void CheckMaterials(Dictionary<string, BigInteger>? materials, string id,
        HashSet<string> known, List<string> errors)
    {
        if (materials is null || materials.Count == 0) { errors.Add($"{id}: materials are missing."); return; }
        foreach (var (material, amount) in materials)
            if (!known.Contains(material) || amount <= 0) errors.Add($"{id}: unknown material or nonpositive quantity {material}.");
    }

    private static EconomyCatalog LoadEmbedded()
    {
        using var stream = typeof(EconomyCatalog).Assembly.GetManifestResourceStream("Guild.Domain.guild-economy.json")
            ?? throw new InvalidOperationException("Embedded economy catalog is missing.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}
