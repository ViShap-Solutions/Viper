using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins V0-02, V0-03, V0-04, V0-08, V0-09, V0-12, V0-23 and V0-24: version 0 carries the same
/// payload encoding as version 1, keyed contracts included, is bounded by the same limits, and is
/// never read by accident.
/// </summary>
public class V0FormatTests
{
    private static BinarySerializer V0(bool allowFallback = true) =>
        new(BinarySerializerOptions.Configure()
            .WithVersion(0)
            .AllowV0Fallback(allowFallback)
            .Build());

    [Fact]
    public void Deserialize_V0Payload_RoundTripsPositionalData()
    {
        var serializer = V0();
        var person = new Person { Name = "Alice", Age = 30 };

        var result = serializer.Deserialize<Person>(serializer.Serialize(person));

        Assert.Equivalent(person, result);
    }

    [Fact]
    public void Serialize_KeyedContractOnV0_RoundTrips()
    {
        var serializer = V0();

        var result = serializer.Deserialize<NewSchema>(
            serializer.Serialize(new NewSchema
            {
                Removed = new Node { Value = 1 },
                Kept = new Node { Value = 7 }
            }))!;

        Assert.Equal(1, result.Removed!.Value);
        Assert.Equal(7, result.Kept!.Value);
    }

    [Fact]
    public void Serialize_KeyedContractOnV0AndUnderPreservedReferences_RestoreTheSameValue()
    {
        // P7-02: the keyed layout belongs to the type, not to the envelope. The two profiles differ
        // in what wraps the payload and in the reference framing inside it, and agree on the value.
        var value = new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 7 } };

        var headerless = V0();
        var preserved = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());

        var fromV0 = headerless.Deserialize<NewSchema>(headerless.Serialize(value))!;
        var fromV1 = preserved.Deserialize<NewSchema>(preserved.Serialize(value))!;

        Assert.Equal(fromV1.Removed!.Value, fromV0.Removed!.Value);
        Assert.Equal(fromV1.Kept!.Value, fromV0.Kept!.Value);
    }

    [Fact]
    public void Serialize_KeyedContractOnV0_CarriesNeitherHeaderNorReferenceFraming()
    {
        // The other half of P7-02: the agreement above is not two identical payloads. V0 writes no
        // magic, and a value written under preserved references declares it in the header.
        var value = new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 7 } };

        byte[] headerless = V0().Serialize(value);
        byte[] preserved = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build()).Serialize(value);

        Assert.NotEqual(BitConverter.GetBytes(Wire.Magic), headerless[..4]);
        Assert.Equal(BitConverter.GetBytes(Wire.Magic), preserved[..4]);
        Assert.True(Wire.ReadHeader(preserved).PreserveReferences);
    }

    [Fact]
    public void Deserialize_KeyedContractOnV0_SkipsAKeyTheReaderDoesNotKnow()
    {
        var serializer = V0();

        byte[] payload = serializer.Serialize(new NewSchema
        {
            Removed = new Node { Value = 1 },
            Kept = new Node { Value = 7 }
        });

        Assert.Equal(7, serializer.Deserialize<OldSchema>(payload)!.Kept!.Value);
    }

    [Fact]
    public void Deserialize_NestedKeyedContractOnV0_RoundTrips()
    {
        var serializer = V0();
        var value = new NestedSchema
        {
            Inner = new NewSchema { Kept = new Node { Value = 7 } },
            Tag = "outer"
        };

        var result = serializer.Deserialize<NestedSchema>(serializer.Serialize(value))!;

        Assert.Equal("outer", result.Tag);
        Assert.Equal(7, result.Inner!.Kept!.Value);
        Assert.Null(result.Inner.Removed);
    }

    [Fact]
    public void Deserialize_KeyedContractOnV0EmbeddedInALargerStream_StopsAtTheRootValue()
    {
        var serializer = V0();
        using var stream = new MemoryStream();

        serializer.Serialize(stream, new OldSchema { Kept = new Node { Value = 7 } });
        stream.Write([9, 9, 9, 9]);
        stream.Position = 0;

        Assert.Equal(7, serializer.Deserialize<OldSchema>(stream)!.Kept!.Value);
    }

    [Fact]
    public void Serialize_KeyedContractOnV0ToANonSeekableDestination_ThrowsNotSupported()
    {
        using var destination = new NonSeekableWriteStream();

        var ex = Assert.Throws<NotSupportedException>(
            () => V0().Serialize(destination, new OldSchema { Kept = new Node { Value = 1 } }));

        Assert.Contains("seekable payload stream", ex.Message);
    }

    [Fact]
    public void Serialize_PositionalDataOnV0ToANonSeekableDestination_Succeeds()
    {
        using var destination = new NonSeekableWriteStream();

        V0().Serialize(destination, new Person { Name = "Alice", Age = 30 });

        Assert.NotEmpty(destination.Written);
    }

    [Fact]
    public void Deserialize_HeaderlessStreamWithTheFallbackDisabled_ThrowsFormat()
    {
        byte[] payload = V0().Serialize(123);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<int>(payload));
    }

    [Fact]
    public void Deserialize_V1PayloadWithAV0Reader_StillUsesV1()
    {
        // The fallback only applies when the magic number is absent.
        byte[] payload = new BinarySerializer().Serialize(new Person { Name = "Alice", Age = 30 });

        Assert.Equal("Alice", V0().Deserialize<Person>(payload)!.Name);
    }

    [Fact]
    public void Serialize_V0_IgnoresPreserveReferencesAndRefusesACycle()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithVersion(0)
                .AllowV0Fallback()
                .PreserveReferences()
                .Build());

        var first = new Cyclic { Name = "a" };
        first.Next = first;

        Assert.Throws<BinaryTypeException>(() => serializer.Serialize(first));
    }

    [Fact]
    public void Deserialize_V0PayloadEmbeddedInALargerStream_DoesNotRequireTheStreamToEnd()
    {
        var serializer = V0();
        using var stream = new MemoryStream();

        serializer.Serialize(stream, 123);
        stream.Write([9, 9, 9, 9]);
        stream.Position = 0;

        Assert.Equal(123, serializer.Deserialize<int>(stream));
    }

    [Fact]
    public void Serialize_V0PayloadOverMaxPayloadBytes_ThrowsLimit()
    {
        // The string itself is well within MaxStringBytes, so only the payload ceiling can fire.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithVersion(0)
                .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 16 })
                .Build());

        AssertEx.Throws<BinaryLimitException>(
            "payload byte budget of 16", () => serializer.Serialize(new string('a', 64)));
    }

    [Fact]
    public void Serialize_V0PayloadAtMaxPayloadBytes_Succeeds()
    {
        // A null flag, a 7-bit length of 1 and fourteen UTF-8 bytes are exactly sixteen.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithVersion(0)
                .AllowV0Fallback()
                .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 16 })
                .Build());

        byte[] payload = serializer.Serialize(new string('a', 14));

        Assert.Equal(16, payload.Length);
        Assert.Equal(new string('a', 14), serializer.Deserialize<string>(payload));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void Deserialize_TruncatedV0Payload_ThrowsFormat(int length)
    {
        var serializer = V0();
        byte[] payload = serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        Assert.Throws<BinaryFormatException>(
            () => serializer.Deserialize<Person>(Mutate.Truncate(payload, length)));
    }
}
