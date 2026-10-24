using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;

namespace Guild.Domain.Tests;

public sealed class DefinitionTests
{
    private static ContentBundle Fixture() => ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));

    [Fact]
    public void TechnicalFixtureIsExplicitAndContainsOneCycleWithoutAdoptingCatalogues()
    {
        var content = Fixture();
        Assert.True(content.TechnicalFixture);
        Assert.Empty(content.Validate());
        Assert.Equal(5, content.WeaponTemplates.Count);
        Assert.Equal(10, Assert.Single(content.Stages).Waves.Count);
        Assert.Equal(1000, Assert.Single(content.Stages).InitialResources);
        Assert.Single(content.Recipes);
        Assert.All(content.Skills, s => Assert.StartsWith("tech-", s.Id));
        Assert.Contains(content.WeaponTemplates, w => w.SkillIds.Count(id => id == "tech-slash") == 2);
        Assert.Contains(content.Skills, s => s.Effects.Any(e => e.Kind == "heal"));
        Assert.Contains(content.Skills, s => s.Effects.Any(e => e.Kind == "status"));
        Assert.Contains(content.Skills, s => s.IsUltimate);
    }

    [Fact]
    public void InvalidReferencesCooldownsAndFutureSchemasAreRejected()
    {
        var content = Fixture();
        var invalidReference = content with { WeaponTemplates = [content.WeaponTemplates[0] with { SkillIds = ["missing"] }] };
        Assert.Contains(invalidReference.Validate(), e => e.Contains("missing skill"));
        var invalidCooldown = content with { Skills = [content.Skills[0] with { CooldownMs = 100 }] };
        Assert.Contains(invalidCooldown.Validate(), e => e.Contains("cooldown bounds"));
        Assert.Throws<InvalidDataException>(() => (content with { SchemaVersion = 999 }).EnsureValid());
        var duplicate = content with { Skills = [content.Skills[0], content.Skills[0]] };
        Assert.Contains(duplicate.Validate(), e => e.Contains("Duplicate skill"));
    }

    [Fact]
    public void SnapshotKeepsActualOldDefinitionsAndNormalizedHash()
    {
        var original = Fixture();
        var frozen = ContentBundle.FromJson(original.ToJson());
        var updated = original with { Version = "tech-fixture-2", Skills = original.Skills.Select(s => s with { CooldownMs = s.CooldownMs * 2 }).ToArray() };
        Assert.Equal(original.ComputeHash(), frozen.ComputeHash());
        Assert.NotEqual(updated.ComputeHash(), frozen.ComputeHash());
        Assert.Equal(original.Skills[0].CooldownMs, frozen.Skills[0].CooldownMs);
        var whitespaceChanged = ContentBundle.FromJson("\n  " + original.ToJson() + "\n");
        Assert.Equal(original.ComputeHash(), whitespaceChanged.ComputeHash());
    }

    [Fact]
    public void GenerationPersistsDeterministicCompletedValuesAndAllowsRepeatedNormalIds()
    {
        var content = Fixture();
        var source = new Pcg32(12345);
        var restoredSource = Pcg32.Restore(source.Capture());
        var first = WeaponGenerator.Generate(content, source, "tech-sword");
        var repeated = WeaponGenerator.Generate(content, restoredSource, "tech-sword");
        Assert.Equal(first.Seed, repeated.Seed);
        Assert.Equal(first.BasePower, repeated.BasePower);
        Assert.Equal(first.AttackIntervalMs, repeated.AttackIntervalMs);
        Assert.Equal(first.SkillIds, repeated.SkillIds);
        var results = Enumerable.Range(0, 200).Select(_ => WeaponGenerator.Generate(content, source, "tech-sword")).ToArray();
        Assert.Contains(results, w => w.SkillIds.Count(id => id == "tech-slash") == 2);
        Assert.All(results, weapon =>
        {
            Assert.Equal((Rational)100, weapon.Budget);
            Assert.True(weapon.NormalAttackBudget >= 40);
            Assert.True(weapon.SkillIds.Count(id => !content.Skills.Single(s => s.Id == id).IsUltimate) <= 2);
            Assert.True(weapon.SkillIds.Count(id => content.Skills.Single(s => s.Id == id).IsUltimate) <= 1);
            Assert.Equal(content.Version, weapon.TemplateVersion);
        });
    }

    [Fact]
    public void GenerationStopsAtTwentyAttemptsWithoutIncreasingBudgetOrRemovingSkills()
    {
        var original = Fixture();
        var excessiveCosts = original with { Skills = original.Skills.Select(s => s with { GenerationCost = (Rational)1000 }).ToArray() };
        var source = new Pcg32(56789);
        WeaponGenerationException? failure = null;
        for (var i = 0; i < 50 && failure is null; i++)
        {
            try
            {
                var generated = WeaponGenerator.Generate(excessiveCosts, source, "tech-sword");
                Assert.Empty(generated.SkillIds); // A genuinely drawn 0/0 composition remains valid.
            }
            catch (WeaponGenerationException exception) { failure = exception; }
        }
        Assert.NotNull(failure);
        Assert.Equal(20, failure.Attempts);
        Assert.True(failure.RequestedOrdinaryCount + failure.RequestedUltimateCount > 0);
    }
}
