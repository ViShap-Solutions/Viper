using System.Buffers;
using System.IO.Pipelines;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins ALC-01 to ALC-09: the allocation targets of the typed engine, each stated as what the engine
/// adds to the frame around it. A frame costs its buffers and its header whatever it carries, so every
/// target is measured against the same frame holding nothing — a null root — or against the same call
/// through another entry point, and the difference must be exactly the objects the caller receives.
/// Allocations are counted on the current thread after the path is warm.
/// </summary>
public class AllocationTests
{
    private static readonly BinarySerializer V1 = new();

    private static readonly BinarySerializer V0 =
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    private static readonly BinarySerializer References =
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    // --- ALC-01: writing into a buffer writer --------------------------------------------------

    [Fact]
    public void Serialize_ARecordIntoABufferWriter_V1_AllocatesNothingBeyondTheFrame()
    {
        var destination = new ArrayBufferWriter<byte>(4096);
        var value = Record();

        long record = Allocated(() => Write(V1, destination, value));
        long empty = Allocated(() => Write<Mixed?>(V1, destination, null));

        Assert.Equal(empty, record);
    }

    [Fact]
    public void Serialize_ARecordIntoABufferWriter_V0_AllocatesNothingBeyondTheFrame()
    {
        var destination = new ArrayBufferWriter<byte>(4096);
        var value = Record();

        long record = Allocated(() => Write(V0, destination, value));
        long empty = Allocated(() => Write<Mixed?>(V0, destination, null));

        Assert.Equal(empty, record);
    }

    // --- ALC-02 and ALC-03: the array and the pooled payload -----------------------------------

    [Fact]
    public void Serialize_ToAnArray_AllocatesExactlyTheArrayBeyondTheBufferWriterPath()
    {
        var destination = new ArrayBufferWriter<byte>(4096);
        var value = Record();
        int length = V1.Serialize(value).Length;

        long toArray = Allocated(() => V1.Serialize(value));
        long toWriter = Allocated(() => Write(V1, destination, value));
        long array = Allocated(() => _ = new byte[length]);

        Assert.Equal(toWriter + array, toArray);
    }

    [Fact]
    public void SerializePooled_AllocatesExactlyOnePooledPayloadBeyondTheBufferWriterPath()
    {
        var destination = new ArrayBufferWriter<byte>(4096);
        var value = Record();
        byte[] rented = new byte[16];

        long pooled = Allocated(() => V1.SerializePooled(value).Dispose());
        long toWriter = Allocated(() => Write(V1, destination, value));
        long payload = Allocated(() => _ = new PooledPayload(rented, 1));

        Assert.Equal(toWriter + payload, pooled);
    }

    // --- ALC-04 and ALC-05: reading ------------------------------------------------------------

    [Fact]
    public void Deserialize_ARecordOfPrimitivesFromASpan_AllocatesExactlyTheRecord()
    {
        byte[] payload = V1.Serialize(Primitives());
        byte[] empty = V1.Serialize<Primitive?>(null);

        long record = Allocated(() => V1.Deserialize<Primitive>(payload));
        long frame = Allocated(() => V1.Deserialize<Primitive?>(empty));
        long instance = Allocated(() => _ = Primitives());

        Assert.Equal(frame + instance, record);
    }

    [Fact]
    public void Deserialize_AGraphWhoseCountsAreBacked_AllocatesExactlyTheGraph()
    {
        // No intermediate copy and no reallocation: the list is created at its final capacity and
        // holds exactly the records read into it.
        var graph = Enumerable.Range(0, 256).Select(_ => Primitives()).ToList();
        byte[] payload = V1.Serialize(graph);
        byte[] empty = V1.Serialize<List<Primitive>?>(null);

        long read = Allocated(() => V1.Deserialize<List<Primitive>>(payload));
        long frame = Allocated(() => V1.Deserialize<List<Primitive>?>(empty));
        long built = Allocated(() =>
        {
            var list = new List<Primitive>(256);
            for (int i = 0; i < 256; i++)
                list.Add(Primitives());
        });

        Assert.Equal(frame + built, read);
    }

    // --- ALC-06: reference framing -------------------------------------------------------------

    [Fact]
    public void Serialize_WithPreserveReferences_AllocatesAsWithout()
    {
        var destination = new ArrayBufferWriter<byte>(1 << 16);
        var graph = Enumerable.Range(0, 256).Select(_ => Record()).ToList();

        Assert.Equal(
            Allocated(() => Write(V1, destination, graph)),
            Allocated(() => Write(References, destination, graph)));
    }

    [Fact]
    public void Deserialize_WithPreserveReferences_AllocatesAsWithout()
    {
        var graph = Enumerable.Range(0, 256).Select(_ => Primitives()).ToList();
        byte[] plain = V1.Serialize(graph);
        byte[] framed = References.Serialize(graph);

        Assert.Equal(
            Allocated(() => V1.Deserialize<List<Primitive>>(plain)),
            Allocated(() => V1.Deserialize<List<Primitive>>(framed)));
    }

    // --- ALC-07 and ALC-08: the asynchronous methods -------------------------------------------

    [Fact]
    public void SerializeAsync_AddsNothingPerValueOverTheSynchronousPath()
    {
        // The engine runs synchronously under an asynchronous method, so a larger graph costs the
        // asynchronous path exactly what it costs the synchronous one.
        var small = Enumerable.Range(0, 16).Select(_ => Record()).ToList();
        var large = Enumerable.Range(0, 1024).Select(_ => Record()).ToList();
        var stream = new MemoryStream(new byte[1 << 20], 0, 1 << 20, writable: true, publiclyVisible: true);

        long synchronous = Allocated(() => ToStream(stream, large)) - Allocated(() => ToStream(stream, small));
        long asynchronous =
            Allocated(() => ToStreamAsync(stream, large)) - Allocated(() => ToStreamAsync(stream, small));

        Assert.Equal(synchronous, asynchronous);
    }

    [Fact]
    public void DeserializeAsync_AddsNothingPerValueOverTheSynchronousPath()
    {
        // An asynchronous read gathers the frame in a buffer that doubles as bytes arrive, so the two
        // frames compared here are chosen to need the same number of reads: what differs between them
        // is only the values, and those must cost the asynchronous path what they cost the other.
        byte[] smaller = V1.Serialize(Enumerable.Range(0, 620).Select(_ => Primitives()).ToList());
        byte[] larger = V1.Serialize(Enumerable.Range(0, 1150).Select(_ => Primitives()).ToList());
        Assert.Equal(BitOperations.Log2((uint)smaller.Length - 1), BitOperations.Log2((uint)larger.Length - 1));

        var small = new MemoryStream(smaller);
        var large = new MemoryStream(larger);

        long synchronous = Allocated(() => FromStream(large)) - Allocated(() => FromStream(small));
        long asynchronous = Allocated(() => FromStreamAsync(large)) - Allocated(() => FromStreamAsync(small));

        Assert.Equal(synchronous, asynchronous);
    }

    [Fact]
    public void DeserializeAsync_FromAPipe_AddsNothingPerValueOverTheSpanPath()
    {
        byte[] small = V1.Serialize(Enumerable.Range(0, 16).Select(_ => Primitives()).ToList());
        byte[] large = V1.Serialize(Enumerable.Range(0, 1024).Select(_ => Primitives()).ToList());

        long span = Allocated(() => V1.Deserialize<List<Primitive>>(large)) -
                    Allocated(() => V1.Deserialize<List<Primitive>>(small));

        long pipe = Allocated(() => FromPipeAsync(large)) - Allocated(() => FromPipeAsync(small));

        Assert.Equal(span, pipe);
    }

    // --- ALC-09: pooled state machines --------------------------------------------------------

    [Fact]
    public void EveryAsynchronousMethod_ReturningAValueTask_PoolsItsStateMachine()
    {
        // A method that suspends boxes its state machine; the pooling builder takes the box from a
        // pool instead of allocating one per suspension.
        var methods = typeof(BinarySerializer).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null)
            .Where(method => method.ReturnType == typeof(ValueTask) ||
                             method.ReturnType.IsGenericType &&
                             method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var builder = method.GetCustomAttribute<AsyncMethodBuilderAttribute>()?.BuilderType;
            Assert.True(
                builder == typeof(PoolingAsyncValueTaskMethodBuilder) ||
                builder == typeof(PoolingAsyncValueTaskMethodBuilder<>),
                $"{method.DeclaringType!.Name}.{method.Name} does not pool its state machine.");
        });
    }

    public enum Tone : byte
    {
        Low,
        High
    }

    /// <summary>A record of primitives and strings, as the write targets name it.</summary>
    public sealed class Mixed
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public double Score { get; set; }

        public Tone Tone { get; set; }

        public string? Note { get; set; }

        public long Stamp { get; set; }

        public bool Active { get; set; }
    }

    /// <summary>A record of primitives, as the read target names it: reading it allocates only itself.</summary>
    public sealed class Primitive
    {
        public int Id { get; set; }

        public double Score { get; set; }

        public Tone Tone { get; set; }

        public long Stamp { get; set; }

        public bool Active { get; set; }

        public decimal Amount { get; set; }

        public Guid Key { get; set; }
    }

    private static Mixed Record() => new()
    {
        Id = 42,
        Name = "Ada Lovelace",
        Score = 0.75,
        Tone = Tone.High,
        Note = "analytical engine",
        Stamp = 1_700_000_000,
        Active = true
    };

    private static Primitive Primitives() => new()
    {
        Id = 7,
        Score = 1.5,
        Tone = Tone.Low,
        Stamp = 99,
        Active = true,
        Amount = 12.5m,
        Key = SampleKey
    };

    private static readonly Guid SampleKey = new(1, 2, 3, [4, 5, 6, 7, 8, 9, 10, 11]);

    private static void Write<T>(BinarySerializer serializer, ArrayBufferWriter<byte> destination, T value)
    {
        destination.ResetWrittenCount();
        serializer.Serialize(destination, value);
    }

    private static void ToStream<T>(MemoryStream stream, T value)
    {
        stream.Position = 0;
        V1.Serialize(stream, value);
    }

    private static void ToStreamAsync<T>(MemoryStream stream, T value)
    {
        stream.Position = 0;
        V1.SerializeAsync(stream, value).GetAwaiter().GetResult();
    }

    private static void FromStream(MemoryStream stream)
    {
        stream.Position = 0;
        V1.Deserialize<List<Primitive>>(stream);
    }

    private static void FromStreamAsync(MemoryStream stream)
    {
        stream.Position = 0;
        V1.DeserializeAsync<List<Primitive>>(stream).GetAwaiter().GetResult();
    }

    private static void FromPipeAsync(byte[] payload)
    {
        var pipe = PipeReader.Create(new System.Buffers.ReadOnlySequence<byte>(payload));
        V1.DeserializeAsync<List<Primitive>>(pipe).GetAwaiter().GetResult();
    }

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
