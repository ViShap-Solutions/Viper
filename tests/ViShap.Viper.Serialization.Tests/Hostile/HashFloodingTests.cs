using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using ViShap.Viper.Formatters;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-46: keys a payload chooses to share a hash cannot make reading a hashed container
/// quadratic. Every hashed container the engine creates for a built-in key type whose default hash is
/// fixed is built with a comparer that hashes the whole value through a per-process seed, so keys that
/// collide under the default hash land in different buckets, and equality stays the key type's own.
/// </summary>
/// <remarks>
/// The tests assert the property that removes the quadratic work — the spread of the chosen keys over
/// buckets — rather than a time. A bucket is the hash modulo a prime, as in the runtime's containers;
/// the default hash of every chosen key falls into one bucket, which each test asserts first, so the
/// payload is hostile and the spread is the comparer's doing.
/// </remarks>
public class HashFloodingTests
{
    private const int Keys = 64;

    /// <summary>A prime well above <see cref="Keys"/>, standing for a container's bucket count.</summary>
    private const int Buckets = 1021;

    private static readonly BinarySerializer Serializer = new();

    /// <summary>Keys whose default hash, <c>lo ^ hi</c>, is zero for every one of them.</summary>
    private static readonly long[] FoldedToZero =
        [.. Enumerable.Range(1, Keys).Select(x => ((long)x << 32) | (uint)x)];

    /// <summary>A frame holding a map of <see cref="FoldedToZero"/> keys, each mapped to its index.</summary>
    private static byte[] FoldedMap() =>
        Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write7BitEncodedInt(Keys + 1);
            for (int index = 0; index < Keys; index++)
            {
                writer.Write(FoldedToZero[index]);
                writer.Write(index);
            }
        }));

    /// <summary>A frame holding a set of <see cref="FoldedToZero"/> elements.</summary>
    private static byte[] FoldedSet() =>
        Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write7BitEncodedInt(Keys + 1);
            foreach (long key in FoldedToZero)
                writer.Write(key);
        }));

    public static TheoryData<string> Maps =>
    [
        "Dictionary", "IDictionary", "ConcurrentDictionary", "ImmutableDictionary", "IImmutableDictionary",
        "FrozenDictionary",
    ];

    public static TheoryData<string> Sets =>
    [
        "HashSet", "ISet", "ImmutableHashSet", "IImmutableSet", "FrozenSet",
    ];

    private static (IEqualityComparer<long> Comparer, IReadOnlyDictionary<long, int> Entries) ReadMap(string container, byte[] frame) =>
        container switch
        {
            "Dictionary" => Map(Serializer.Deserialize<Dictionary<long, int>>(frame)!, map => map.Comparer),
            "IDictionary" => Map((Dictionary<long, int>)Serializer.Deserialize<IDictionary<long, int>>(frame)!, map => map.Comparer),
            "ConcurrentDictionary" => Map(Serializer.Deserialize<ConcurrentDictionary<long, int>>(frame)!, map => map.Comparer),
            "ImmutableDictionary" => Map(Serializer.Deserialize<ImmutableDictionary<long, int>>(frame)!, map => map.KeyComparer),
            "IImmutableDictionary" => Map((ImmutableDictionary<long, int>)Serializer.Deserialize<IImmutableDictionary<long, int>>(frame)!, map => map.KeyComparer),
            "FrozenDictionary" => Map(Serializer.Deserialize<FrozenDictionary<long, int>>(frame)!, map => map.Comparer),
            _ => throw new ArgumentOutOfRangeException(nameof(container)),
        };

    private static (IEqualityComparer<long> Comparer, IReadOnlySet<long> Elements) ReadSet(string container, byte[] frame) =>
        container switch
        {
            "HashSet" => Set(Serializer.Deserialize<HashSet<long>>(frame)!, set => set.Comparer),
            "ISet" => Set((HashSet<long>)Serializer.Deserialize<ISet<long>>(frame)!, set => set.Comparer),
            "ImmutableHashSet" => Set(Serializer.Deserialize<ImmutableHashSet<long>>(frame)!, set => set.KeyComparer),
            "IImmutableSet" => Set((ImmutableHashSet<long>)Serializer.Deserialize<IImmutableSet<long>>(frame)!, set => set.KeyComparer),
            "FrozenSet" => Set(Serializer.Deserialize<FrozenSet<long>>(frame)!, set => set.Comparer),
            _ => throw new ArgumentOutOfRangeException(nameof(container)),
        };

    private static (IEqualityComparer<long>, IReadOnlyDictionary<long, int>) Map<TMap>(TMap map, Func<TMap, IEqualityComparer<long>> comparer)
        where TMap : IReadOnlyDictionary<long, int> => (comparer(map), map);

    private static (IEqualityComparer<long>, IReadOnlySet<long>) Set<TSet>(TSet set, Func<TSet, IEqualityComparer<long>> comparer)
        where TSet : IReadOnlySet<long> => (comparer(set), set);

    /// <summary>Asserts that the default hash puts every key in one bucket, so the input is hostile.</summary>
    private static void AssertOneDefaultBucket<T>(IEnumerable<T> keys) where T : notnull =>
        Assert.Single(keys.Select(key => Bucket(EqualityComparer<T>.Default.GetHashCode(key))).Distinct());

    /// <summary>
    /// Asserts that <paramref name="comparer"/> spreads <paramref name="keys"/> over more than half as
    /// many buckets as there are keys. Sixty-four seeded hashes over 1 021 buckets occupy about 62.
    /// </summary>
    private static void AssertSpread<T>(IEqualityComparer<T> comparer, IEnumerable<T> keys) where T : notnull
    {
        int buckets = keys.Select(key => Bucket(comparer.GetHashCode(key))).Distinct().Count();

        Assert.True(buckets > Keys / 2, $"{Keys} chosen keys share {buckets} of {Buckets} buckets.");
    }

    private static int Bucket(int hash) => (int)((uint)hash % Buckets);

    // --- every hashed container ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Maps))]
    public void Deserialize_AMapOfKeysWithOneDefaultHash_SpreadsThemAndKeepsEveryEntry(string container)
    {
        AssertOneDefaultBucket(FoldedToZero);

        var (comparer, entries) = ReadMap(container, FoldedMap());

        AssertSpread(comparer, FoldedToZero);
        Assert.Equal(Keys, entries.Count);
        for (int index = 0; index < Keys; index++)
            Assert.Equal(index, entries[FoldedToZero[index]]);
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void Deserialize_ASetOfElementsWithOneDefaultHash_SpreadsThemAndKeepsEveryElement(string container)
    {
        AssertOneDefaultBucket(FoldedToZero);

        var (comparer, elements) = ReadSet(container, FoldedSet());

        AssertSpread(comparer, FoldedToZero);
        Assert.Equal(Keys, elements.Count);
        Assert.All(FoldedToZero, key => Assert.Contains(key, elements));
    }

    // --- every key type whose default hash a payload can steer ----------------------------------------

    [Fact]
    public void Deserialize_Int32KeysThatAreMultiplesOfTheBucketCount_SpreadsThem()
    {
        int[] keys = [.. Enumerable.Range(1, Keys).Select(x => x * Buckets)];
        AssertOneDefaultBucket(keys);

        var read = Serializer.Deserialize<HashSet<int>>(Serializer.Serialize(new SortedSet<int>(keys)))!;

        AssertSpread(read.Comparer, keys);
        Assert.Equal(Keys, read.Count);
    }

    [Fact]
    public void Deserialize_UInt64KeysWithOneDefaultHash_SpreadsThem()
    {
        AssertSpreadOnRead([.. FoldedToZero.Select(key => (ulong)key)]);
    }

    [Fact]
    public void Deserialize_EnumKeysWithOneDefaultHash_SpreadsThem()
    {
        AssertSpreadOnRead([.. FoldedToZero.Select(key => (WideEnum)key)]);
    }

    [Fact]
    public void Deserialize_DoubleKeysWithOneDefaultHash_SpreadsThem()
    {
        AssertSpreadOnRead([.. FoldedToZero.Select(BitConverter.Int64BitsToDouble)]);
    }

    [Fact]
    public void Deserialize_GuidKeysWithOneDefaultHash_SpreadsThem()
    {
        // The default hash of a Guid is the exclusive or of its four 32-bit words.
        AssertSpreadOnRead([.. Enumerable.Range(1, Keys).Select(x => new Guid([.. BitConverter.GetBytes(x), .. BitConverter.GetBytes(x), .. new byte[8]]))]);
    }

    [Fact]
    public void Deserialize_DateTimeKeysWithOneDefaultHash_SpreadsThem()
    {
        AssertSpreadOnRead([.. FoldedToZero.Select(ticks => new DateTime(ticks, DateTimeKind.Utc))]);
    }

    [Fact]
    public void Deserialize_DateTimeOffsetKeysWithOneDefaultHash_SpreadsThem()
    {
        AssertSpreadOnRead([.. FoldedToZero.Select(ticks => new DateTimeOffset(ticks, TimeSpan.Zero))]);
    }

    [Fact]
    public void Deserialize_TimeSpanKeysWithOneDefaultHash_SpreadsThem()
    {
        AssertSpreadOnRead([.. FoldedToZero.Select(ticks => new TimeSpan(ticks))]);
    }

    [Fact]
    public void Deserialize_DecimalKeysWithOneDefaultHash_SpreadsThem()
    {
        // The default hash of a decimal folds the words of its coefficient together.
        AssertSpreadOnRead([.. Enumerable.Range(1, Keys).Select(x => new decimal(x, x, 0, isNegative: false, scale: 0))]);
    }

    private static void AssertSpreadOnRead<T>(T[] keys) where T : notnull
    {
        AssertOneDefaultBucket(keys);

        var read = Serializer.Deserialize<Dictionary<T, int>>(
            Serializer.Serialize(new SortedDictionary<T, int>(keys.ToDictionary(key => key, _ => 0, KeyComparer<T>.Instance))))!;

        AssertSpread(read.Comparer, keys);
        Assert.Equal(Keys, read.Count);
    }

    // --- which key types are covered ------------------------------------------------------------------

    [Fact]
    public void KeyComparer_EveryBuiltInKeyTypeWithAFixedHash_HasOne()
    {
        Assert.NotNull(KeyComparer<bool>.Instance);
        Assert.NotNull(KeyComparer<byte>.Instance);
        Assert.NotNull(KeyComparer<sbyte>.Instance);
        Assert.NotNull(KeyComparer<short>.Instance);
        Assert.NotNull(KeyComparer<ushort>.Instance);
        Assert.NotNull(KeyComparer<int>.Instance);
        Assert.NotNull(KeyComparer<uint>.Instance);
        Assert.NotNull(KeyComparer<long>.Instance);
        Assert.NotNull(KeyComparer<ulong>.Instance);
        Assert.NotNull(KeyComparer<nint>.Instance);
        Assert.NotNull(KeyComparer<nuint>.Instance);
        Assert.NotNull(KeyComparer<Int128>.Instance);
        Assert.NotNull(KeyComparer<UInt128>.Instance);
        Assert.NotNull(KeyComparer<char>.Instance);
        Assert.NotNull(KeyComparer<Rune>.Instance);
        Assert.NotNull(KeyComparer<WideEnum>.Instance);
        Assert.NotNull(KeyComparer<float>.Instance);
        Assert.NotNull(KeyComparer<double>.Instance);
        Assert.NotNull(KeyComparer<Half>.Instance);
        Assert.NotNull(KeyComparer<decimal>.Instance);
        Assert.NotNull(KeyComparer<Guid>.Instance);
        Assert.NotNull(KeyComparer<DateTime>.Instance);
        Assert.NotNull(KeyComparer<DateTimeOffset>.Instance);
        Assert.NotNull(KeyComparer<TimeSpan>.Instance);
        Assert.NotNull(KeyComparer<DateOnly>.Instance);
        Assert.NotNull(KeyComparer<TimeOnly>.Instance);
        Assert.NotNull(KeyComparer<BigInteger>.Instance);
        Assert.NotNull(KeyComparer<Version>.Instance);
    }

    [Fact]
    public void Deserialize_StringKeys_KeepTheDefaultComparer()
    {
        var read = Serializer.Deserialize<Dictionary<string, int>>(Serializer.Serialize(new Dictionary<string, int> { ["a"] = 1 }))!;

        Assert.Same(EqualityComparer<string>.Default, read.Comparer);
    }

    [Fact]
    public void Deserialize_KeysOfATypeThatIsNotBuiltIn_KeepTheDefaultComparer()
    {
        var read = Serializer.Deserialize<Dictionary<(int, int), int>>(Serializer.Serialize(new Dictionary<(int, int), int> { [(1, 2)] = 3 }))!;

        Assert.Same(EqualityComparer<(int, int)>.Default, read.Comparer);
    }

    // --- one value, one hash --------------------------------------------------------------------------

    [Fact]
    public void KeyComparer_BothZerosAndEveryNaN_HashAsOneValue()
    {
        AssertOneValue(0.0, -0.0);
        AssertOneValue(double.NaN, BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0001));
        AssertOneValue(0f, -0f);
        AssertOneValue(float.NaN, BitConverter.Int32BitsToSingle(0x7FC0_0001));
        AssertOneValue(Half.Zero, Half.NegativeZero);
        AssertOneValue(Half.NaN, BitConverter.Int16BitsToHalf(0x7E01));
    }

    [Fact]
    public void KeyComparer_DecimalsThatDifferOnlyInTrailingZerosOrTheSignOfZero_HashAsOneValue()
    {
        AssertOneValue(1m, 1.00m);
        AssertOneValue(-12.5m, -12.500m);
        AssertOneValue(0m, new decimal(0, 0, 0, isNegative: true, scale: 3));
        AssertOneValue(79228162514264337593543950335m, 79228162514264337593543950335m);
    }

    [Fact]
    public void KeyComparer_DateTimesThatDifferOnlyInKind_HashAsOneValue()
    {
        const long ticks = 638_000_000_000_000_000;

        AssertOneValue(new DateTime(ticks, DateTimeKind.Utc), new DateTime(ticks, DateTimeKind.Local));
        AssertOneValue(new DateTime(ticks, DateTimeKind.Utc), new DateTime(ticks, DateTimeKind.Unspecified));
    }

    [Fact]
    public void KeyComparer_OneInstantAtTwoOffsets_HashesAsOneValue()
    {
        var instant = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        AssertOneValue(instant, instant.ToOffset(TimeSpan.FromHours(4)));
    }

    [Fact]
    public void KeyComparer_EqualBigIntegersAndVersions_HashAsOneValue()
    {
        AssertOneValue(BigInteger.Parse("123456789012345678901234567890"), BigInteger.Parse("123456789012345678901234567890"));
        AssertOneValue(BigInteger.MinusOne << 3000, -(BigInteger.One << 3000));
        AssertOneValue(new Version(1, 2, 3, 4), new Version(1, 2, 3, 4));
    }

    [Fact]
    public void Deserialize_ADictionaryOfDoubleKeys_FindsAKeyByAnEqualValueOfAnotherRepresentation()
    {
        var read = Serializer.Deserialize<Dictionary<double, int>>(Serializer.Serialize(new Dictionary<double, int> { [0.0] = 1, [double.NaN] = 2 }))!;

        Assert.Equal(1, read[-0.0]);
        Assert.Equal(2, read[BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0001)]);
    }

    private static void AssertOneValue<T>(T first, T second) where T : notnull
    {
        var comparer = KeyComparer<T>.Instance!;

        Assert.True(comparer.Equals(first, second));
        Assert.Equal(comparer.GetHashCode(first), comparer.GetHashCode(second));
    }

    private enum WideEnum : long
    {
    }
}
