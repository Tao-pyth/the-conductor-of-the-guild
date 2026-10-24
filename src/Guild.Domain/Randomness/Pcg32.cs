// PCG reference implementation: Copyright 2014 M. E. O'Neill (pcg-random.org).
// Adapted to C# for this project with explicit seeding, saved state and bounded selection.
// Apache-2.0 reference notices: third_party/pcg/LICENSE.txt and NOTICE.txt.
namespace Guild.Domain.Randomness;

public sealed record PcgState(ulong State, ulong Increment);

/// <summary>PCG-XSH-RR 64/32, with the reference seeding procedure.</summary>
public sealed class Pcg32
{
    public const string AlgorithmVersion = "pcg-xsh-rr-64-32-v1";
    private ulong state;
    private readonly ulong increment;

    public Pcg32(ulong seed, ulong stream = 54)
    {
        unchecked
        {
            increment = (stream << 1) | 1;
            state = 0;
            NextUInt();
            state += seed;
            NextUInt();
        }
    }

    private Pcg32(PcgState saved)
    {
        if ((saved.Increment & 1) == 0) throw new ArgumentException("A PCG increment must be odd.", nameof(saved));
        state = saved.State;
        increment = saved.Increment;
    }

    public uint NextUInt()
    {
        unchecked
        {
            var previous = state;
            state = previous * 6364136223846793005UL + increment;
            var shifted = (uint)(((previous >> 18) ^ previous) >> 27);
            var rotation = (int)(previous >> 59);
            return (shifted >> rotation) | (shifted << ((-rotation) & 31));
        }
    }

    /// <summary>Unbiased integer selection by rejecting the incomplete low range.</summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        var bound = (uint)exclusiveMax;
        var threshold = unchecked(0U - bound) % bound;
        while (true)
        {
            var value = NextUInt();
            if (value >= threshold) return (int)(value % bound);
        }
    }

    public PcgState Capture() => new(state, increment);
    public static Pcg32 Restore(PcgState saved) => new(saved ?? throw new ArgumentNullException(nameof(saved)));
}
