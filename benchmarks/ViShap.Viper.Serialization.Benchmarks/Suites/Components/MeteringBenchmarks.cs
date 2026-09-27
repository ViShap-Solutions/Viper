using BenchmarkDotNet.Attributes;
using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-09 — metering and windowing over buffers (Contract §7), against a bare copy of the same
/// bytes: the <see cref="PayloadBuffer"/> budget on write, the <see cref="WireReader"/> budget on
/// read, the field window of <see cref="WireReader.Slice"/>, and the copy of a finished buffer to a
/// stream destination.
/// </summary>
/// <remarks>
/// One invocation moves 64 KB in 256 calls, and the cell is per call, because what metering adds is a
/// per-call comparison rather than a per-byte cost. The bare rows are the same 256 copies between
/// arrays, so the overhead of metering is a difference on this table alone.
/// <para>
/// The budgets are set to a ceiling a run cannot reach. The check each performs is the one a real
/// budget performs — a single comparison — so what this measures is the cost of metering rather than
/// the cost of a particular ceiling. A write buffer is rented, filled and returned per invocation, as
/// one serialization does, so the write cell carries that lifecycle amortized over its 256 calls; a
/// reader and a window are constructed per invocation for the same reason. The copy cell moves the
/// whole 64 KB of a filled buffer to a stream once per invocation and is reported per 256 bytes.
/// </para>
/// <para>
/// Explains DIFF-06, where the stream family is compared with the <c>byte[]</c> family end to end.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class MeteringBenchmarks
{
    private const int Chunk = 256;
    private const int Chunks = 256;
    private const int Total = Chunk * Chunks;

    private readonly byte[] _buffer = new byte[Chunk];

    private SerializationOperation _operation = null!;
    private byte[] _payload = [];
    private byte[] _bareDestination = [];
    private PayloadBuffer _filled = null!;
    private MemoryStream _streamDestination = null!;

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.Operation();
        _payload = new byte[Total];
        _bareDestination = new byte[Total];

        _filled = new PayloadBuffer(long.MaxValue, "MICRO-09");
        _filled.GetSpan(Total).Clear();
        _filled.Advance(Total);

        _streamDestination = new MemoryStream(Total);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _filled.Dispose();
        _streamDestination.Dispose();
    }

    [Benchmark(Description = "MICRO-09 bare write", OperationsPerInvoke = Chunks)]
    public int BareWrite()
    {
        var destination = _bareDestination.AsSpan();

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            _buffer.CopyTo(destination[(chunk * Chunk)..]);
        }

        return destination.Length;
    }

    [Benchmark(Description = "MICRO-09 metered write", OperationsPerInvoke = Chunks)]
    public long MeteredWrite()
    {
        using var buffer = new PayloadBuffer(long.MaxValue, "MICRO-09");

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            _buffer.CopyTo(buffer.GetSpan(Chunk));
            buffer.Advance(Chunk);
        }

        return buffer.Length;
    }

    [Benchmark(Description = "MICRO-09 bare read", OperationsPerInvoke = Chunks)]
    public int BareRead()
    {
        ReadOnlySpan<byte> source = _payload;
        var read = 0;

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            source.Slice(read, Chunk).CopyTo(_buffer);
            read += Chunk;
        }

        return read;
    }

    [Benchmark(Description = "MICRO-09 metered read", OperationsPerInvoke = Chunks)]
    public long MeteredRead()
    {
        var reader = new WireReader(_payload, _operation, new WireBudget("wire", long.MaxValue));

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            reader.ReadExact(_buffer, "MICRO-09");
        }

        return reader.Consumed;
    }

    [Benchmark(Description = "MICRO-09 window read", OperationsPerInvoke = Chunks)]
    public long WindowRead()
    {
        var reader = new WireReader(_payload, _operation);
        var window = reader.Slice(Total, "MICRO-09");

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            window.ReadExact(_buffer, "MICRO-09");
        }

        return window.Consumed;
    }

    [Benchmark(Description = "MICRO-09 window skip", OperationsPerInvoke = Chunks)]
    public long WindowSkip()
    {
        var reader = new WireReader(_payload, _operation);
        var window = reader.Slice(Total, "MICRO-09");

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            window.Skip(Chunk, "MICRO-09");
        }

        return window.Consumed;
    }

    [Benchmark(Description = "MICRO-09 copy to stream", OperationsPerInvoke = Chunks)]
    public long CopyToStream()
    {
        _streamDestination.Position = 0;
        _filled.WriteTo(_streamDestination);
        return _streamDestination.Position;
    }
}
