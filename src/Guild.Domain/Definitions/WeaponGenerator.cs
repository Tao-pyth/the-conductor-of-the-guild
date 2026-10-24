using Guild.Domain.Numerics;
using Guild.Domain.Randomness;

namespace Guild.Domain.Definitions;

public sealed record GeneratedWeapon(
    string TemplateId,
    string Name,
    string WeaponType,
    Rational BasePower,
    Rational AttackIntervalMs,
    int RangeCenti,
    int ProjectileSpeedCentiPerSecond,
    IReadOnlyList<string> SkillIds,
    ulong Seed,
    string TemplateVersion,
    string GeneratorVersion,
    string SkillDefinitionVersion,
    Rational Budget,
    Rational NormalAttackBudget);

public sealed class WeaponGenerationException(string templateId, int ordinaryCount, int ultimateCount, int attempts)
    : InvalidOperationException($"Weapon generation failed for {templateId} after {attempts} attempts; template adjustment is required.")
{
    public string TemplateId { get; } = templateId;
    public int RequestedOrdinaryCount { get; } = ordinaryCount;
    public int RequestedUltimateCount { get; } = ultimateCount;
    public int Attempts { get; } = attempts;
}

public static class WeaponGenerator
{
    public const string Version = "tech-generator-v1";
    public const int MaximumAttempts = 20;

    /// <summary>Budget points and stat variation are technical-fixture estimates, not adopted balance data.</summary>
    public static GeneratedWeapon Generate(ContentBundle content, Pcg32 source, string? templateId = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(source);
        content.EnsureValid();
        if (content.WeaponTemplates.Count == 0) throw new InvalidDataException("No weapon templates are available.");
        // The final numeric values are persisted by the caller. Seed is diagnostic provenance only.
        var seed = ((ulong)source.NextUInt() << 32) | source.NextUInt();
        var random = new Pcg32(seed, 73);
        var template = templateId is null ? content.WeaponTemplates[random.NextInt(content.WeaponTemplates.Count)]
            : content.WeaponTemplates.FirstOrDefault(w => w.Id == templateId)
                ?? throw new ArgumentException("Unknown weapon template.", nameof(templateId));
        if (!content.TechnicalFixture)
            throw new InvalidOperationException("This generator's cost and stat distributions are restricted to technical fixtures.");

        var roll = random.NextInt(100);
        var (normalCount, ultimateCount) = roll switch
        {
            < 10 => (0, 0),
            < 35 => (1, 0),
            < 65 => (2, 0),
            < 70 => (0, 1),
            < 85 => (1, 1),
            _ => (2, 1)
        };
        var compatibleIds = template.CompatibleSkillIds.Count > 0 ? template.CompatibleSkillIds : template.SkillIds;
        var compatible = compatibleIds.Distinct(StringComparer.Ordinal).Select(id => content.Skills.Single(s => s.Id == id))
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var ordinary = compatible.Where(s => !s.IsUltimate).ToArray();
        var ultimates = compatible.Where(s => s.IsUltimate).ToArray();
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            if ((normalCount > 0 && ordinary.Length == 0) || (ultimateCount > 0 && ultimates.Length == 0)) continue;
            var selected = new List<SkillDefinition>();
            for (var i = 0; i < normalCount; i++) selected.Add(ordinary[random.NextInt(ordinary.Length)]);
            for (var i = 0; i < ultimateCount; i++) selected.Add(ultimates[random.NextInt(ultimates.Length)]);
            var skillCost = selected.Aggregate(Rational.Zero, (cost, skill) => cost + (skill.GenerationCost ?? (skill.IsUltimate ? 20 : 10)));
            Rational budget = 100;
            var normalBudget = budget - skillCost;
            if (normalBudget < 40) continue;

            var powerVariation = new Rational(90 + random.NextInt(21), 100);
            var intervalVariation = new Rational(95 + random.NextInt(11), 100);
            return new GeneratedWeapon(template.Id, template.Name, template.WeaponType,
                template.BasePower * (normalBudget / budget) * powerVariation,
                template.AttackIntervalMs * intervalVariation, template.RangeCenti,
                template.ProjectileSpeedCentiPerSecond, selected.Select(s => s.Id).ToArray(),
                seed, content.Version, Version, content.Version, budget, normalBudget);
        }
        throw new WeaponGenerationException(template.Id, normalCount, ultimateCount, MaximumAttempts);
    }
}
