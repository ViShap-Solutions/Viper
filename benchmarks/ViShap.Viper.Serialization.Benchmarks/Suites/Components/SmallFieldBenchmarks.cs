using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-17 — a positional record of many booleans and small integers, written and read through the
/// engine: the shape whose cost is one or two bytes per member, so the price of each primitive call
/// is what the cell shows rather than something a string or a blob averages away.
/// </summary>
/// <remarks>
/// Explains the small-scalar end of WL-01 and WL-04 and the member term of SCALE-04: a record like
/// this is a member loop over one-byte and two-byte primitives with nothing else to hide their cost.
/// One invocation is one record of thirty-two members. A write invocation fills the payload buffer
/// from empty and returns its segments to the pool, as one serialization does.
/// </remarks>
[MemoryDiagnoser]
public class SmallFieldBenchmarks
{
    private SerializationOperation _operation = null!;
    private PayloadBuffer _destination = null!;
    private GraphWriter _graphWriter = null!;
    private GraphReader _graphReader = null!;
    private SmallFieldRecord _record = null!;
    private byte[] _payload = [];

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.UnboundedTotals();
        _destination = new PayloadBuffer(_operation.Limits.MaxPayloadBytes, "payload");
        _graphWriter = new GraphWriter(_operation);
        _graphReader = new GraphReader(_operation);
        _record = SmallFieldRecord.Sample();

        _payload = ComponentFixtures.Encode(
            _operation,
            (ref WireWriter writer) => _graphWriter.WriteValue(ref writer, _record, typeof(SmallFieldRecord)));
    }

    [GlobalCleanup]
    public void Cleanup() => _destination.Dispose();

    [Benchmark(Description = "MICRO-17 write small-field record")]
    public long Write()
    {
        var writer = new WireWriter(_destination, _operation);
        _graphWriter.WriteValue(ref writer, _record, typeof(SmallFieldRecord));
        writer.Flush();

        long written = _destination.Length;
        _destination.Dispose();
        return written;
    }

    [Benchmark(Description = "MICRO-17 read small-field record")]
    public object? Read()
    {
        var reader = new WireReader(_payload, _operation);
        return _graphReader.ReadValue(ref reader, typeof(SmallFieldRecord));
    }
}

/// <summary>MICRO-17 — sixteen flags, eight bytes and eight 16-bit integers, all positional.</summary>
internal sealed class SmallFieldRecord
{
    public bool F00 { get; set; }
    public bool F01 { get; set; }
    public bool F02 { get; set; }
    public bool F03 { get; set; }
    public bool F04 { get; set; }
    public bool F05 { get; set; }
    public bool F06 { get; set; }
    public bool F07 { get; set; }
    public bool F08 { get; set; }
    public bool F09 { get; set; }
    public bool F10 { get; set; }
    public bool F11 { get; set; }
    public bool F12 { get; set; }
    public bool F13 { get; set; }
    public bool F14 { get; set; }
    public bool F15 { get; set; }

    public byte B0 { get; set; }
    public byte B1 { get; set; }
    public byte B2 { get; set; }
    public byte B3 { get; set; }
    public byte B4 { get; set; }
    public byte B5 { get; set; }
    public byte B6 { get; set; }
    public byte B7 { get; set; }

    public short S0 { get; set; }
    public short S1 { get; set; }
    public short S2 { get; set; }
    public short S3 { get; set; }
    public short S4 { get; set; }
    public short S5 { get; set; }
    public short S6 { get; set; }
    public short S7 { get; set; }

    /// <summary>Alternating flags and distinct small values, so no member takes a trivial path.</summary>
    public static SmallFieldRecord Sample() => new()
    {
        F00 = true, F02 = true, F04 = true, F06 = true, F08 = true, F10 = true, F12 = true, F14 = true,
        B0 = 1, B1 = 3, B2 = 5, B3 = 7, B4 = 11, B5 = 13, B6 = 17, B7 = 19,
        S0 = -2, S1 = 300, S2 = -400, S3 = 500, S4 = -600, S5 = 700, S6 = -800, S7 = 900,
    };
}
