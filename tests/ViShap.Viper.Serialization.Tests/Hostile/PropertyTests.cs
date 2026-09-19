using System.Text;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-27…HST-34: properties that must hold over a corpus rather than for one hand-picked
/// value. Every generator is seeded from a constant, so a failure here is reproducible and a green
/// run means the same thing on every machine.
/// </summary>
public class PropertyTests
{
    private const int Seed = 20250919;

    private static Random Generator() => new(Seed);

    // --- HST-27: primitive round-trip closure ----------------------------------------------------

    [Fact]
    public void Serialize_Deserialize_GeneratedIntegers_RoundTrip()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        foreach (long value in Boundaries(long.MinValue, long.MaxValue, random))
        {
            Assert.Equal(value, serializer.Deserialize<long>(serializer.Serialize(value)));
            Assert.Equal((int)value, serializer.Deserialize<int>(serializer.Serialize((int)value)));
            Assert.Equal((short)value, serializer.Deserialize<short>(serializer.Serialize((short)value)));
            Assert.Equal((byte)value, serializer.Deserialize<byte>(serializer.Serialize((byte)value)));
        }
    }

    [Fact]
    public void Serialize_Deserialize_GeneratedFloatingPointValues_RoundTrip()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        double[] special =
        [
            0d, -0d, double.Epsilon, double.MaxValue, double.MinValue,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity
        ];

        foreach (double value in special.Concat(
            Enumerable.Range(0, 64).Select(_ => random.NextDouble() * random.Next())))
        {
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(value),
                BitConverter.DoubleToInt64Bits(serializer.Deserialize<double>(serializer.Serialize(value))));
        }
    }

    [Fact]
    public void Serialize_Deserialize_GeneratedStrings_RoundTrip()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        foreach (string value in Strings(random).Take(64))
            Assert.Equal(value, serializer.Deserialize<string>(serializer.Serialize(value)));
    }

    [Fact]
    public void Serialize_Deserialize_GeneratedDecimals_RoundTrip()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        decimal[] special = [0m, decimal.MinValue, decimal.MaxValue, decimal.MinusOne, 0.0000001m];

        foreach (decimal value in special.Concat(
            Enumerable.Range(0, 32).Select(_ => new decimal(random.Next(), random.Next(), random.Next(), false, 3))))
        {
            Assert.Equal(value, serializer.Deserialize<decimal>(serializer.Serialize(value)));
        }
    }

    // --- HST-28: collection round-trip closure ---------------------------------------------------

    [Fact]
    public void Serialize_Deserialize_GeneratedLists_RoundTrip()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        for (int trial = 0; trial < 64; trial++)
        {
            var original = Enumerable.Range(0, random.Next(0, 32)).Select(_ => random.Next()).ToList();

            Assert.Equal(original, serializer.Deserialize<List<int>>(serializer.Serialize(original)));
        }
    }

    [Fact]
    public void Serialize_Deserialize_GeneratedDictionaries_RoundTrip()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        for (int trial = 0; trial < 32; trial++)
        {
            var original = new Dictionary<string, int>();
            foreach (string key in Strings(random).Take(random.Next(0, 16)))
                original[key] = random.Next();

            var restored = serializer.Deserialize<Dictionary<string, int>>(serializer.Serialize(original));

            Assert.Equal(original.Count, restored!.Count);
            foreach (var (key, value) in original)
                Assert.Equal(value, restored[key]);
        }
    }

    // --- HST-29: acyclic nested graph closure ----------------------------------------------------

    [Fact]
    public void Serialize_Deserialize_GeneratedNestedGraphs_RoundTrip()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        for (int trial = 0; trial < 32; trial++)
        {
            var original = Branch(random, depth: 4);

            var restored = serializer.Deserialize<Holder>(serializer.Serialize(original));

            AssertSameShape(original, restored);
        }
    }

    // --- HST-30 / HST-31: containers whose order is and is not part of the value -----------------

    [Fact]
    public void Serialize_Deserialize_HashSetsBuiltInDifferentOrders_YieldTheSameContents()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        for (int trial = 0; trial < 32; trial++)
        {
            int[] values = [.. Enumerable.Range(0, 16).Select(_ => random.Next(0, 64)).Distinct()];

            var forwards = serializer.Deserialize<HashSet<int>>(serializer.Serialize(new HashSet<int>(values)));
            var backwards = serializer.Deserialize<HashSet<int>>(
                serializer.Serialize(new HashSet<int>(values.Reverse())));

            Assert.True(forwards!.SetEquals(backwards!));
        }
    }

    [Fact]
    public void Serialize_Deserialize_SortedSetsBuiltInDifferentOrders_EnumerateIdentically()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        for (int trial = 0; trial < 32; trial++)
        {
            int[] values = [.. Enumerable.Range(0, 16).Select(_ => random.Next(0, 64)).Distinct()];

            var forwards = serializer.Deserialize<SortedSet<int>>(serializer.Serialize(new SortedSet<int>(values)));
            var backwards = serializer.Deserialize<SortedSet<int>>(
                serializer.Serialize(new SortedSet<int>(values.Reverse())));

            Assert.Equal(forwards!.ToArray(), backwards!.ToArray());
            Assert.Equal(values.Order().ToArray(), forwards.ToArray());
        }
    }

    [Fact]
    public void Serialize_SortedDictionaries_ProduceTheSameBytesWhateverTheInsertionOrder()
    {
        var serializer = new BinarySerializer();
        var random = Generator();
        int[] keys = [.. Enumerable.Range(0, 16).Select(_ => random.Next(0, 64)).Distinct()];

        byte[] forwards = serializer.Serialize(
            new SortedDictionary<int, int>(keys.ToDictionary(key => key, key => key * 2)));
        byte[] backwards = serializer.Serialize(
            new SortedDictionary<int, int>(keys.Reverse().ToDictionary(key => key, key => key * 2)));

        Assert.Equal(forwards, backwards);
    }

    // --- HST-32: tightening a limit never rescues a payload --------------------------------------

    [Fact]
    public void Deserialize_TighteningALimit_NeverTurnsAFailureIntoASuccess()
    {
        var loose = new BinarySerializer(
            BinarySerializerOptions.Configure().WithLimits(SerializationLimits.Default).Build());
        var tight = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with
                {
                    MaxDepth = 3,
                    MaxArrayLength = 4,
                    MaxCollectionLength = 4,
                    MaxDictionaryEntries = 4,
                    MaxStringBytes = 16,
                    MaxByteBlobBytes = 16,
                    MaxTotalElements = 16,
                    MaxObjectGraphNodes = 16,
                    MaxKeyedFields = 4,
                    MaxTotalKeyedFields = 8,
                    MaxPayloadBytes = 256,
                    MaxCompressedBytes = 256,
                    MaxEncryptedBytes = 256,
                    MaxWireBytes = 512
                })
                .Build());

        foreach (byte[] payload in Corpus())
        {
            bool looseFailed = Record.Exception(() => loose.Deserialize<List<int>>(payload)) is not null;
            if (!looseFailed)
                continue;

            Assert.NotNull(Record.Exception(() => tight.Deserialize<List<int>>(payload)));
        }
    }

    // --- HST-33: the entry points agree ----------------------------------------------------------

    [Fact]
    public void Serialize_EveryEntryPoint_ProducesTheSameBytes()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        for (int trial = 0; trial < 16; trial++)
        {
            var value = new Person { Name = Strings(random).First(), Age = random.Next() };

            byte[] fromArray = serializer.Serialize(value);

            using var viaSerializer = new MemoryStream();
            serializer.Serialize(viaSerializer, value);

            using var viaExtension = new MemoryStream();
            viaExtension.Serialize(value);

            Assert.Equal(fromArray, viaSerializer.ToArray());
            Assert.Equal(fromArray, viaExtension.ToArray());
        }
    }

    [Fact]
    public void Deserialize_EveryEntryPoint_ProducesTheSameValue()
    {
        var serializer = new BinarySerializer();
        var random = Generator();

        for (int trial = 0; trial < 16; trial++)
        {
            var value = new Person { Name = Strings(random).First(), Age = random.Next() };
            byte[] payload = serializer.Serialize(value);

            var fromArray = serializer.Deserialize<Person>(payload);

            using var stream = new MemoryStream(payload, writable: false);
            var fromStream = serializer.Deserialize<Person>(stream);

            using var forExtension = new MemoryStream(payload, writable: false);
            var fromExtension = forExtension.Deserialize<Person>();

            Assert.Equal(value.Name, fromArray!.Name);
            Assert.Equal(fromArray.Name, fromStream!.Name);
            Assert.Equal(fromArray.Name, fromExtension!.Name);
            Assert.Equal(fromArray.Age, fromStream.Age);
            Assert.Equal(fromArray.Age, fromExtension.Age);
        }
    }

    [Fact]
    public void Deserialize_EveryEntryPoint_FailsTheSameWayOnAMalformedCorpus()
    {
        var serializer = new BinarySerializer();

        foreach (byte[] payload in Corpus())
        {
            var fromArray = Record.Exception(() => serializer.Deserialize<Person>(payload));

            using var stream = new MemoryStream(payload, writable: false);
            var fromStream = Record.Exception(() => serializer.Deserialize<Person>(stream));

            Assert.Equal(fromArray?.GetType(), fromStream?.GetType());
        }
    }

    // --- HST-34: nothing in the corpus escapes the taxonomy --------------------------------------

    [Fact]
    public void Deserialize_AMalformedCorpus_OnlyEverFailsWithAViperException()
    {
        var serializer = new BinarySerializer();

        foreach (byte[] payload in Corpus())
        {
            var ex = Record.Exception(() => serializer.Deserialize<Person>(payload));

            if (ex is not (null or BinarySerializerException))
                Assert.Fail($"A {payload.Length}-byte payload produced {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void Deserialize_AMalformedCorpusAgainstSeveralTargetTypes_OnlyEverFailsWithAViperException()
    {
        var serializer = new BinarySerializer();

        foreach (byte[] payload in Corpus())
        {
            AssertOnlyViperFailures<List<int>>(serializer, payload);
            AssertOnlyViperFailures<Dictionary<string, int>>(serializer, payload);
            AssertOnlyViperFailures<int[,]>(serializer, payload);
            AssertOnlyViperFailures<NewSchema>(serializer, payload);
            AssertOnlyViperFailures<string>(serializer, payload);
        }
    }

    [Fact]
    public void Deserialize_AMalformedCorpusUnderTheV0Fallback_OnlyEverFailsWithAViperException()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().AllowV0Fallback().Build());

        foreach (byte[] payload in Corpus())
        {
            AssertOnlyViperFailures<List<int>>(serializer, payload);
            AssertOnlyViperFailures<Person>(serializer, payload);
        }
    }

    private static void AssertOnlyViperFailures<T>(BinarySerializer serializer, byte[] payload)
    {
        var ex = Record.Exception(() => serializer.Deserialize<T>(payload));

        if (ex is not (null or BinarySerializerException))
            Assert.Fail(
                $"Reading {typeof(T).Name} from a {payload.Length}-byte payload produced " +
                $"{ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>
    /// The malformed corpus: a valid frame carrying a graph of every shape, damaged in one to four
    /// places, plus frames made of nothing but noise behind a valid magic number.
    /// </summary>
    private static IEnumerable<byte[]> Corpus()
    {
        var random = new Random(Seed);
        byte[] valid = new BinarySerializer().Serialize(new Person { Name = "Alice", Age = 30 });

        yield return [];
        yield return [0];
        yield return valid;

        for (int trial = 0; trial < 200; trial++)
        {
            byte[] damaged = (byte[])valid.Clone();
            int edits = random.Next(1, 5);
            for (int edit = 0; edit < edits; edit++)
                damaged[random.Next(damaged.Length)] = (byte)random.Next(256);

            yield return damaged;
        }

        for (int trial = 0; trial < 100; trial++)
        {
            byte[] noise = new byte[random.Next(1, 96)];
            random.NextBytes(noise);
            yield return noise;
        }

        for (int trial = 0; trial < 100; trial++)
        {
            byte[] noise = new byte[random.Next(8, 96)];
            random.NextBytes(noise);
            BitConverter.GetBytes(Wire.Magic).CopyTo(noise, 0);
            BitConverter.GetBytes(1).CopyTo(noise, 4);
            yield return noise;
        }
    }

    private static IEnumerable<long> Boundaries(long minimum, long maximum, Random random)
    {
        yield return minimum;
        yield return minimum + 1;
        yield return -1;
        yield return 0;
        yield return 1;
        yield return maximum - 1;
        yield return maximum;

        for (int trial = 0; trial < 64; trial++)
            yield return random.NextInt64(minimum, maximum);
    }

    private static IEnumerable<string> Strings(Random random)
    {
        yield return string.Empty;
        yield return "a";
        yield return "é中\U0001F600";       // two-, three- and four-byte code points

        while (true)
        {
            var builder = new StringBuilder();
            int length = random.Next(0, 48);
            for (int index = 0; index < length; index++)
                builder.Append((char)random.Next(0x20, 0x7F));

            yield return builder.ToString();
        }
    }

    private static Holder Branch(Random random, int depth)
    {
        var node = new Holder { Name = Strings(random).First() };
        if (depth == 0)
            return node;

        node.Items = [];
        for (int child = 0; child < random.Next(0, 3); child++)
            node.Items.Add(Branch(random, depth - 1));

        return node;
    }

    private static void AssertSameShape(Holder expected, Holder? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Name, actual.Name);

        if (expected.Items is null)
        {
            Assert.Null(actual.Items);
            return;
        }

        Assert.NotNull(actual.Items);
        Assert.Equal(expected.Items.Count, actual.Items.Count);
        for (int index = 0; index < expected.Items.Count; index++)
            AssertSameShape(expected.Items[index], actual.Items[index]);
    }
}
