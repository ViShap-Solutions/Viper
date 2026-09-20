using System.Collections.Immutable;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins WF-25, WF-26 and WF-27: the composite encodings of §22.5. Each one has a fixed,
/// type-determined child layout, so the engine has already charged depth and nodes and the bytes
/// below carry no count of their own except where the table says otherwise.
/// </summary>
public class CompositeWireTests
{
    private readonly BinarySerializer _serializer = new();

    /// <summary>The payload bytes of a V1 frame, with the fixed-length header removed.</summary>
    private byte[] Payload<T>(T value) => _serializer.Serialize(value)[Wire.PlainHeaderLength..];

    [Fact]
    public void KeyValuePair_IsTheKeyThenTheValue()
    {
        Assert.Equal<byte[]>(
            [
                1, 1, 0x61,             // key "a": non-null, length 1, bytes
                0x07, 0, 0, 0           // value 7
            ],
            Payload(new KeyValuePair<string, int>("a", 7)));
    }

    [Fact]
    public void ValueTuple_IsItsItemsInDeclarationOrder()
    {
        Assert.Equal<byte[]>(
            [
                0x07, 0, 0, 0,          // Item1
                1, 1, 0x61              // Item2
            ],
            Payload((7, "a")));
    }

    [Fact]
    public void Tuple_IsItsItemsInDeclarationOrder()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // the reference-typed tuple is non-null
                0x07, 0, 0, 0,          // Item1
                1, 1, 0x61              // Item2
            ],
            Payload(Tuple.Create(7, "a")));
    }

    [Fact]
    public void LongTuple_ContinuesIntoTRest()
    {
        // An eight-item ValueTuple is seven items plus a nested one-item TRest.
        Assert.Equal<byte[]>(
            [
                1, 0, 0, 0,
                2, 0, 0, 0,
                3, 0, 0, 0,
                4, 0, 0, 0,
                5, 0, 0, 0,
                6, 0, 0, 0,
                7, 0, 0, 0,
                8, 0, 0, 0
            ],
            Payload((1, 2, 3, 4, 5, 6, 7, 8)));
    }

    [Fact]
    public void Lazy_IsItsMaterializedValue()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // the Lazy itself is non-null
                0x07, 0, 0, 0           // the materialized value
            ],
            Payload(new Lazy<int>(() => 7)));
    }

    [Fact]
    public void ImmutableArray_WritesAPresentFlagThenACountAndElements()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // present
                2, 0, 0, 0,             // count
                0x07, 0, 0, 0,
                0x09, 0, 0, 0
            ],
            Payload(ImmutableArray.Create(7, 9)));
    }

    [Fact]
    public void ImmutableArray_DefaultWritesAFalseFlagAndStaysDistinctFromEmpty()
    {
        Assert.Equal<byte[]>([0], Payload(default(ImmutableArray<int>)));
        Assert.Equal<byte[]>([1, 0, 0, 0, 0], Payload(ImmutableArray<int>.Empty));

        Assert.True(_serializer.Deserialize<ImmutableArray<int>>(
            _serializer.Serialize(default(ImmutableArray<int>))).IsDefault);
        Assert.True(_serializer.Deserialize<ImmutableArray<int>>(
            _serializer.Serialize(ImmutableArray<int>.Empty)).IsEmpty);
    }

    [Fact]
    public void MultiDimensionalArray_IsRankThenLengthsThenRowMajorElements()
    {
        var grid = new int[2, 3];
        int next = 1;
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 3; column++)
                grid[row, column] = next++;

        Assert.Equal<byte[]>(
            [
                1,                      // non-null
                2, 0, 0, 0,             // rank
                2, 0, 0, 0,             // dimension 0 length
                3, 0, 0, 0,             // dimension 1 length
                1, 0, 0, 0,             // [0,0]
                2, 0, 0, 0,             // [0,1]
                3, 0, 0, 0,             // [0,2]
                4, 0, 0, 0,             // [1,0]
                5, 0, 0, 0,             // [1,1]
                6, 0, 0, 0              // [1,2]
            ],
            Payload(grid));
    }

    [Fact]
    public void MultiDimensionalArray_WithAZeroDimension_WritesTheShapeAndNoElements()
    {
        Assert.Equal<byte[]>(
            [
                1,
                2, 0, 0, 0,             // rank
                0, 0, 0, 0,             // dimension 0 length
                3, 0, 0, 0              // dimension 1 length
            ],
            Payload(new int[0, 3]));
    }

    [Fact]
    public void Deserialize_MultiDimensionalArrayDeclaringTheWrongRank_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(
            Wire.Payload(writer =>
            {
                writer.Write(true);
                writer.Write(3);        // rank 3 for a declared rank-2 array
                writer.Write(1);
                writer.Write(1);
                writer.Write(1);
            }));

        AssertEx.Throws<BinaryFormatException>(
            "does not match the declared array rank",
            () => _serializer.Deserialize<int[,]>(frame));
    }
}
