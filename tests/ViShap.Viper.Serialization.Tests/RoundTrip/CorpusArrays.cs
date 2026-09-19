using System.Buffers;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Plan §19.3: RT-43…RT-52, arrays and the memory-like shapes of contract §23. A memory-like value
/// travels as the sequence of §22.3 — a count and its elements — so what is asserted here is the
/// elements it yields, which is all the wire carries.
/// </summary>
public abstract partial class Corpus
{
    [Fact]
    public void Deserialize_PrimitiveArray_RestoresEveryElement()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new[] { 1, 2, 3 })!);
        Assert.Equal([], RoundTrip(Array.Empty<int>())!);
        Assert.Equal([42], RoundTrip(new[] { 42 })!);
        Assert.Equal([1.5, double.NaN], RoundTrip(new[] { 1.5, double.NaN })!);

        Assert.Equal(typeof(int[]), RoundTrip(new[] { 1, 2, 3 })!.GetType());
    }

    [Fact]
    public void Deserialize_ReferenceArray_RestoresNullElements()
    {
        var restored = RoundTrip(new[] { "a", null, "c" })!;

        Assert.Equal(3, restored.Length);
        Assert.Equal("a", restored[0]);
        Assert.Null(restored[1]);
        Assert.Equal("c", restored[2]);
    }

    [Fact]
    public void Deserialize_ObjectArray_RestoresEachElementSeparately()
    {
        var restored = RoundTrip(new[]
        {
            new Person { Name = "Ada", Age = 36 },
            null,
            new Person { Name = "Grace", Age = 45 }
        })!;

        Assert.Equal("Ada", restored[0]!.Name);
        Assert.Null(restored[1]);
        Assert.Equal(45, restored[2]!.Age);
        Assert.NotSame(restored[0], restored[2]);
    }

    [Fact]
    public void Deserialize_NullArray_RestoresNull()
    {
        Assert.Null(RoundTrip<int[]?>(null));
        Assert.Null(RoundTrip<string[]?>(null));
        Assert.Null(RoundTrip<int[,]?>(null));
    }

    [Fact]
    public void Deserialize_JaggedArray_RestoresEachRow()
    {
        var restored = RoundTrip(new[] { new[] { 1, 2 }, [3], Array.Empty<int>() })!;

        Assert.Equal(3, restored.Length);
        Assert.Equal([1, 2], restored[0]);
        Assert.Equal([3], restored[1]);
        Assert.Empty(restored[2]);
    }

    [Fact]
    public void Deserialize_Rank2Array_RestoresEveryCellInRowMajorOrder()
    {
        var grid = new int[2, 3];
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 3; column++)
                grid[row, column] = (row * 10) + column;

        var restored = RoundTrip(grid)!;

        Assert.Equal(2, restored.Rank);
        Assert.Equal(2, restored.GetLength(0));
        Assert.Equal(3, restored.GetLength(1));
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 3; column++)
                Assert.Equal((row * 10) + column, restored[row, column]);
    }

    [Fact]
    public void Deserialize_Rank3Array_RestoresEveryCellInRowMajorOrder()
    {
        var cube = new string[2, 3, 4];
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 4; z++)
                    cube[x, y, z] = $"{x}-{y}-{z}";

        var restored = RoundTrip(cube)!;

        Assert.Equal(3, restored.Rank);
        Assert.Equal(2, restored.GetLength(0));
        Assert.Equal(3, restored.GetLength(1));
        Assert.Equal(4, restored.GetLength(2));
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 4; z++)
                    Assert.Equal($"{x}-{y}-{z}", restored[x, y, z]);
    }

    [Fact]
    public void Deserialize_MultidimensionalArrayWithAZeroDimension_RestoresTheShape()
    {
        var restored = RoundTrip(new int[0, 3])!;

        Assert.Equal(2, restored.Rank);
        Assert.Equal(0, restored.GetLength(0));
        Assert.Equal(3, restored.GetLength(1));
        Assert.Empty(restored);

        var allZero = RoundTrip(new int[0, 0])!;
        Assert.Equal(0, allZero.GetLength(0));
        Assert.Equal(0, allZero.GetLength(1));
    }

    [Fact]
    public void Deserialize_Memory_RestoresEveryElement()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new Memory<int>([1, 2, 3])).ToArray());
        Assert.Equal([], RoundTrip(Memory<int>.Empty).ToArray());
        Assert.Equal<string?[]>(["a", null], RoundTrip(new Memory<string?>(["a", null])).ToArray());
    }

    [Fact]
    public void Deserialize_ReadOnlyMemory_RestoresEveryElement()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyMemory<int>([1, 2, 3])).ToArray());
        Assert.Equal([], RoundTrip(ReadOnlyMemory<int>.Empty).ToArray());
    }

    [Fact]
    public void Deserialize_MemorySlice_RestoresTheSliceAndNotTheBackingArray()
    {
        // A slice is a sequence of its own elements (§22.3): the bytes outside it never travel.
        var restored = RoundTrip(new Memory<int>([1, 2, 3, 4, 5]).Slice(1, 3));

        Assert.Equal([2, 3, 4], restored.ToArray());
        Assert.Equal(3, restored.Length);
    }

    [Fact]
    public void Deserialize_ArraySegmentWithAnOffset_RestoresTheSegmentedElements()
    {
        // RT-50: only the segment's own elements are on the wire, so §23 has the read build a fresh
        // array and wrap the whole of it — offset zero, and nothing of the original around it.
        var segment = new ArraySegment<int>([1, 2, 3, 4, 5], 1, 3);

        var restored = RoundTrip(segment);

        Assert.Equal([2, 3, 4], restored.ToArray());
        Assert.Equal(3, restored.Count);
        Assert.Equal(0, restored.Offset);
        Assert.Equal(3, restored.Array!.Length);

        Assert.Empty(RoundTrip(new ArraySegment<int>([1, 2, 3], 3, 0)).ToArray());
    }

    [Fact]
    public void Deserialize_SingleSegmentReadOnlySequence_RestoresEveryElement()
    {
        var restored = RoundTrip(new ReadOnlySequence<int>([1, 2, 3]));

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.Equal(3, restored.Length);
        Assert.Equal([], RoundTrip(ReadOnlySequence<int>.Empty).ToArray());
    }

    [Fact]
    public void Deserialize_MultiSegmentReadOnlySequence_RestoresTheElementsInOrder()
    {
        // RT-52: the sequence carries a count and its elements, so §23 has the segmentation drop out
        // — three segments go in, the concatenation comes back as one.
        var source = Sequences.Of([1, 2], [3], [4, 5]);
        Assert.False(source.IsSingleSegment);

        var restored = RoundTrip(source);

        Assert.Equal([1, 2, 3, 4, 5], restored.ToArray());
        Assert.Equal(5, restored.Length);
        Assert.True(restored.IsSingleSegment);
    }
}
