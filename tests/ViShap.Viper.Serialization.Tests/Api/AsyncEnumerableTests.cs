using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-27: <c>DeserializeAsyncEnumerable</c> reads version 1 frames until the source ends. The
/// source ending between two frames completes the enumeration and ending inside one is malformed, each
/// frame is an operation of its own with its own budgets, and a version 0 payload is refused.
/// </summary>
public class AsyncEnumerableTests
{
    private static byte[] Frames(BinarySerializer serializer, int count) =>
        [.. Enumerable.Range(0, count).SelectMany(i => serializer.Serialize(new Person { Name = $"p{i}", Age = i }))];

    private static async Task<List<Person?>> Collect(IAsyncEnumerable<Person?> frames)
    {
        var values = new List<Person?>();
        await foreach (var value in frames)
            values.Add(value);

        return values;
    }

    private static void AssertAreTheFrames(IReadOnlyList<Person?> values, int count)
    {
        Assert.Equal(count, values.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal($"p{i}", values[i]!.Name);
            Assert.Equal(i, values[i]!.Age);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Stream_NFrames_YieldsNValuesAndCompletes(int count)
    {
        var serializer = new BinarySerializer();

        var values = await Collect(serializer.DeserializeAsyncEnumerable<Person>(new NonSeekableStream(Frames(serializer, count))));

        AssertAreTheFrames(values, count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Pipe_NFrames_YieldsNValuesAndCompletes(int count)
    {
        var serializer = new BinarySerializer();
        byte[] frames = Frames(serializer, count);
        var pipe = new ChunkedPipeReader(frames, chunkSize: 11);

        var values = await Collect(serializer.DeserializeAsyncEnumerable<Person>(pipe));

        AssertAreTheFrames(values, count);
        Assert.Equal(frames.Length, pipe.Consumed);
    }

    [Fact]
    public async Task Stream_EndingInsideAFrame_YieldsTheCompleteFramesThenThrowsFormat()
    {
        var serializer = new BinarySerializer();
        byte[] frames = Frames(serializer, 3);
        var values = new List<Person?>();

        await Assert.ThrowsAsync<BinaryFormatException>(async () =>
        {
            await foreach (var value in serializer.DeserializeAsyncEnumerable<Person>(new NonSeekableStream(frames[..^4])))
                values.Add(value);
        });

        AssertAreTheFrames(values, 2);
    }

    [Fact]
    public async Task Pipe_CompletingInsideAFrame_YieldsTheCompleteFramesThenThrowsFormat()
    {
        var serializer = new BinarySerializer();
        byte[] frames = Frames(serializer, 3);
        var pipe = new ChunkedPipeReader(frames[..^4], chunkSize: 9);
        var values = new List<Person?>();

        await Assert.ThrowsAsync<BinaryFormatException>(async () =>
        {
            await foreach (var value in serializer.DeserializeAsyncEnumerable<Person>(pipe))
                values.Add(value);
        });

        AssertAreTheFrames(values, 2);
        Assert.Equal(2 * (frames.Length / 3), pipe.Consumed);
    }

    [Fact]
    public async Task EachFrame_HasItsOwnBudget()
    {
        // Every frame spends most of MaxTotalElements; together they spend it several times over.
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxTotalElements = 100 })
            .Build());
        byte[] frame = serializer.Serialize(Enumerable.Range(0, 90).ToList());
        byte[] frames = [.. frame, .. frame, .. frame, .. frame];

        int read = 0;
        await foreach (var list in serializer.DeserializeAsyncEnumerable<List<int>>(new NonSeekableStream(frames)))
        {
            Assert.Equal(90, list!.Count);
            read++;
        }

        Assert.Equal(4, read);
    }

    [Fact]
    public async Task V0_ThrowsNotSupported()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());
        byte[] payloads = Frames(serializer, 2);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => Collect(serializer.DeserializeAsyncEnumerable<Person>(new MemoryStream(payloads))));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Collect(serializer.DeserializeAsyncEnumerable<Person>(new ChunkedPipeReader(payloads, chunkSize: 8))));
    }

    [Fact]
    public async Task Pipe_CancelledInsideAFrame_LeavesTheStartedFrameUnconsumed()
    {
        var serializer = new BinarySerializer();
        byte[] frames = Frames(serializer, 2);
        int firstLength = serializer.Serialize(new Person { Name = "p0", Age = 0 }).Length;
        var pipe = new ChunkedPipeReader(frames[..^3], chunkSize: 5, completeAtEnd: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var values = new List<Person?>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var value in serializer.DeserializeAsyncEnumerable<Person>(pipe, cancellation.Token))
                values.Add(value);
        });

        AssertAreTheFrames(values, 1);
        Assert.Equal(firstLength, pipe.Consumed);
    }
}
