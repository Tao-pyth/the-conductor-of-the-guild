using System.Text.Json;
using Guild.Domain.Battle;
using Guild.Domain.Serialization;

namespace Guild.Domain.Tests;

public sealed class LegacyDefenseCompatibilityTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0", name));

    [Fact]
    public void PublishedDefenseDefinitionsKeepTheirOriginalHashAfterAddingInvasionDefinitions()
    {
        using var session = JsonDocument.Parse(Fixture("battle-session.json"));
        var originalHash = session.RootElement.GetProperty("contentHash").GetString();
        var snapshot = JsonSerializer.Deserialize<BattleSnapshot>(session.RootElement.GetProperty("waveStart"), DomainJson.Options)!;
        Assert.Equal("ad265a687145776a58cde75ac0d1db26f7062dc2b26111f4ce06b05356dffeac", originalHash);
        Assert.Equal(originalHash, snapshot.Setup.Content.ComputeHash());
    }

    [Fact]
    public void PublishedV020DefenseReplayMatchesTheReleasedAssemblyThroughAllTenWaves()
    {
        using var session = JsonDocument.Parse(Fixture("battle-session.json"));
        var snapshot = JsonSerializer.Deserialize<BattleSnapshot>(session.RootElement.GetProperty("waveStart"), DomainJson.Options)!;
        var battle = BattleSimulation.Restore(snapshot);
        var steps = 0;
        while (!battle.State.Victory && !battle.State.Defeated && steps < 30000)
        {
            if (!battle.State.Running)
            {
                foreach (var unit in battle.State.Units.Where(x => !x.IsEnemy))
                    while (unit.PendingLevelChoices > 0) Assert.True(battle.GrantLevel(unit.Id, "firepower"));
                Assert.True(battle.BeginWave());
            }
            battle.Step();
            steps++;
        }
        Assert.True(battle.State.Victory);
        Assert.Equal(10, battle.State.Wave);
        using var expected = JsonDocument.Parse(Fixture("battle-golden.json"));
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(battle.Capture(), DomainJson.Options));
        AssertLegacyFields(expected.RootElement, actual.RootElement, "snapshot");
    }

    // Compare every field that the published v0.2.0 assembly wrote, allowing only
    // newly added mode-specific fields in the current representation.
    private static void AssertLegacyFields(JsonElement expected, JsonElement actual, string path)
    {
        Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: JSON kind changed.");
        if (expected.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in expected.EnumerateObject())
            {
                Assert.True(actual.TryGetProperty(property.Name, out var value), $"{path}.{property.Name}: missing legacy field.");
                AssertLegacyFields(property.Value, value, path + "." + property.Name);
            }
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            Assert.True(expected.GetArrayLength() == actual.GetArrayLength(), $"{path}: legacy array length changed.");
            for (var index = 0; index < expected.GetArrayLength(); index++)
                AssertLegacyFields(expected[index], actual[index], $"{path}[{index}]");
        }
        else Assert.True(expected.ToString() == actual.ToString(), $"{path}: expected {expected}, actual {actual}.");
    }
}
