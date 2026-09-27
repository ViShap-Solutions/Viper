using System.Buffers;
using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// B1 — the buffer family and the pooled family over the representative profiles and the core corpus: a write into a
/// caller's <see cref="IBufferWriter{T}"/> (WL-21), a read from a span and from a sequence of several
/// segments (WL-22), and a write into a pooled payload (WL-23). Under B-P5 and B-P6 the WL-21 cells
/// against WL-01 of <see cref="ProfileMatrixBenchmarks"/> are WL-19, an encrypted frame written to a
/// buffer writer against the same frame to a new array.
/// </summary>
/// <remarks>
/// The writer is reset rather than reallocated, so the measurement is the write and not the writer's
/// growth; the sequence is cut into four segments in setup.
/// </remarks>
[MemoryDiagnoser]
public class ProfileBufferBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private ArrayBufferWriter<byte> _destination = null!;
    private byte[] _payload = [];
    private ReadOnlySequence<byte> _segmented;

    public static IEnumerable<ViperProfile> Profiles => ViperProfiles.Representative;

    public static IEnumerable<Dataset> Datasets => Corpus.Core;

    [ParamsSource(nameof(Profiles))]
    public ViperProfile Profile { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);
        _dataset = Data;
        _payload = _dataset.Serialize(_adapter);
        _destination = new ArrayBufferWriter<byte>(_payload.Length);
        _segmented = Segments.Of(_payload, count: 4);
    }

    [Benchmark(Description = "WL-21 serialize → IBufferWriter")]
    public int SerializeToBufferWriter()
    {
        _destination.ResetWrittenCount();
        _dataset.SerializeInto(_adapter, _destination);
        return _destination.WrittenCount;
    }

    [Benchmark(Description = "WL-22 deserialize ← ReadOnlySpan")]
    public object? DeserializeFromSpan() => _dataset.DeserializeSpan(_adapter, _payload);

    [Benchmark(Description = "WL-22 deserialize ← ReadOnlySequence")]
    public object? DeserializeFromSequence() => _dataset.DeserializeSequence(_adapter, _segmented);

    [Benchmark(Description = "WL-23 serialize → PooledPayload")]
    public int SerializePooled() => _dataset.SerializePooled(_adapter);
}

/// <summary>
/// B1 — the asynchronous write family over the representative profiles and the core corpus (WL-24): the frame is
/// built synchronously and only the output is awaited, into a stream and into a pipe. Version 0 writes
/// asynchronously too, so B-P7 is measured here.
/// </summary>
[MemoryDiagnoser]
public class ProfileAsyncWriteBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private MemoryStream _destination = null!;
    private Pipe _pipe = null!;

    public static IEnumerable<ViperProfile> Profiles => ViperProfiles.Representative;

    public static IEnumerable<Dataset> Datasets => Corpus.Core;

    [ParamsSource(nameof(Profiles))]
    public ViperProfile Profile { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);
        _dataset = Data;
        _destination = new MemoryStream(_dataset.Serialize(_adapter).Length);
        _pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false));
    }

    [GlobalCleanup]
    public void Cleanup() => _destination.Dispose();

    [Benchmark(Description = "WL-24 serialize → Stream, awaited")]
    public async Task<long> SerializeToStreamAsync()
    {
        _destination.Position = 0;
        await _dataset.SerializeAsync(_adapter, _destination);
        return _destination.Position;
    }

    [Benchmark(Description = "WL-24 serialize → PipeWriter, awaited")]
    public async Task<long> SerializeToPipeAsync()
    {
        await _dataset.SerializeAsync(_adapter, _pipe.Writer);

        // The pipe is drained in the same invocation, so every invocation writes into an empty pipe;
        // the drain is part of the cell and is the same for every profile.
        var read = await _pipe.Reader.ReadAsync();
        long length = read.Buffer.Length;
        _pipe.Reader.AdvanceTo(read.Buffer.End);
        return length;
    }
}

/// <summary>
/// B1 — the reads that need a frame's declared length, over the representative version 1 profiles and the core corpus:
/// a synchronous read from a stream that cannot seek (WL-18), the path that did not exist before
/// such streams were read, and the asynchronous reads from a stream and from a pipe (WL-24).
/// </summary>
/// <remarks>
/// B-P7 is absent by design: a version 0 payload carries no length, so these entry points refuse it
/// (Contract §10.2). Its synchronous reads are measured by <see cref="ProfileBufferBenchmarks"/> and
/// <see cref="ProfileStreamBenchmarks"/>.
/// </remarks>
[MemoryDiagnoser]
public class ProfileFramedReadBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];
    private ForwardOnlyReadStream _forwardOnly = null!;
    private MemoryStream _source = null!;

    public static IEnumerable<ViperProfile> Profiles => ViperProfiles.RepresentativeFramed;

    public static IEnumerable<Dataset> Datasets => Corpus.Core;

    [ParamsSource(nameof(Profiles))]
    public ViperProfile Profile { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);
        _dataset = Data;
        _payload = _dataset.Serialize(_adapter);
        _forwardOnly = new ForwardOnlyReadStream(_payload);
        _source = new MemoryStream(_payload, writable: false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _forwardOnly.Dispose();
        _source.Dispose();
    }

    [Benchmark(Description = "WL-18 deserialize ← non-seekable Stream")]
    public object? DeserializeFromNonSeekableStream()
    {
        _forwardOnly.Reset();
        return _dataset.DeserializeFrom(_adapter, _forwardOnly);
    }

    [Benchmark(Description = "WL-24 deserialize ← Stream, awaited")]
    public async Task<object?> DeserializeFromStreamAsync()
    {
        _source.Position = 0;
        return await _dataset.DeserializeAsync(_adapter, _source);
    }

    /// <remarks>The reader over the payload is created per invocation, as a pipe is per connection.</remarks>
    [Benchmark(Description = "WL-24 deserialize ← PipeReader, awaited")]
    public async Task<object?> DeserializeFromPipeAsync() =>
        await _dataset.DeserializeAsync(_adapter, PipeReader.Create(new ReadOnlySequence<byte>(_payload)));
}

/// <summary>
/// B1 — a long stream of small frames read one after another with <c>DeserializeAsyncEnumerable</c>
/// (WL-20), from a pipe and from a stream: the cost of one frame, each its own operation, when the
/// frames are already there. Reported per frame.
/// </summary>
[MemoryDiagnoser]
public class FrameStreamBenchmarks
{
    private const int Frames = 1_000;

    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _frames = [];
    private MemoryStream _source = null!;

    public static IEnumerable<ViperProfile> Profiles => [ViperProfile.Default, ViperProfile.ProtectedBrotli];

    public static IEnumerable<Dataset> Datasets => [Corpus.Find("DATA-01")];

    [ParamsSource(nameof(Profiles))]
    public ViperProfile Profile { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);
        _dataset = Data;
        _frames = [.. Enumerable.Range(0, Frames).SelectMany(_ => _dataset.Serialize(_adapter))];
        _source = new MemoryStream(_frames, writable: false);
    }

    [GlobalCleanup]
    public void Cleanup() => _source.Dispose();

    [Benchmark(Description = "WL-20 frames ← PipeReader", OperationsPerInvoke = Frames)]
    public async Task<int> ReadFramesFromPipe() =>
        await _dataset.DeserializeFrames(_adapter, PipeReader.Create(new ReadOnlySequence<byte>(_frames)));

    [Benchmark(Description = "WL-20 frames ← Stream", OperationsPerInvoke = Frames)]
    public async Task<int> ReadFramesFromStream()
    {
        _source.Position = 0;
        return await _dataset.DeserializeFrames(_adapter, _source);
    }
}

/// <summary>
/// A readable source that cannot seek, such as a socket, over bytes the suite rewinds between
/// invocations.
/// </summary>
internal sealed class ForwardOnlyReadStream(byte[] content) : Stream
{
    private readonly MemoryStream _inner = new(content, writable: false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Rewinds the bytes underneath; the stream itself still cannot seek.</summary>
    public void Reset() => _inner.Position = 0;

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Cuts a payload into a sequence of several segments, as a pipe delivers it.</summary>
internal static class Segments
{
    internal static ReadOnlySequence<byte> Of(byte[] payload, int count)
    {
        int size = Math.Max(1, (payload.Length + count - 1) / count);
        Segment? first = null;
        Segment? last = null;

        for (int start = 0; start < payload.Length; start += size)
        {
            var memory = payload.AsMemory(start, Math.Min(size, payload.Length - start));
            last = last is null ? first = new Segment(memory, 0) : last.Append(memory);
        }

        return first is null
            ? ReadOnlySequence<byte>.Empty
            : new ReadOnlySequence<byte>(first, 0, last!, last!.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
