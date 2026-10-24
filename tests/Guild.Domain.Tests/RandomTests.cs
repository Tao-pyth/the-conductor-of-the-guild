using System.Text.Json;
using Guild.Domain.Randomness;
using Guild.Domain.Serialization;

namespace Guild.Domain.Tests;

public sealed class RandomTests
{
    [Fact]
    public void MatchesPcgReferenceSeed42Stream54()
    {
        var random = new Pcg32(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => random.NextUInt()));
    }

    [Fact]
    public void RestoringActualStateContinuesTheSameSequence()
    {
        var random = new Pcg32(ulong.MaxValue, 73);
        for (var i = 0; i < 37; i++) random.NextUInt();
        var savedJson = JsonSerializer.Serialize(random.Capture(), DomainJson.Options);
        var restored = Pcg32.Restore(JsonSerializer.Deserialize<PcgState>(savedJson, DomainJson.Options)!);
        for (var i = 0; i < 500; i++) Assert.Equal(random.NextUInt(), restored.NextUInt());
        Assert.Throws<ArgumentException>(() => Pcg32.Restore(new PcgState(42, 2)));
    }

    [Fact]
    public void StreamsAreIndependentAndBoundsAreRespected()
    {
        var combat = new Pcg32(42, 1);
        var untouchedCombat = new Pcg32(42, 1);
        var visual = new Pcg32(42, 2);
        for (var i = 0; i < 1000; i++) visual.NextUInt();
        for (var i = 0; i < 100; i++) Assert.Equal(untouchedCombat.NextUInt(), combat.NextUInt());
        var bounded = new Pcg32(1234);
        Assert.Equal(0, bounded.NextInt(1));
        foreach (var bound in new[] { 2, 3, 10, 100, int.MaxValue })
            for (var i = 0; i < 1000; i++) Assert.InRange(bounded.NextInt(bound), 0, bound - 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => bounded.NextInt(0));
    }
}
