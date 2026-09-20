using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-13, KEY-01, KEY-02, KEY-03, KEY-17 and KEY-18: a keyed payload tolerates a reader whose
/// schema has moved on, skipping a field it does not know can never strand a reference, and the
/// encoding belongs to the payload rather than to a wire format version.
/// </summary>
public class KeyedContractTests
{
    private static BinarySerializer WithReferences() =>
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    [Fact]
    public void Deserialize_CompleteContract_RoundTrips()
    {
        var source = new CompleteContract
        {
            Number = 42,
            Text = "a contract",
            Numbers = [1, 2, 3],
            Child = new Node { Value = 7 },
            Excluded = 99
        };

        var result = new BinarySerializer().Deserialize<CompleteContract>(
            new BinarySerializer().Serialize(source))!;

        Assert.Equal(42, result.Number);
        Assert.Equal("a contract", result.Text);
        Assert.Equal([1, 2, 3], result.Numbers!);
        Assert.Equal(7, result.Child!.Value);
        Assert.Equal(0, result.Excluded);
    }

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
    public void Serialize_KeyedContract_EncodesIdenticallyUnderBothWireFormats()
    {
        var value = new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 7 } };

        byte[] v0 = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build()).Serialize(value);
        byte[] v1 = new BinarySerializer().Serialize(value);

        Assert.Equal(v0, v1[^v0.Length..]);
    }
}
