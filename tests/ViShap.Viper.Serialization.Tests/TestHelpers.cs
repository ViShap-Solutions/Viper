namespace ViShap.Viper.Serialization.Tests;

internal static class TestHelpers
{
    public static BinarySerializerOptions TightOptions() => BinarySerializerOptions.Configure()
        .WithLimits(new DeserializationLimits
        {
            MaxDepth = 4,
            MaxArrayLength = 3,
            MaxCollectionLength = 3,
            MaxDictionaryEntries = 2,
            MaxStringLength = 8,
            MaxByteBlobLength = 8,
            MaxTotalElements = 5,
            MaxMessageBytes = 32
        }).Build();

    public static BinarySerializerOptions FullPipeline(byte[] key, bool deflate = false, bool references = false) =>
        BinarySerializerOptions.Configure()
            .WithCompression(deflate ? new Deflate() : new Brotli())
            .WithChecksum(new Crc32())
            .WithEncryption(new Aes256Gcm(), key, "key-A")
            .PreserveReferences(references)
            .Build();

    public static byte[] Serialize<T>(T value, BinarySerializerOptions? options = null) =>
        new BinarySerializer(options).Serialize(value);

    public static T RoundTrip<T>(T value, BinarySerializerOptions? options = null)
    {
        var result = new BinarySerializer(options).Deserialize<T>(Serialize(value, options));
        return result!;
    }

    public static void AssertSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Assert.Equal(expected.ToArray(), actual.ToArray());

    public static void AssertDictionary<TKey,TValue>(IDictionary<TKey,TValue> expected, IDictionary<TKey,TValue> actual)
        where TKey : notnull
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
        {
            Assert.True(actual.TryGetValue(pair.Key, out var value), $"Missing key {pair.Key}");
            Assert.Equal(pair.Value, value);
        }
    }
}
