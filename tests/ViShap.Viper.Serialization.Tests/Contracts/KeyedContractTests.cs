using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins KEY-01, KEY-02, KEY-04, KEY-17 and V0-03: a keyed payload tolerates a reader whose schema has
/// moved on, and skipping a field it does not know can never strand a reference.
/// </summary>
public class KeyedContractTests
{
    private static BinarySerializer WithReferences() =>
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    [Fact]
    public void Deserialize_SameSchema_RoundTrips()
    {
        var serializer = WithReferences();
        var shared = new Node { Value = 11 };

        var result = serializer.Deserialize<NewSchema>(
            serializer.Serialize(new NewSchema { Removed = shared, Kept = shared }))!;

        Assert.Equal(11, result.Removed!.Value);
        Assert.Equal(11, result.Kept!.Value);
    }

    [Fact]
    public void Deserialize_OldSchema_SkipsARemovedFieldThatHeldASharedObject()
    {
        var serializer = WithReferences();
        var shared = new Node { Value = 7 };

        byte[] payload = serializer.Serialize(new NewSchema { Removed = shared, Kept = shared });

        var result = serializer.Deserialize<OldSchema>(payload)!;

        Assert.NotNull(result.Kept);
        Assert.Equal(7, result.Kept!.Value);
    }

    [Fact]
    public void Deserialize_OldSchemaWithoutReferences_SkipsTheRemovedField()
    {
        var serializer = new BinarySerializer();

        byte[] payload = serializer.Serialize(
            new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 7 } });

        Assert.Equal(7, serializer.Deserialize<OldSchema>(payload)!.Kept!.Value);
    }

    [Fact]
    public void Deserialize_FieldAbsentFromThePayload_KeepsTheClrDefault()
    {
        var serializer = new BinarySerializer();

        byte[] payload = serializer.Serialize(new OldSchema { Kept = new Node { Value = 7 } });

        var result = serializer.Deserialize<NewSchema>(payload)!;

        Assert.Null(result.Removed);
        Assert.Equal(7, result.Kept!.Value);
    }

    [Fact]
    public void Deserialize_UnknownKeysAmongNoKnownOnes_AreSkipped()
    {
        Assert.NotNull(new BinarySerializer().Deserialize<EmptyContract>(Wire.KeyedFields(3)));
    }

    [Fact]
    public void Serialize_KeyedContractOnV0_ThrowsNotSupported()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build());

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => serializer.Serialize(new OldSchema { Kept = new Node { Value = 1 } }));
    }
}
