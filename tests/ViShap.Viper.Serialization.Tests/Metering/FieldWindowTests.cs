using System.Runtime.CompilerServices;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Metering;

/// <summary>
/// Pins STR-17…STR-22: <see cref="WireReader.Slice"/> is a reader over exactly one declared keyed
/// field. A field decoder may consume its declared length and not one byte more, running past it is
/// a malformed payload rather than a limit violation, and an unknown field is skipped without being
/// copied.
/// </summary>
public class FieldWindowTests
{
    /// <summary>Bytes the whole operation may allocate. Generous, and orders below the declarations.</summary>
    private const long AllocationCeiling = 1024 * 1024;

    private static OperationBox Operation() => new();

    private static byte[] Counting(int length) => [.. Enumerable.Range(0, length).Select(value => (byte)value)];

    // --- STR-17: exactly the declared length ----------------------------------------------------

    [Fact]
    public void Slice_ReadsTheDeclaredLength()
    {
        var reader = new WireReader(Counting(16), ref Operation().State);
        var window = reader.Slice(4, "Key 1 payload");

        byte[] destination = new byte[4];
        window.ReadExact(destination, "Field");

        Assert.Equal([0, 1, 2, 3], destination);
        Assert.Equal(0, window.Remaining);
    }

    [Fact]
    public void Slice_MovesTheParentPastTheField()
    {
        var reader = new WireReader(Counting(16), ref Operation().State);

        reader.Slice(4, "Key 1 payload");

        Assert.Equal(4, reader.Consumed);
        Assert.Equal(4, reader.ReadByte());
    }

    [Fact]
    public void Slice_Remaining_IsTheDeclaredLength()
    {
        var reader = new WireReader(Counting(16), ref Operation().State);

        Assert.Equal(4, reader.Slice(4, "Key 1 payload").Remaining);
    }

    // --- STR-18 / STR-19: the boundary is the end of the field -----------------------------------

    [Fact]
    public void Read_PastTheWindow_IsAMalformedPayloadRatherThanTheNextField()
    {
        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(Counting(16), ref Operation().State, new WireBudget("wire", 16));
            var window = reader.Slice(4, "Key 1 payload");
            window.ReadExact(new byte[4], "Field");
            window.ReadByte();
        });

        Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact]
    public void RequireAvailable_BeyondTheWindow_IsAMalformedPayloadAndNotALimitViolation()
    {
        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(Counting(16), ref Operation().State, new WireBudget("wire", 16));
            reader.Slice(4, "Key 1 payload").RequireAvailable(8, "String byte length");
        });

        Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact]
    public void Slice_DeclaringMoreThanTheParentHolds_IsClassifiedByTheParent()
    {
        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(Counting(4), ref Operation().State);
            reader.Slice(16, "Key 1 payload");
        });

        Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact]
    public void Deserialize_AFieldDeclaringMoreThanItsWindowHolds_ThrowsFormat()
    {
        // The string claims eight bytes inside a field that declares four.
        byte[] frame = Wire.Frame(
        [
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
        // Key 2 of OldSchema decodes a Node, which is shorter than the seven bytes declared.
        byte[] frame = Wire.Frame(
        [
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(2, [1, 7, 0, 0, 0, 9, 9])
            ])
        ]);

        AssertEx.Throws<BinaryFormatException>(
            "trailing", () => new BinarySerializer().Deserialize<OldSchema>(frame));
    }

    // --- STR-20: skipping consumes the field without reading it ----------------------------------

    [Fact]
    public void Skip_ConsumesTheRestOfTheWindowOnly()
    {
        var reader = new WireReader(Counting(16), ref Operation().State);
        var window = reader.Slice(4, "Key 1 payload");
        window.ReadByte();

        window.Skip(window.Remaining, "Key 1 payload");

        Assert.Equal(0, window.Remaining);
        Assert.Equal(4, window.Consumed);
        Assert.Equal(4, reader.Consumed);
    }

    [Fact]
    public void Skip_BeyondTheBytes_ThrowsFormat()
    {
        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(Counting(4), ref Operation().State);
            reader.Skip(16, "Key 1 payload");
        });

        Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact]
    public void Skip_OverALargeWindow_AllocatesNothingProportional()
    {
        byte[] content = new byte[4 * 1024 * 1024];

        AssertEx.AllocatesLessThan(AllocationCeiling, () =>
        {
            var reader = new WireReader(content, ref Operation().State);
            var window = reader.Slice(content.Length, "Key 1 payload");
            window.Skip(window.Remaining, "Key 1 payload");
        });
    }

    // --- STR-21: the window never materializes the field -----------------------------------------

    [Fact]
    public void Deserialize_AnUnknownFieldDeclaringMoreThanIsPresent_AllocatesNothingProportional()
    {
        byte[] frame = Wire.Frame(
        [
            .. Wire.KeyedBody([new Wire.KeyedField(9, [], DeclaredLength: 8 * 1024 * 1024)])
        ]);

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<OldSchema>(frame));
    }

    [Fact]
    public void Deserialize_ALargeUnknownField_IsSkippedWithoutCopyingIt()
    {
        // A V0 payload read from an array is decoded where it lies, so nothing but the skip itself
        // could account for an allocation here.
        byte[] payload =
        [
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
    public void Slice_SharesTheParentOperation()
    {
        var operation = Operation();
        var reader = new WireReader(Counting(16), ref operation.State);

        var window = reader.Slice(4, "Key 1 payload");

        Assert.True(Unsafe.AreSame(ref operation.State, ref window.State));
    }
}
