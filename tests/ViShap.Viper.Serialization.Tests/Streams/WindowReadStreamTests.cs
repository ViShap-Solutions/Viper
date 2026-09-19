using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Streams;

/// <summary>
/// Pins STR-17…STR-22: a window exposes exactly one declared subrange of an already metered stream.
/// A field decoder may consume its declared length and not one byte more, running past it is a
/// malformed payload rather than a limit violation, and an unknown field is skipped in bounded
/// chunks instead of being copied into an attacker-sized array.
/// </summary>
public class WindowReadStreamTests
{
    /// <summary>Bytes the whole operation may allocate. Generous, and orders below the declarations.</summary>
    private const long AllocationCeiling = 1024 * 1024;

    private static WindowReadStream Window(int windowLength, int physicalBytes)
    {
        byte[] content = [.. Enumerable.Range(0, physicalBytes).Select(value => (byte)value)];
        return new WindowReadStream(new MemoryStream(content), windowLength, "Key 1 payload");
    }

    // --- STR-17: exactly the declared length ----------------------------------------------------

    [Fact]
    public void Read_TheDeclaredLength_Succeeds()
    {
        var window = Window(windowLength: 4, physicalBytes: 16);

        byte[] destination = new byte[4];
        window.ReadExactly(destination);

        Assert.Equal([0, 1, 2, 3], destination);
        Assert.Equal(0, window.RemainingBytes);
    }

    [Fact]
    public void Read_InSeveralSteps_ConsumesTheWindowExactly()
    {
        var window = Window(windowLength: 4, physicalBytes: 16);

        Assert.Equal(0, window.ReadByte());
        Assert.Equal(1, window.ReadByte());
        Assert.Equal(2, window.RemainingBytes);
    }

    [Fact]
    public void Length_IsTheDeclaredLength()
    {
        var window = Window(windowLength: 4, physicalBytes: 16);

        Assert.Equal(4, window.Length);
    }

    // --- STR-18 / STR-19: the boundary is the end of the field -----------------------------------

    [Fact]
    public void Read_PastTheWindow_ReturnsNothingRatherThanTheNextField()
    {
        var window = Window(windowLength: 4, physicalBytes: 16);
        window.ReadExactly(new byte[4]);

        Assert.Equal(0, window.Read(new byte[4]));
        Assert.Equal(-1, window.ReadByte());
    }

    [Fact]
    public void Exceeded_BeyondTheWindow_IsAMalformedPayloadAndNotALimitViolation()
    {
        var window = Window(windowLength: 4, physicalBytes: 16);

        var ex = window.Exceeded(8, "String byte length");

        Assert.IsType<BinaryFormatException>(ex);
        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_AFieldDeclaringMoreThanItsWindowHolds_ThrowsFormat()
    {
        // The string claims eight bytes inside a field that declares four.
        byte[] frame = Wire.Frame(
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(2, [1, 8, 0x61, 0x61])
            ])
        ]);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<OldSchema>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_AFieldWithTrailingBytesInsideItsWindow_ThrowsFormat()
    {
        // Key 2 of OldSchema decodes a Node, which is shorter than the six bytes declared.
        byte[] frame = Wire.Frame(
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(2, [1, 7, 0, 0, 0, 9, 9])
            ])
        ]);

        AssertEx.Throws<BinaryFormatException>(
            "trailing", () => new BinarySerializer().Deserialize<OldSchema>(frame));
    }

    // --- STR-20: the rest is consumed in bounded chunks ------------------------------------------

    [Fact]
    public void SkipRemaining_ConsumesTheRestOfTheWindowOnly()
    {
        var window = Window(windowLength: 4, physicalBytes: 16);
        window.ReadExactly(new byte[1]);

        window.SkipRemaining();

        Assert.Equal(0, window.RemainingBytes);
        Assert.Equal(4, window.Position);
    }

    [Fact]
    public void SkipRemaining_OverATruncatedSource_ThrowsFormat()
    {
        var window = Window(windowLength: 16, physicalBytes: 4);

        Assert.Throws<BinaryFormatException>(() => window.SkipRemaining());
    }

    [Fact]
    public void SkipRemaining_OverALargeWindow_AllocatesNothingProportional()
    {
        byte[] content = new byte[4 * 1024 * 1024];

        AssertEx.AllocatesLessThan(AllocationCeiling, () =>
            new WindowReadStream(new MemoryStream(content), content.Length, "Key 1 payload")
                .SkipRemaining());
    }

    // --- STR-21: the window never materializes the field -----------------------------------------

    [Fact]
    public void Deserialize_AnUnknownFieldDeclaringMoreThanIsPresent_AllocatesNothingProportional()
    {
        byte[] frame = Wire.Frame(
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody([new Wire.KeyedField(9, [], DeclaredLength: 8 * 1024 * 1024)])
        ]);

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<OldSchema>(frame));
    }

    [Fact]
    public void Deserialize_ALargeUnknownField_IsSkippedWithoutCopyingIt()
    {
        // V0 reads straight from the source instead of buffering the payload, so nothing but the
        // skip itself can account for an allocation here.
        byte[] payload =
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(9, new byte[2 * 1024 * 1024]),
                new Wire.KeyedField(2, [0])
            ])
        ];
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().AllowV0Fallback().Build());

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => serializer.Deserialize<OldSchema>(payload));
    }

    // --- STR-22: the window shares the parent operation's accounting ----------------------------

    [Fact]
    public void Deserialize_ACollectionInsideAKeyedField_SpendsTheOperationElementBudget()
    {
        var contract = new CompleteContract { Numbers = [1, 2, 3, 4] };
        byte[] payload = new BinarySerializer().Serialize(contract);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxTotalElements = 3 })
                .Build());

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<CompleteContract>(payload));
    }

    [Fact]
    public void Deserialize_ANestedObjectInsideAKeyedField_SpendsTheOperationDepthBudget()
    {
        var contract = new NestedSchema { Inner = new NewSchema { Kept = new Node { Value = 1 } } };
        byte[] payload = new BinarySerializer().Serialize(contract);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxDepth = 2 })
                .Build());

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<NestedSchema>(payload));
    }

    [Fact]
    public void Constructor_WithANegativeLength_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WindowReadStream(new MemoryStream(), -1, "Key 1 payload"));
    }
}
