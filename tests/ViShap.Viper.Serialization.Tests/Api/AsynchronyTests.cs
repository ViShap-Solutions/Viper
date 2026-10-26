using System.Buffers;
using System.IO.Pipelines;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-25 and API-26: an asynchronous read awaits one whole version 1 frame and decodes it
/// synchronously, an asynchronous write builds the frame first and awaits only the output, a
/// cancellation or a failure consumes nothing from a pipe, and a version 0 payload — which carries no
/// length — is refused by every asynchronous read while an asynchronous version 0 write succeeds.
/// </summary>
public class AsynchronyTests
{
    private static Person Value => new() { Name = "Ada", Age = 36 };

    private static BinarySerializer Headerless() =>
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    // --- API-25: reading awaits the frame, then decodes it ----------------------------------------

    [Fact]
    public async Task DeserializeAsync_AFrameSplitAcrossManyPipeSegments_IsRead()
    {
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(new Person { Name = new string('a', 2_000), Age = 36 });
        var pipe = new ChunkedPipeReader(frame, chunkSize: 13);

        var restored = await serializer.DeserializeAsync<Person>(pipe);

        Assert.Equal(2_000, restored!.Name.Length);
        Assert.Equal(frame.Length, pipe.Consumed);
    }

    [Fact]
    public async Task DeserializeAsync_APipeCompletedInsideAFrame_ThrowsFormatAndConsumesNothing()
    {
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(Value);
        var pipe = new ChunkedPipeReader(frame[..^3], chunkSize: 5);

        await Assert.ThrowsAsync<BinaryFormatException>(() => serializer.DeserializeAsync<Person>(pipe).AsTask());

        Assert.Equal(0, pipe.Consumed);
    }

    [Fact]
    public async Task DeserializeAsync_APipeCompletedInsideTheHeader_ThrowsFormat()
    {
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(Value);

        foreach (int length in new[] { 1, 4, 5, 6, Wire.ReadHeader(frame).HeaderLength - 1 })
            await Assert.ThrowsAsync<BinaryFormatException>(
                () => serializer.DeserializeAsync<Person>(new ChunkedPipeReader(frame[..length], chunkSize: 3)).AsTask());
    }

    [Fact]
    public async Task DeserializeAsync_CancelledBeforeAnyByte_LeavesThePipeUnconsumed()
    {
        var serializer = new BinarySerializer();
        var pipe = new ChunkedPipeReader([], chunkSize: 4, completeAtEnd: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => serializer.DeserializeAsync<Person>(pipe, cancellation.Token).AsTask());

        Assert.Equal(0, pipe.Consumed);
    }

    [Fact]
    public async Task DeserializeAsync_CancelledInsideAFrame_LeavesTheStartedFrameUnconsumed()
    {
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(Value);
        var pipe = new ChunkedPipeReader(frame[..^2], chunkSize: 6, completeAtEnd: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => serializer.DeserializeAsync<Person>(pipe, cancellation.Token).AsTask());

        Assert.Equal(0, pipe.Consumed);
        Assert.Equal(frame.Length - 2, pipe.Arrived);
    }

    [Fact]
    public async Task DeserializeAsync_AMalformedFrame_ConsumesNothing()
    {
        var serializer = new BinarySerializer();
        byte[] frame = Wire.FrameWith(Wire.Body(serializer.Serialize(Value)), services: [Wire.Service(0, false, [])]);
        var pipe = new ChunkedPipeReader(frame, chunkSize: 64);

        await Assert.ThrowsAsync<BinaryFormatException>(() => serializer.DeserializeAsync<Person>(pipe).AsTask());

        Assert.Equal(0, pipe.Consumed);
    }

    [Fact]
    public async Task DeserializeAsync_ACanceledPendingRead_ThrowsCanceledAndConsumesNothing()
    {
        var serializer = new BinarySerializer();
        var pipe = new ChunkedPipeReader(serializer.Serialize(Value), chunkSize: 4);
        pipe.CancelPendingRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serializer.DeserializeAsync<Person>(pipe).AsTask());

        Assert.Equal(0, pipe.Consumed);
    }

    [Fact]
    public async Task DeserializeAsync_AStreamCancelledBeforeReading_ThrowsCanceled()
    {
        var serializer = new BinarySerializer();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => serializer.DeserializeAsync<Person>(new MemoryStream(serializer.Serialize(Value)), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task DeserializeAsync_FromAStream_LeavesItWhereTheFrameEnds()
    {
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(Value);
        using var stream = new MemoryStream([.. frame, .. frame]);

        await serializer.DeserializeAsync<Person>(stream);

        Assert.Equal(frame.Length, stream.Position);
    }

    // --- API-25: writing builds the frame, then awaits the output ---------------------------------

    [Fact]
    public async Task SerializeAsync_ToAStream_WritesTheBytesOfTheArrayForm()
    {
        var serializer = new BinarySerializer();
        using var destination = new NonSeekableWriteStream();

        await serializer.SerializeAsync(destination, Value);

        Assert.Equal(serializer.Serialize(Value), destination.Written);
    }

    [Fact]
    public async Task SerializeAsync_ToAPipe_WritesTheBytesOfTheArrayForm()
    {
        var serializer = new BinarySerializer();
        var pipe = new Pipe();

        await serializer.SerializeAsync(pipe.Writer, Value);
        await pipe.Writer.CompleteAsync();
        var written = await pipe.Reader.ReadAsync();

        Assert.Equal(serializer.Serialize(Value), written.Buffer.ToArray());
    }

    [Fact]
    public async Task SerializeAsync_CancelledBeforeTheOutputStarts_LeavesTheDestinationEmpty()
    {
        var serializer = new BinarySerializer();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stream = new MemoryStream();
        var pipe = new Pipe();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => serializer.SerializeAsync(stream, Value, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => serializer.SerializeAsync(pipe.Writer, Value, cancellation.Token).AsTask());

        Assert.Equal(0, stream.Length);
        Assert.Equal(0, pipe.Writer.UnflushedBytes);
    }

    [Fact]
    public async Task SerializeAsync_FailingMidGraph_LeavesTheDestinationEmpty()
    {
        var tree = new Tree();
        tree.Add(tree);
        var serializer = new BinarySerializer();
        using var stream = new MemoryStream();
        var pipe = new Pipe();

        await Assert.ThrowsAsync<BinaryTypeException>(() => serializer.SerializeAsync(stream, tree).AsTask());
        await Assert.ThrowsAsync<BinaryTypeException>(() => serializer.SerializeAsync(pipe.Writer, tree).AsTask());

        Assert.Equal(0, stream.Length);
        Assert.Equal(0, pipe.Writer.UnflushedBytes);
    }

    // --- API-26: an asynchronous read refuses version 0; an asynchronous version 0 write succeeds --

    [Fact]
    public async Task EveryAsynchronousRead_MeetingV0_ThrowsNotSupportedNamingTheRule()
    {
        var serializer = Headerless();
        byte[] payload = serializer.Serialize(Value);

        var fromStream = await Assert.ThrowsAsync<NotSupportedException>(
            () => serializer.DeserializeAsync<Person>(new MemoryStream(payload)).AsTask());
        var fromPipe = await Assert.ThrowsAsync<NotSupportedException>(
            () => serializer.DeserializeAsync<Person>(new ChunkedPipeReader(payload, chunkSize: 4)).AsTask());
        var populateStream = await Assert.ThrowsAsync<NotSupportedException>(
            () => serializer.PopulateAsync(new MemoryStream(payload), new Person()).AsTask());
        var populatePipe = await Assert.ThrowsAsync<NotSupportedException>(
            () => serializer.PopulateAsync(new ChunkedPipeReader(payload, chunkSize: 4), new Person()).AsTask());

        Assert.All(
            [fromStream, fromPipe, populateStream, populatePipe],
            ex => Assert.Contains("version 1 frames only", ex.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryAsynchronousRead_MeetingV0_ConsumesNothingFromAPipe()
    {
        var serializer = Headerless();
        var pipe = new ChunkedPipeReader(serializer.Serialize(Value), chunkSize: 32);

        await Assert.ThrowsAsync<NotSupportedException>(() => serializer.DeserializeAsync<Person>(pipe).AsTask());

        Assert.Equal(0, pipe.Consumed);
    }

    [Fact]
    public async Task SerializeAsync_V0_WritesTheBytesOfTheSynchronousForm()
    {
        var serializer = Headerless();
        using var stream = new MemoryStream();
        var pipe = new Pipe();

        await serializer.SerializeAsync(stream, Value);
        await serializer.SerializeAsync(pipe.Writer, Value);
        await pipe.Writer.CompleteAsync();
        var piped = await pipe.Reader.ReadAsync();

        Assert.Equal(serializer.Serialize(Value), stream.ToArray());
        Assert.Equal(serializer.Serialize(Value), piped.Buffer.ToArray());
        Assert.Equal("Ada", serializer.Deserialize<Person>(stream.ToArray())!.Name);
    }
}
