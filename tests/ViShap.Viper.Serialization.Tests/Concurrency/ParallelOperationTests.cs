using ViShap.Viper.Crypto;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Concurrency;

/// <summary>
/// Pins CN-01, CN-02, CN-17 and CN-18: a <see cref="BinarySerializer"/> holds configuration, never
/// the state of a call. One operation's accounting, key material and stream position belong to that
/// operation alone, so the same instance used from many threads at once must give every one of them
/// the same answer it would have given alone (contract section 2.2).
/// </summary>
public class ParallelOperationTests
{
    private static readonly byte[] KeyA =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    private static readonly byte[] KeyB =
    [
        0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7,
        0xF8, 0xF9, 0xFA, 0xFB, 0xFC, 0xFD, 0xFE, 0xFF,
        0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7,
        0xE8, 0xE9, 0xEA, 0xEB, 0xEC, 0xED, 0xEE, 0xEF
    ];

    [Fact]
    public void SharedSerializer_UsedFromManyThreads_GivesEveryCallItsOwnResult()
    {
        var serializer = new BinarySerializer();

        var restored = Concurrent.Race(worker =>
            serializer.Deserialize<Person>(
                serializer.Serialize(new Person { Name = $"person-{worker}", Age = worker })));

        for (int worker = 0; worker < restored.Length; worker++)
        {
            Assert.Equal($"person-{worker}", restored[worker]!.Name);
            Assert.Equal(worker, restored[worker]!.Age);
        }
    }

    [Fact]
    public void SharedSerializer_UsedFromManyThreadsOverStreams_GivesEveryCallItsOwnResult()
    {
        var serializer = new BinarySerializer();

        var restored = Concurrent.Race(worker =>
        {
            using var stream = new MemoryStream();
            serializer.Serialize(stream, Enumerable.Range(0, worker + 1).ToList());
            stream.Position = 0;

            return serializer.Deserialize<List<int>>(stream);
        });

        for (int worker = 0; worker < restored.Length; worker++)
            Assert.Equal(Enumerable.Range(0, worker + 1), restored[worker]!);
    }

    [Fact]
    public void Budget_IsNotSharedBetweenConcurrentCalls()
    {
        // Every call sits just under the ceiling on its own. If a budget were shared, the workers
        // would exhaust it between them and all but the first would fail.
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxTotalElements = 1000 })
            .Build());

        var value = Enumerable.Range(0, 900).ToList();

        var restored = Concurrent.Race(_ => serializer.Deserialize<List<int>>(serializer.Serialize(value)));

        Assert.All(restored, list => Assert.Equal(value, list!));
    }

    [Fact]
    public void Budget_IsNotSharedBetweenSequentialCalls()
    {
        // The same ceiling, reached repeatedly through one instance: a budget that survived a call
        // would make the second one fail where the first succeeded.
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxTotalElements = 1000 })
            .Build());

        var value = Enumerable.Range(0, 900).ToList();

        for (int attempt = 0; attempt < 5; attempt++)
            Assert.Equal(value, serializer.Deserialize<List<int>>(serializer.Serialize(value))!);
    }

    [Fact]
    public void Encryption_WithDistinctKeyIdsInParallel_StaysCorrect()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), ResolveKey, keyId: "alpha")
            .Build());

        var beta = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), ResolveKey, keyId: "beta")
            .Build());

        var restored = Concurrent.Race(worker =>
        {
            var writer = worker % 2 == 0 ? serializer : beta;
            return writer.Deserialize<Person>(writer.Serialize(new Person { Name = $"p{worker}", Age = worker }));
        });

        for (int worker = 0; worker < restored.Length; worker++)
            Assert.Equal($"p{worker}", restored[worker]!.Name);
    }

    [Fact]
    public void Encryption_PayloadOfOneKeyId_IsNotReadableUnderTheOther()
    {
        // Guards the test above: it would prove nothing if both ids resolved to the same key.
        var alpha = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), ResolveKey, keyId: "alpha")
            .Build());

        var beta = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), _ => KeyB, keyId: "beta")
            .Build());

        byte[] payload = alpha.Serialize(new Person { Name = "Alice", Age = 30 });

        Assert.Throws<BinaryIntegrityException>(() => beta.Deserialize<Person>(payload));
    }

    [Fact]
    public void Inspection_OfSeparateStreamsInParallel_ReportsEachStreamsOwnHeader()
    {
        byte[][] payloads =
        [
            .. Enumerable.Range(0, Concurrent.Workers).Select(worker =>
                new BinarySerializer(BinarySerializerOptions.Configure()
                        .WithEncryption(new Aes256Gcm(), ResolveKey, keyId: $"key-{worker}")
                        .Build())
                    .Serialize(worker))
        ];

        var ids = Concurrent.Race(worker =>
        {
            using var stream = new MemoryStream(payloads[worker]);
            return BinaryFormatInspector.Peek(stream)!.Value.KeyId;
        });

        for (int worker = 0; worker < ids.Length; worker++)
            Assert.Equal($"key-{worker}", ids[worker]);
    }

    /// <summary>One key per id, so a payload written under one id cannot be read under another.</summary>
    private static byte[] ResolveKey(string? keyId) => keyId == "alpha" ? KeyA : KeyB;
}
