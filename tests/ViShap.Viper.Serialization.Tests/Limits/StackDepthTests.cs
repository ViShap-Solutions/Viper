using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-61: a graph within <c>MaxDepth</c> that the calling thread's stack cannot hold is a
/// <see cref="BinaryLimitException"/> on write and on read, never the end of the stack — the limit is
/// raised far above the graph, so only the stack can refuse it; on a thread with room for it, a graph
/// at the default limit is written and read whole. Each call runs on a thread of its own with the
/// stated stack.
/// </summary>
public class StackDepthTests
{
    private const int Depth = 511;

    private const int DeepDepth = 5_000;

    private const int SmallStack = 256 * 1024;

    private const int LargeStack = 64 * 1024 * 1024;

    private static readonly BinarySerializer Serializer = new();

    private static readonly BinarySerializerOptions Deep =
        BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxDepth = 100_000 })
            .Build();

    private static readonly BinarySerializer Unlimited = new(Deep);

    [Fact]
    public void DeepGraphs_OnASmallStack_AreALimitOnWriteAndOnRead()
    {
        byte[] keyed = OnThread(LargeStack, () => Unlimited.Serialize(KeyedLink.Chain(DeepDepth)));
        byte[] positional = OnThread(LargeStack, () => Unlimited.Serialize(PositionalLink.Chain(DeepDepth)));
        byte[] lists = OnThread(LargeStack, () => Unlimited.Serialize(NestedList.Of(DeepDepth)));

        AssertLimit(() => Unlimited.Serialize(KeyedLink.Chain(DeepDepth)));
        AssertLimit(() => Unlimited.Deserialize<KeyedLink>(keyed));
        AssertLimit(() => Unlimited.Serialize(PositionalLink.Chain(DeepDepth)));
        AssertLimit(() => Unlimited.Deserialize<PositionalLink>(positional));
        AssertLimit(() => Unlimited.Serialize(NestedList.Of(DeepDepth)));
        AssertLimit(() => Unlimited.Deserialize<NestedList>(lists));

        var dump = OnThread(SmallStack, () => ViShap.Viper.Diagnostics.BinaryFormatDumper.Dump<KeyedLink>(keyed, Deep));
        Assert.IsType<BinaryLimitException>(dump.Failure);
    }

    [Fact]
    public void DeepGraphs_OnALargeStack_RoundTripAtTheDepthLimit()
    {
        OnThread(LargeStack, () =>
        {
            var keyed = Serializer.Deserialize<KeyedLink>(Serializer.Serialize(KeyedLink.Chain(Depth)))!;
            var positional = Serializer.Deserialize<PositionalLink>(Serializer.Serialize(PositionalLink.Chain(Depth)))!;
            var lists = Serializer.Deserialize<NestedList>(Serializer.Serialize(NestedList.Of(Depth)))!;

            Assert.Equal(Depth, keyed.Length());
            Assert.Equal(Depth, positional.Length());
            Assert.Equal(Depth, lists.Depth());
            return 0;
        });
    }

    private static void AssertLimit(Func<object?> call)
    {
        var thrown = OnThread(SmallStack, () =>
        {
            try
            {
                call();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        });

        Assert.IsType<BinaryLimitException>(thrown);
    }

    private static T OnThread<T>(int stack, Func<T> call)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = call();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }, stack);

        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("The call failed on its thread.", failure);

        return result;
    }
}

[BinaryContract]
public sealed class KeyedLink
{
    [BinaryKey(1)] public int Value { get; set; }

    [BinaryKey(2)] public KeyedLink? Next { get; set; }

    public static KeyedLink Chain(int depth)
    {
        var first = new KeyedLink();
        var link = first;
        for (int i = 1; i < depth; i++)
            link = link.Next = new KeyedLink { Value = i };

        return first;
    }

    public int Length()
    {
        int length = 0;
        for (var link = this; link is not null; link = link.Next)
            length++;

        return length;
    }
}

public sealed class PositionalLink
{
    public int Value { get; set; }

    public PositionalLink? Next { get; set; }

    public static PositionalLink Chain(int depth)
    {
        var first = new PositionalLink();
        var link = first;
        for (int i = 1; i < depth; i++)
            link = link.Next = new PositionalLink { Value = i };

        return first;
    }

    public int Length()
    {
        int length = 0;
        for (var link = this; link is not null; link = link.Next)
            length++;

        return length;
    }
}

public sealed class NestedList : List<NestedList>
{
    public static NestedList Of(int depth)
    {
        var first = new NestedList();
        var list = first;
        for (int i = 1; i < depth; i++)
        {
            var inner = new NestedList();
            list.Add(inner);
            list = inner;
        }

        return first;
    }

    public int Depth()
    {
        int depth = 1;
        for (var list = this; list.Count > 0; list = list[0])
            depth++;

        return depth;
    }
}
