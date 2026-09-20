namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

/// <summary>
/// A fixed-algorithm pseudo-random source, so a dataset is byte-identical on every machine and under
/// every runtime version. <see cref="Random"/> is not used: its sequence is an implementation detail
/// of the framework, and a corpus that changes with a runtime upgrade is not a corpus.
/// </summary>
/// <remarks>xoshiro256** over a SplitMix64-expanded seed.</remarks>
internal sealed class DeterministicRandom
{
    private ulong _s0, _s1, _s2, _s3;

    internal DeterministicRandom(ulong seed)
    {
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    private static ulong SplitMix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    internal ulong NextUInt64()
    {
        var result = ulong.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = ulong.RotateLeft(_s3, 45);

        return result;
    }

    internal int NextInt32() => (int)(NextUInt64() >> 32);

    /// <summary>A value in [0, exclusiveMax).</summary>
    internal int Next(int exclusiveMax) => (int)(NextUInt64() % (ulong)exclusiveMax);

    /// <summary>A value in [inclusiveMin, exclusiveMax).</summary>
    internal int Next(int inclusiveMin, int exclusiveMax) => inclusiveMin + Next(exclusiveMax - inclusiveMin);

    internal bool NextBool() => (NextUInt64() & 1) == 0;

    internal double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    internal long NextInt64() => (long)NextUInt64();

    internal byte NextByte() => (byte)(NextUInt64() >> 56);

    internal void NextBytes(Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = NextByte();
        }
    }

    internal byte[] NextBytes(int count)
    {
        var bytes = new byte[count];
        NextBytes(bytes);
        return bytes;
    }

    internal Guid NextGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        NextBytes(bytes);
        return new Guid(bytes);
    }

    /// <summary>A UTC <see cref="DateTime"/> inside a fixed window, so no value depends on the clock.</summary>
    internal DateTime NextDateTime()
    {
        var epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddTicks((long)(NextUInt64() % (ulong)TimeSpan.TicksPerDay * 3650));
    }

    internal string NextAscii(int minLength, int maxLength)
    {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_.";

        var length = Next(minLength, maxLength + 1);
        return string.Create(length, this, static (span, rng) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[rng.Next(Alphabet.Length)];
            }
        });
    }

    internal T Pick<T>(IReadOnlyList<T> values) => values[Next(values.Count)];
}
