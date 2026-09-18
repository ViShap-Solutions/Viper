using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins V0-02, V0-03 and V0-12: the legacy format stays positional and headerless, rejects keyed
/// contracts, and is never read by accident.
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
    public void Serialize_KeyedContractOnV0_ThrowsNotSupported()
    {
        Assert.Throws<BinaryFormatNotSupportedException>(
            () => V0().Serialize(new OldSchema { Kept = new Node { Value = 1 } }));
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
}
