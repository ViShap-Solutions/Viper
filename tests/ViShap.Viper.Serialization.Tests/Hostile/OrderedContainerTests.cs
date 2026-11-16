using ViShap.Viper.Formatters;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-52: the entries of a <see cref="SortedList{TKey,TValue}"/> arrive in whatever order the
/// payload chose, so they are gathered beside the list and added in ascending order once the map is
/// complete — no entry is inserted in front of another while it is read — and the list is what a
/// reference resolves to from its first occurrence; a repeated key is still malformed.
/// </summary>
public class OrderedContainerTests
{
    private static readonly BinarySerializer Serializer = new();

    [Fact]
    public void SortedListShape_AddsNothingToTheListUntilTheMapCompletes()
    {
        var shape = new SortedListShape<int, int>();
        var builder = shape.Create(1_000);
        var list = (SortedList<int, int>)shape.Instance(builder);

        for (int key = 999; key >= 0; key--)
            shape.Add(ref builder, key, -key);

        Assert.Empty(list);

        var completed = shape.Complete(builder);

        Assert.Same(list, completed);
        Assert.Equal(Enumerable.Range(0, 1_000), completed.Keys);
        Assert.Equal(Enumerable.Range(0, 1_000).Select(key => -key), completed.Values);
    }

    [Fact]
    public void Deserialize_SortedListWrittenInDescendingOrder_ReadsSorted()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write7BitEncodedInt(5_000 + 1);
            for (int key = 4_999; key >= 0; key--)
            {
                writer.Write(key);
                writer.Write(key * 3);
            }
        }));

        var read = Serializer.Deserialize<SortedList<int, int>>(frame)!;

        Assert.Equal(Enumerable.Range(0, 5_000), read.Keys);
        Assert.Equal(Enumerable.Range(0, 5_000).Select(key => key * 3), read.Values);
        Assert.Equal(Serializer.Serialize(new SortedList<int, int>(read)), Serializer.Serialize(read));
    }

    [Fact]
    public void Deserialize_SortedListWithARepeatedKey_ThrowsFormat()
    {
        foreach (int[] keys in new[] { new[] { 3, 1, 3 }, new[] { 1, 1 }, new[] { 9, 2, 5, 2 } })
        {
            byte[] frame = Wire.Frame(Wire.Payload(writer =>
            {
                writer.Write7BitEncodedInt(keys.Length + 1);
                foreach (int key in keys)
                {
                    writer.Write(key);
                    writer.Write(0);
                }
            }));

            Assert.Throws<BinaryFormatException>(() => Serializer.Deserialize<SortedList<int, int>>(frame));
        }
    }

    [Fact]
    public void Deserialize_SortedListUnderReferences_IsTheInstanceItsMembersPointTo()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build());
        var holder = new SortedListHolder();
        holder.Entries[2] = holder;
        holder.Entries[1] = new SortedListHolder();
        holder.Entries[1].Entries[7] = holder;

        var read = serializer.Deserialize<SortedListHolder>(serializer.Serialize(holder))!;

        Assert.Same(read, read.Entries[2]);
        Assert.Same(read, read.Entries[1].Entries[7]);
        Assert.Equal([1, 2], read.Entries.Keys);
    }
}

public sealed class SortedListHolder
{
    public SortedList<int, SortedListHolder> Entries { get; set; } = [];
}
