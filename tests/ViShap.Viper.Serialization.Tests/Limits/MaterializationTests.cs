using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins TYP-03 (INV-3, contract §17): a declared count decides how memory is taken for it. When the
/// bytes that remain could hold the array itself, it is allocated at its final length and read into;
/// when they could not, the elements are gathered in a pooled buffer as they arrive and one array of
/// the final length is created at the end. Both paths yield the same value, and a count the bytes do
/// not back still fails on truncation without an allocation in proportion to it. A collection's
/// capacity follows the same rule.
/// </summary>
public class MaterializationTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void ReadArray_ACountBackedByBytes_AllocatesOnlyTheFinalArray()
    {
        int[] values = [.. Enumerable.Range(0, 1000)];
        byte[] bytes = Wire.Payload(writer =>
        {
            writer.Write(values.Length);
            foreach (int value in values)
                writer.Write(value);
        });

        var operation = new OperationBox();
        long allocated = Allocated(() => ReadArray<int>(operation, bytes));
        long array = Allocated(() => _ = new int[1000]);

        Assert.Equal(array, allocated);
        Assert.Equal(values, ReadArray<int>(operation, bytes));
    }

    [Fact]
    public void ReadArray_ACountNotBackedByBytes_YieldsTheSameValueAsABackedOne()
    {
        // A null string is one byte on the wire and a reference in memory, so a thousand of them do
        // not back an array of a thousand references; a thousand eight-character strings do.
        string?[] nulls = new string?[1000];
        string?[] words = [.. Enumerable.Range(0, 1000).Select(i => $"w{i:D7}")];

        Assert.False(IsBacked<string?>(Count(nulls.Length), Remaining(nulls)));
        Assert.True(IsBacked<string?>(Count(words.Length), Remaining(words)));

        Assert.Equal(nulls, _serializer.Deserialize<string?[]>(_serializer.Serialize(nulls)));
        Assert.Equal(words, _serializer.Deserialize<string?[]>(_serializer.Serialize(words)));
    }

    [Fact]
    public void Deserialize_ACountNotBackedByBytes_FailsOnTruncationWithoutAProportionalAllocation()
    {
        // A million elements declared, eight bytes delivered.
        byte[] payload = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(1_000_000);
            writer.Write(1);
            writer.Write(2);
        }));

        Assert.Throws<BinaryFormatException>(() => _serializer.Deserialize<int[]>(payload));
        AssertEx.AllocatesLessThan(64 * 1024, () => _serializer.Deserialize<int[]>(payload));
    }

    [Fact]
    public void Deserialize_ACountOfReferencesNotBackedByBytes_FailsWithoutAProportionalAllocation()
    {
        byte[] payload = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(1_000_000);
            writer.Write(false);
        }));

        Assert.Throws<BinaryFormatException>(() => _serializer.Deserialize<string?[]>(payload));
        AssertEx.AllocatesLessThan(64 * 1024, () => _serializer.Deserialize<string?[]>(payload));
    }

    [Fact]
    public void Deserialize_ACollectionWhoseCountIsBacked_StartsAtExactlyThatCapacity()
    {
        var list = _serializer.Deserialize<List<int>>(_serializer.Serialize(Enumerable.Range(0, 3000).ToList()));

        Assert.Equal(3000, list!.Count);
        Assert.Equal(3000, list.Capacity);
    }

    [Fact]
    public void Deserialize_ACollectionWhoseCountIsNotBacked_GrowsFromTheCapacityHint()
    {
        // Three thousand elements declared at four bytes each, twelve bytes delivered: the list is
        // created at the growth hint, not at the declared count, and the read fails on truncation.
        byte[] payload = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(3000);
            writer.Write(1);
            writer.Write(2);
            writer.Write(3);
        }));

        Assert.Throws<BinaryFormatException>(() => _serializer.Deserialize<List<int>>(payload));
        Assert.False(IsBacked<int>(Count(3000), 12));
        Assert.Equal(1024, Count(3000).CapacityFor(sizeof(int), 12));
        Assert.Equal(3000, Count(3000).CapacityFor(sizeof(int), 12_000));
    }

    private static T[] ReadArray<T>(OperationBox operation, byte[] payload)
    {
        var reader = new WireReader(payload, ref operation.State);
        var count = reader.ReadCount(CountKind.Array, "Array length");
        return Elements.ReadArray<T>(ref reader, count);
    }

    private static ElementCount Count(int value)
    {
        var operation = new OperationBox();
        return ElementCount.Validate(value, CountKind.Array, ref operation.State, "Array length");
    }

    private static bool IsBacked<T>(ElementCount count, long remaining) =>
        count.IsBackedBy(System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), remaining);

    /// <summary>The bytes an array's elements occupy on the wire, after its count.</summary>
    private long Remaining(string?[] values) =>
        new BinarySerializer(BinarySerializerOptions.Configure().WithVersion(0).Build()).Serialize(values).Length
        - 1 - sizeof(int);

    /// <summary>
    /// The bytes one call allocates on this thread once it is warm: the least of several calls, since
    /// a pool the collector trimmed in the meantime can only add to a call, never take from it.
    /// </summary>
    private static long Allocated(Action act)
    {
        for (int i = 0; i < 4; i++)
            act();

        long least = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            act();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }
}
