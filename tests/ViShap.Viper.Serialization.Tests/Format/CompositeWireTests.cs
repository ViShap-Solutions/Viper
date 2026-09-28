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

    /// <summary>The payload bytes of a V1 frame, with the header removed.</summary>
    private byte[] Payload<T>(T value) => Wire.Body(_serializer.Serialize(value));

    [Fact]
    public void KeyValuePair_IsTheKeyThenTheValue()
    {
        Assert.Equal<byte[]>(
            [
                2, 0x61,                // key "a": length 1 + 1, bytes
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
                2, 0x61                 // Item2
            ],
            Payload((7, "a")));
    }

    [Fact]
    public void Tuple_IsAFlagThenItsItemsInDeclarationOrder()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // the reference-typed tuple begins with no number: a flag
                0x07, 0, 0, 0,          // Item1
                2, 0x61                 // Item2
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
    public void Lazy_IsAFlagThenItsMaterializedValue()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // the Lazy itself is not null
                0x07, 0, 0, 0           // the materialized value
            ],
            Payload(new Lazy<int>(() => 7)));
    }

    [Fact]
    public void ImmutableArray_IsItsCountPlusOneThenElements()
    {
        Assert.Equal<byte[]>(
            [
                3,                      // count 2, plus one: not default
                0x07, 0, 0, 0,
                0x09, 0, 0, 0
            ],
            Payload(ImmutableArray.Create(7, 9)));
    }

    [Fact]
    public void ImmutableArray_DefaultIsZeroAndStaysDistinctFromEmpty()
    {
        Assert.Equal<byte[]>([0x00], Payload(default(ImmutableArray<int>)));
        Assert.Equal<byte[]>([0x01], Payload(ImmutableArray<int>.Empty));
        Assert.Equal<byte[]>([0x04, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0], Payload(ImmutableArray.Create(1, 2, 3)));

        Assert.True(_serializer.Deserialize<ImmutableArray<int>>(
            _serializer.Serialize(default(ImmutableArray<int>))).IsDefault);
        Assert.True(_serializer.Deserialize<ImmutableArray<int>>(
            _serializer.Serialize(ImmutableArray<int>.Empty)).IsEmpty);
    }

    [Fact]
    public void NullableImmutableArray_IsTheNullableFlagThenTheFold()
    {
        Assert.Equal<byte[]>([0x00], Payload<ImmutableArray<int>?>(null));
        Assert.Equal<byte[]>([0x01, 0x00], Payload<ImmutableArray<int>?>(default(ImmutableArray<int>)));
        Assert.Equal<byte[]>([0x01, 0x01], Payload<ImmutableArray<int>?>(ImmutableArray<int>.Empty));

        var restored = _serializer.Deserialize<ImmutableArray<int>?>(
            _serializer.Serialize<ImmutableArray<int>?>(default(ImmutableArray<int>)));
        Assert.True(restored!.Value.IsDefault);
    }

    [Fact]
    public void ImmutableArray_UnderReferences_IsNeverFramed()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build());

        Assert.Equal<byte[]>([0x02, 0x07, 0, 0, 0], Wire.Body(serializer.Serialize(ImmutableArray.Create(7))));
    }

    [Fact]
    public void MultiDimensionalArray_IsRankPlusOneThenLengthsThenRowMajorElements()
    {
        var grid = new int[2, 3];
        int next = 1;
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 3; column++)
                grid[row, column] = next++;

        Assert.Equal<byte[]>(
            [
                3,                      // rank 2, plus one: not null
                2,                      // dimension 0 length
                3,                      // dimension 1 length
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
                3,                      // rank 2, plus one
                0,                      // dimension 0 length
                3                       // dimension 1 length
            ],
            Payload(new int[0, 3]));
    }

    [Fact]
    public void Deserialize_MultiDimensionalArrayDeclaringTheWrongRank_ThrowsFormat()
    {
        // rank 3, plus one, for a declared rank-2 array
        byte[] frame = Wire.Frame([0x04, 1, 1, 1]);

        AssertEx.Throws<BinaryFormatException>(
            "does not match the declared array rank",
            () => _serializer.Deserialize<int[,]>(frame));
    }
}
