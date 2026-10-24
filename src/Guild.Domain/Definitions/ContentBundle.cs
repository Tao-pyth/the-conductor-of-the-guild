using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;

namespace Guild.Domain.Definitions;

public sealed record ContentBundle
{
    public const int SupportedSchemaVersion = 1;
    public string Version { get; init; } = "";
    public int SchemaVersion { get; init; } = SupportedSchemaVersion;
    public bool TechnicalFixture { get; init; }
    public IReadOnlyList<ParameterDefinition> Parameters { get; init; } = [];
    public IReadOnlyList<SkillDefinition> Skills { get; init; } = [];
    public IReadOnlyList<WeaponTemplateDefinition> WeaponTemplates { get; init; } = [];
    public IReadOnlyList<BlessingDefinition> Blessings { get; init; } = [];
    public IReadOnlyList<RecipeDefinition> Recipes { get; init; } = [];
    public IReadOnlyList<StageDefinition> Stages { get; init; } = [];

    public static ContentBundle Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));
    public static ContentBundle FromJson(string json)
    {
        var content = JsonSerializer.Deserialize<ContentBundle>(json, DomainJson.Options)
            ?? throw new JsonException("Content is null.");
        content.EnsureValid();
        return content;
    }

    public string ToJson() => JsonSerializer.Serialize(this, DomainJson.Options);

    /// <summary>Hashes the normalized DTO representation, not the original file's whitespace.</summary>
    public string ComputeHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson()))).ToLowerInvariant();

    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count != 0) throw new InvalidDataException("Invalid content: " + string.Join("; ", errors));
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != SupportedSchemaVersion) errors.Add($"Unsupported content schema: {SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(Version)) errors.Add("Content version is missing.");
        if (Parameters is null || Skills is null || WeaponTemplates is null || Blessings is null || Recipes is null || Stages is null)
        {
            errors.Add("Definition lists cannot be null.");
            return errors;
        }

        CheckIds(Parameters.Select(x => x?.Id), "parameter", errors, false);
        CheckIds(Skills.Select(x => x?.Id), "skill", errors);
        CheckIds(WeaponTemplates.Select(x => x?.Id), "weapon", errors);
        CheckIds(Blessings.Select(x => x?.Id), "blessing", errors);
        CheckIds(Recipes.Select(x => x?.Id), "recipe", errors);
        CheckIds(Stages.Select(x => x?.Id), "stage", errors);
        if (errors.Any(x => x.StartsWith("Missing ", StringComparison.Ordinal))) return errors;

        var parameterIds = Parameters.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var skillIds = Skills.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var blessingIds = Blessings.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in Parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Unit)) errors.Add($"{p.Id}: parameter unit is missing.");
            if (!Enum.IsDefined(p.Type)) errors.Add($"{p.Id}: invalid parameter type.");
            if (p.Maximum is { } maximum && p.Minimum > maximum) errors.Add($"{p.Id}: invalid parameter bounds.");
        }
        foreach (var skill in Skills)
        {
            var floor = skill.IsUltimate ? Rational.Max(10000, skill.CooldownMs / 4) : Rational.Max(1000, skill.CooldownMs / 10);
            if (skill.CooldownMs <= 0 || floor > skill.CooldownMs * 4) errors.Add($"{skill.Id}: invalid cooldown bounds.");
            if (skill.CastMs < 0 || skill.RecoveryMs < 0 || skill.RangeCenti < 0) errors.Add($"{skill.Id}: negative skill timing/range.");
            if (skill.GenerationCost is { } cost && cost < 0) errors.Add($"{skill.Id}: negative generation cost.");
            if (skill.Effects is null || skill.Effects.Count == 0) { errors.Add($"{skill.Id}: skill has no effects."); continue; }
            CheckIds(skill.Effects.Select(x => x?.Id), $"{skill.Id} effect", errors, false);
            foreach (var effect in skill.Effects.Where(x => x is not null))
            {
                if (effect.Kind is not ("damage" or "heal" or "status")) errors.Add($"{effect.Id}: unsupported effect kind.");
                if (effect.Amount < 0 || effect.DurationMs < 0 || effect.TickMs < 0) errors.Add($"{effect.Id}: negative effect value.");
                if (effect.Kind == "status" && effect.Status is not ("burn" or "poison" or "slow" or "stun")) errors.Add($"{effect.Id}: unknown status.");
                if (effect.Kind == "status" && (effect.DurationMs <= 0 || ((effect.Status is "burn" or "poison") && effect.TickMs <= 0)))
                    errors.Add($"{effect.Id}: invalid status timing.");
            }
        }
        foreach (var weapon in WeaponTemplates)
        {
            if (weapon.WeaponType is not ("sword" or "spear" or "hammer" or "bow" or "staff")) errors.Add($"{weapon.Id}: unknown weapon type.");
            if (weapon.BasePower < 0 || weapon.AttackIntervalMs <= 0 || weapon.RangeCenti < 0 || weapon.ProjectileSpeedCentiPerSecond < 0 || weapon.Budget <= 0)
                errors.Add($"{weapon.Id}: invalid weapon values.");
            if (weapon.SkillIds is null) { errors.Add($"{weapon.Id}: skill slots are null."); continue; }
            foreach (var skillId in weapon.SkillIds)
                if (!skillIds.Contains(skillId)) errors.Add($"{weapon.Id}: missing skill {skillId}.");
            if (weapon.CompatibleSkillIds is null) errors.Add($"{weapon.Id}: compatible skill list is null.");
            else foreach (var skillId in weapon.CompatibleSkillIds)
                if (!skillIds.Contains(skillId)) errors.Add($"{weapon.Id}: missing compatible skill {skillId}.");
            var equipped = weapon.SkillIds.Select(id => Skills.FirstOrDefault(s => s.Id == id)).Where(s => s is not null).ToArray();
            if (equipped.Count(s => !s!.IsUltimate) > 2 || equipped.Count(s => s!.IsUltimate) > 1) errors.Add($"{weapon.Id}: too many skill slots.");
            // Repeated ordinary SkillIDs are intentionally allowed in distinct real slots.
        }
        foreach (var blessing in Blessings)
        {
            if (blessing.Rarity is < 1 or > 10) errors.Add($"{blessing.Id}: rarity must be 1..10.");
            if (blessing.Acquisition is not ("normal" or "fusion" or "unique")) errors.Add($"{blessing.Id}: invalid acquisition route.");
            if (blessing.Tags is null || blessing.Modifiers is null) { errors.Add($"{blessing.Id}: blessing lists cannot be null."); continue; }
            if (blessing.Tags.Any(t => t is not ("base" or "defense" or "attack" or "action" or "support" or "status"))) errors.Add($"{blessing.Id}: unknown classification tag.");
            CheckIds(blessing.Modifiers.Select(x => x?.Id), $"{blessing.Id} modifier", errors, false);
            foreach (var modifier in blessing.Modifiers.Where(x => x is not null))
            {
                if (!parameterIds.Contains(modifier.ParameterId)) errors.Add($"{modifier.Id}: unknown parameter.");
                if (!Enum.IsDefined(modifier.Stage)) errors.Add($"{modifier.Id}: unknown modifier stage.");
                if (modifier.SkillId is not null && !skillIds.Contains(modifier.SkillId)) errors.Add($"{modifier.Id}: unknown skill target.");
                if (modifier.Slot is not (null or "S1" or "S2" or "ULT")) errors.Add($"{modifier.Id}: invalid real slot target.");
            }
        }
        foreach (var recipe in Recipes)
        {
            if (!blessingIds.Contains(recipe.FirstBlessingId) || !blessingIds.Contains(recipe.SecondBlessingId) || !blessingIds.Contains(recipe.ResultBlessingId))
                errors.Add($"{recipe.Id}: unknown blessing reference.");
            if (recipe.FirstBlessingId == recipe.SecondBlessingId) errors.Add($"{recipe.Id}: fusion needs distinct ingredients.");
        }
        foreach (var stage in Stages)
        {
            if (stage.Rank < 1 || stage.RewardRank < 1 || stage.InitialResources < 0 || stage.DefenseHp <= 0 || stage.MapWidth < 1 || stage.MapHeight < 1)
                errors.Add($"{stage.Id}: invalid stage values.");
            if (stage.Path is null || stage.Path.Count < 2 || stage.Path.Any(p => p is null || p.X < 0 || p.X >= stage.MapWidth || p.Y < 0 || p.Y >= stage.MapHeight))
                errors.Add($"{stage.Id}: invalid enemy path.");
            else for (var i = 1; i < stage.Path.Count; i++)
                if (Math.Abs(stage.Path[i].X - stage.Path[i - 1].X) + Math.Abs(stage.Path[i].Y - stage.Path[i - 1].Y) != 1)
                    errors.Add($"{stage.Id}: path must use adjacent orthogonal cells.");
            if (stage.Waves is null || stage.Waves.Count == 0) { errors.Add($"{stage.Id}: missing waves."); continue; }
            for (var i = 0; i < stage.Waves.Count; i++)
            {
                var wave = stage.Waves[i];
                if (wave is null || wave.Number != i + 1 || wave.EnemyCount <= 0 || wave.EnemyLevel <= 0 || wave.EnemyHp <= 0 || wave.EnemyDefense < 0
                    || wave.EnemyPower < 0 || wave.EnemySpeedCentiPerSecond <= 0 || wave.SpawnIntervalMs <= 0)
                    errors.Add($"{stage.Id}: invalid wave at index {i}.");
            }
        }
        return errors;
    }

    private void CheckIds(IEnumerable<string?> ids, string kind, List<string> errors, bool checkFixturePrefix = true)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id)) { errors.Add($"Missing {kind} ID."); continue; }
            if (!seen.Add(id)) errors.Add($"Duplicate {kind} ID: {id}.");
            if (TechnicalFixture && checkFixturePrefix && !id.StartsWith("tech-", StringComparison.Ordinal)) errors.Add($"{id}: technical fixture IDs must begin with tech-.");
        }
    }
}
