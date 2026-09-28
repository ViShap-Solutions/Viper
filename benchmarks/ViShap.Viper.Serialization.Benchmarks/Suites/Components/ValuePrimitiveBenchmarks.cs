using BenchmarkDotNet.Attributes;
using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>One primitive of the wire format of §22.1, measured on its own.</summary>
public enum PayloadPrimitive
{
    Byte,
    Int16,
    Int32,
    Int64,
    Double,
    Decimal,

    /// <summary>A 7-bit encoded integer that fits one byte — the length prefix of a short field.</summary>
    VarIntOneByte,

    /// <summary>A 7-bit encoded integer that needs all five bytes — a prefix at the top of its range.</summary>
    VarIntFiveBytes,
}

/// <summary>
/// MICRO-01 — the fixed-width and 7-bit encoded primitives of <see cref="WireWriter"/> and
/// <see cref="WireReader"/>, in both directions.
/// </summary>
/// <remarks>
/// Explains WL-01 and WL-04 for every dataset: the payload cost of a value is a sum of these calls,
/// so a profile-matrix row that surprises is read against this table before anything else. One
/// invocation is a thousand calls, and the cell is per call. A write invocation fills the payload
/// buffer from empty and returns its segments to the pool, as one serialization does, so the cell
/// carries that buffer's share of a thousand calls.
/// </remarks>
[MemoryDiagnoser]
public class ValuePrimitiveBenchmarks
{
    private const int Operations = 1_000;

    private OperationState _operation;
    private PayloadBuffer _destination = null!;
    private byte[] _payload = [];

    [Params(
        PayloadPrimitive.Byte,
        PayloadPrimitive.Int16,
        PayloadPrimitive.Int32,
        PayloadPrimitive.Int64,
        PayloadPrimitive.Double,
        PayloadPrimitive.Decimal,
        PayloadPrimitive.VarIntOneByte,
        PayloadPrimitive.VarIntFiveBytes)]
    public PayloadPrimitive Primitive { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.Operation();

        _destination = new PayloadBuffer(_operation.Limits.MaxPayloadBytes, "payload");
        _payload = ComponentFixtures.Encode(
            ref _operation, (ref WireWriter writer) => WriteAll(ref writer, Primitive));
    }

    [GlobalCleanup]
    public void Cleanup() => _destination.Dispose();

    [Benchmark(Description = "MICRO-01 write", OperationsPerInvoke = Operations)]
    public long Write()
    {
        var writer = new WireWriter(_destination, ref _operation);
        WriteAll(ref writer, Primitive);
        writer.Flush();

        long written = _destination.Length;
        _destination.Dispose();
        return written;
    }

    [Benchmark(Description = "MICRO-01 read", OperationsPerInvoke = Operations)]
    public long Read()
    {
        var reader = new WireReader(_payload, ref _operation);
        return ReadAll(ref reader, Primitive);
    }

    /// <summary>
    /// The primitive is selected once and the loop runs inside the branch, so the timed region holds
    /// a thousand calls to one method and no dispatch of its own.
    /// </summary>
    private static void WriteAll(ref WireWriter writer, PayloadPrimitive primitive)
    {
        switch (primitive)
        {
            case PayloadPrimitive.Byte:
                for (var i = 0; i < Operations; i++)
                {
                    writer.WriteByte(0x5A);
                }

                break;

            case PayloadPrimitive.Int16:
                for (var i = 0; i < Operations; i++)
                {
                    writer.WriteInt16(0x5AA5);
                }

                break;

            case PayloadPrimitive.Int32:
                for (var i = 0; i < Operations; i++)
                {
                    writer.WriteInt32(0x5AA55AA5);
                }

                break;

            case PayloadPrimitive.Int64:
                for (var i = 0; i < Operations; i++)
                {
                    writer.WriteInt64(0x5AA55AA55AA55AA5);
                }

                break;

            case PayloadPrimitive.Double:
                for (var i = 0; i < Operations; i++)
                {
                    writer.WriteDouble(1.7976931348623157e208);
                }

                break;

            case PayloadPrimitive.Decimal:
                for (var i = 0; i < Operations; i++)
                {
                    writer.WriteDecimal(79228162514264.337593543950335m);
                }

                break;

            case PayloadPrimitive.VarIntOneByte:
                for (var i = 0; i < Operations; i++)
                {
                    writer.Write7BitEncodedInt(0x7F);
                }

                break;

            case PayloadPrimitive.VarIntFiveBytes:
                for (var i = 0; i < Operations; i++)
                {
                    writer.Write7BitEncodedInt(int.MaxValue);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(primitive));
        }
    }

    private static long ReadAll(ref WireReader reader, PayloadPrimitive primitive)
    {
        long consumed = 0;

        switch (primitive)
        {
            case PayloadPrimitive.Byte:
                for (var i = 0; i < Operations; i++)
                {
                    consumed += reader.ReadByte();
                }

                break;

            case PayloadPrimitive.Int16:
                for (var i = 0; i < Operations; i++)
                {
                    consumed += reader.ReadInt16();
                }

                break;

            case PayloadPrimitive.Int32:
                for (var i = 0; i < Operations; i++)
                {
                    consumed += reader.ReadInt32();
                }

                break;

            case PayloadPrimitive.Int64:
                for (var i = 0; i < Operations; i++)
                {
                    consumed += reader.ReadInt64();
                }

                break;

            case PayloadPrimitive.Double:
                for (var i = 0; i < Operations; i++)
                {
                    consumed += (long)reader.ReadDouble();
                }

                break;

            case PayloadPrimitive.Decimal:
                for (var i = 0; i < Operations; i++)
                {
                    consumed += (long)reader.ReadDecimal();
                }

                break;

            case PayloadPrimitive.VarIntOneByte:
            case PayloadPrimitive.VarIntFiveBytes:
                for (var i = 0; i < Operations; i++)
                {
                    consumed += reader.Read7BitEncodedInt("MICRO-01");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(primitive));
        }

        return consumed;
    }
}

/// <summary>
/// MICRO-01 — the two length-prefixed primitives, where the payload rather than the call dominates:
/// a UTF-8 string and a byte blob, in both directions, at four sizes.
/// </summary>
/// <remarks>
/// Explains the string end of SCALE-05 and the blob end of SCALE-02: both curves are these calls with
/// a traversal around them. One invocation is one call, so the cell is per string or per blob.
/// </remarks>
[MemoryDiagnoser]
public class ValueTextBenchmarks
{
    private OperationState _operation;
    private PayloadBuffer _destination = null!;
    private byte[] _stringPayload = [];
    private byte[] _blobPayload = [];
    private string _text = string.Empty;
    private byte[] _blob = [];

    [Params(8, 256, 4_096, 65_536)]
    public int Bytes { get; set; }

    /// <summary>
    /// A multi-byte string encodes to more bytes than it has characters, so the two columns separate
    /// what encoding costs from what moving bytes costs at one encoded size.
    /// </summary>
    [Params(false, true)]
    public bool MultiByte { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.Operation();

        _text = MultiByte
            ? string.Concat(Enumerable.Repeat("日", Bytes / 3))
            : new string('a', Bytes);

        _blob = new DataSets.DeterministicRandom(0x0000_1801).NextBytes(Bytes);

        _destination = new PayloadBuffer(_operation.Limits.MaxPayloadBytes, "payload");

        _stringPayload = ComponentFixtures.Encode(
            ref _operation, (ref WireWriter writer) => writer.WriteString(_text));

        _blobPayload = ComponentFixtures.Encode(
            ref _operation, (ref WireWriter writer) => writer.WriteBlob(_blob, "MICRO-01"));
    }

    [GlobalCleanup]
    public void Cleanup() => _destination.Dispose();

    [Benchmark(Description = "MICRO-01 write string")]
    public long WriteString()
    {
        var writer = new WireWriter(_destination, ref _operation);
        writer.WriteString(_text);
        writer.Flush();

        long written = _destination.Length;
        _destination.Dispose();
        return written;
    }

    [Benchmark(Description = "MICRO-01 read string")]
    public string ReadString()
    {
        var reader = new WireReader(_stringPayload, ref _operation);
        return reader.ReadString();
    }

    [Benchmark(Description = "MICRO-01 write blob")]
    public long WriteBlob()
    {
        var writer = new WireWriter(_destination, ref _operation);
        writer.WriteBlob(_blob, "MICRO-01");
        writer.Flush();

        long written = _destination.Length;
        _destination.Dispose();
        return written;
    }

    [Benchmark(Description = "MICRO-01 read blob")]
    public byte[] ReadBlob()
    {
        var reader = new WireReader(_blobPayload, ref _operation);
        return reader.ReadBlob("MICRO-01");
    }
}
