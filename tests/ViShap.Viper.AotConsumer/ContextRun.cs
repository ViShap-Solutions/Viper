using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Contracts;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// The serializer used only through the type information of generated contexts, with the reflection
/// path switched off: every line it prints is a check and its result, and every encoded value is printed
/// as the hash of its bytes, so the output of a native AOT build and of a JIT build can be compared line
/// for line. It exits with 1 when a check fails.
/// </summary>
internal static class ContextRun
{
    private static readonly List<string> Failures = [];

    public static async Task<int> Run()
    {
        Check(AppContext.TryGetSwitch("ViShap.Viper.ReflectionPath.IsEnabled", out bool enabled) && !enabled,
            "the reflection path is switched off");

        Fixtures();
        Conformance();
        await EntryPoints();
        Collection();
        Unavailable();
        Uncontracted();

        Console.WriteLine(Failures.Count == 0 ? "all checks passed" : $"{Failures.Count} check(s) failed");
        return Failures.Count == 0 ? 0 : 1;
    }

    // --- the frozen payloads, read and written back -----------------------------------------------------

    private static void Fixtures()
    {
        var contracts = GeneratedCompatibilityContracts.Default;
        var plain = Serializer(contracts, builder => builder);
        var referencing = Serializer(contracts, builder => builder.PreserveReferences());
        var headerless = Serializer(contracts, builder => builder.WithVersion(0).AllowV0Fallback());

        Reproduce("v1-primitives.bin", plain, contracts.FrozenPrimitives);
        Reproduce("v1-time-system.bin", plain, contracts.FrozenTimeAndSystem);
        Reproduce("v1-numerics.bin", plain, contracts.FrozenNumerics);
        Reproduce("v1-collections.bin", plain, contracts.FrozenCollections);
        Reproduce("v1-composites.bin", plain, contracts.FrozenComposites);
        Reproduce("v1-keyed.bin", plain, contracts.FrozenKeyed);
        Reproduce("v1-union.bin", plain, contracts.FrozenShape);
        Reproduce("v1-absences.bin", plain, contracts.FrozenAbsences);
        Reproduce("v1-references.bin", referencing, contracts.FrozenGraph);
        Reproduce("v0-primitives.bin", headerless, contracts.FrozenPrimitives);
    }

    private static void Reproduce<T>(string fixture, BinarySerializer serializer, BinaryTypeInfo<T> info)
    {
        byte[] frozen = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Wire", fixture));
        byte[] written = serializer.Serialize(serializer.Deserialize(frozen, info)!, info);

        Check(written.AsSpan().SequenceEqual(frozen), $"{fixture} read and written back {Hash(written)}");
    }

    // --- the conformance shapes ---------------------------------------------------------------------------

    private static void Conformance()
    {
        var contracts = GeneratedConformanceContracts.Default;
        var serializer = Serializer(contracts, builder => builder);

        var positional = new ConformancePositional { Flag = true, Id = 3, Name = "p", Origin = new ConformancePoint { X = 1, Y = -1 } };
        positional.SetRatio(0.5);
        var keyed = new ConformanceKeyed { Id = 7, Name = "Ada", Big = long.MaxValue, Defaulted = 9 };
        keyed.SetCode(-2);
        var derived = new ConformanceDerived { Alpha = 1, Mike = 2, Zulu = 3 };
        derived.SetHidden(4);

        RoundTrip(serializer, positional, contracts.ConformancePositional);
        RoundTrip(serializer, keyed, contracts.ConformanceKeyed);
        RoundTrip(serializer, derived, contracts.ConformanceDerived);
        RoundTrip(serializer, new ConformanceKeyedBase { First = 1, Third = 3 }, contracts.ConformanceKeyedBase);
        RoundTrip(serializer, new ConformanceKeyedDerived { First = 1, Second = 2, Third = 3 }, contracts.ConformanceKeyedDerived);
        RoundTrip(serializer, new ConformanceShadowed { Value = "new" }, contracts.ConformanceShadowed);
        RoundTrip(serializer, new ConformanceOverridden { Value = 5, Other = 6 }, contracts.ConformanceOverridden);
        RoundTrip<ConformanceShape>(serializer, new ConformanceCircle { Label = "c", Radius = 2 }, contracts.ConformanceShape);
        RoundTrip<ConformanceShape>(serializer, new ConformanceSquare { Label = "s", Side = 3 }, contracts.ConformanceShape);
        RoundTrip(serializer, new ConformancePoint { X = 9, Y = 8 }, contracts.ConformancePoint);
        RoundTrip(serializer, new ConformanceKeyedPoint { X = 4, Tag = "t" }, contracts.ConformanceKeyedPoint);
    }

    private static void RoundTrip<T>(BinarySerializer serializer, T value, BinaryTypeInfo<T> info)
    {
        byte[] written = serializer.Serialize(value, info);
        byte[] again = serializer.Serialize(serializer.Deserialize(written, info)!, info);

        Check(again.AsSpan().SequenceEqual(written), $"{typeof(T).Name} written, read and written again {Hash(written)}");
    }

    // --- every entry point that takes type information ----------------------------------------------------

    private static async Task EntryPoints()
    {
        var contracts = GeneratedConformanceContracts.Default;
        var options = BinarySerializerOptions.Configure().WithContracts(contracts).Build();
        var serializer = new BinarySerializer(options);
        var info = contracts.ConformanceKeyed;

        var value = new ConformanceKeyed { Id = 1, Name = "entry", Big = 2 };
        byte[] frame = serializer.Serialize(value, info);
        byte[] twice = [.. frame, .. frame];

        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, value, info);
        Check(buffer.WrittenSpan.SequenceEqual(frame), "Serialize to a buffer writer");

        using (var pooled = serializer.SerializePooled(value, info))
            Check(pooled.Span.SequenceEqual(frame), "SerializePooled");

        var stream = new MemoryStream();
        serializer.Serialize(stream, value, info);
        Check(stream.ToArray().AsSpan().SequenceEqual(frame), "Serialize to a stream");

        stream = new MemoryStream();
        await serializer.SerializeAsync(stream, value, info);
        Check(stream.ToArray().AsSpan().SequenceEqual(frame), "SerializeAsync to a stream");

        var pipe = new Pipe();
        await serializer.SerializeAsync(pipe.Writer, value, info);
        await pipe.Writer.CompleteAsync();
        var read = await pipe.Reader.ReadAsync();
        Check(read.Buffer.ToArray().AsSpan().SequenceEqual(frame), "SerializeAsync to a pipe");
        pipe.Reader.AdvanceTo(read.Buffer.End);

        Check(Same(value, serializer.Deserialize(frame, info)), "Deserialize a span");
        Check(Same(value, serializer.Deserialize(twice, info, out int bytesConsumed)) && bytesConsumed == frame.Length, "Deserialize a span, bytes consumed");
        Check(Same(value, serializer.Deserialize(new ReadOnlySequence<byte>(frame), info)), "Deserialize a sequence");
        Check(Same(value, serializer.Deserialize(new ReadOnlySequence<byte>(twice), info, out SequencePosition _)), "Deserialize a sequence, position");
        Check(Same(value, serializer.Deserialize(new MemoryStream(frame), info)), "Deserialize a stream");
        Check(Same(value, await serializer.DeserializeAsync(new MemoryStream(frame), info)), "DeserializeAsync a stream");
        Check(Same(value, await serializer.DeserializeAsync(PipeReader.Create(new ReadOnlySequence<byte>(frame)), info)), "DeserializeAsync a pipe");

        int count = 0;
        await foreach (var each in serializer.DeserializeAsyncEnumerable(new MemoryStream(twice), info))
            count += Same(value, each) ? 1 : 0;
        await foreach (var each in serializer.DeserializeAsyncEnumerable(PipeReader.Create(new ReadOnlySequence<byte>(twice)), info))
            count += Same(value, each) ? 1 : 0;
        Check(count == 4, "DeserializeAsyncEnumerable of a stream and of a pipe");

        var target = new ConformanceKeyed();
        serializer.Populate(frame, target, info);
        Check(Same(value, target), "Populate from a span");
        target = new ConformanceKeyed();
        serializer.Populate(twice, target, info, out bytesConsumed);
        Check(Same(value, target) && bytesConsumed == frame.Length, "Populate from a span, bytes consumed");
        target = new ConformanceKeyed();
        serializer.Populate(new ReadOnlySequence<byte>(frame), target, info);
        Check(Same(value, target), "Populate from a sequence");
        target = new ConformanceKeyed();
        serializer.Populate(new ReadOnlySequence<byte>(twice), target, info, out SequencePosition _);
        Check(Same(value, target), "Populate from a sequence, position");
        target = new ConformanceKeyed();
        serializer.Populate(new MemoryStream(frame), target, info);
        Check(Same(value, target), "Populate from a stream");
        target = new ConformanceKeyed();
        await serializer.PopulateAsync(new MemoryStream(frame), target, info);
        Check(Same(value, target), "PopulateAsync from a stream");
        target = new ConformanceKeyed();
        await serializer.PopulateAsync(PipeReader.Create(new ReadOnlySequence<byte>(frame)), target, info);
        Check(Same(value, target), "PopulateAsync from a pipe");

        var dump = BinaryFormatDumper.Dump(frame, info, options);
        Check(dump.Failure is null && dump.Root is not null, "Dump a span");
        Check(BinaryFormatDumper.Dump(new ReadOnlySequence<byte>(frame), info, options).ToString() == dump.ToString(), "Dump a sequence");
        Check(BinaryFormatDumper.DumpValue(value, info, options).ToString() == dump.ToString(), "DumpValue");
        Check(BinaryFormatDumper.Compare(frame, frame, info, options) is null, "Compare");
    }

    private static bool Same(ConformanceKeyed expected, ConformanceKeyed? actual) =>
        actual is not null && (expected.Id, expected.Name, expected.Big, expected.Defaulted, expected.GetCode()) ==
        (actual.Id, actual.Name, actual.Big, actual.Defaulted, actual.GetCode());

    // --- a custom collection, created and filled by the shape --------------------------------------------

    private static void Collection()
    {
        var context = BasketContext.Default;
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());
        var basket = new Basket { 3, 1, 4, 1, 5 };

        byte[] written = serializer.Serialize(basket, context.Basket);
        var read = serializer.Deserialize(written, context.Basket);

        Check(read is not null && read.SequenceEqual(basket), $"a custom collection read through its Add {Hash(written)}");
    }

    // --- a type no context offered ------------------------------------------------------------------------

    private static void Unavailable()
    {
        var context = new PartialContext();
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

        try
        {
            serializer.Serialize(new List<Unoffered> { new() }, context.Items);
            Check(false, "a type no context offered is refused");
        }
        catch (BinaryConfigurationException failure)
        {
            Check(failure.Message.Contains(nameof(Unoffered), StringComparison.Ordinal), "a type no context offered is refused");
        }
    }

    // --- a type offered without a contract ----------------------------------------------------------------

    private static void Uncontracted()
    {
        var context = new UncontractedContext();
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

        try
        {
            serializer.Serialize(new Bare(), context.Value);
            Check(false, "a type offered without a contract is refused");
        }
        catch (BinaryConfigurationException failure)
        {
            Check(failure.Message.Contains("has no TypeContract", StringComparison.Ordinal) &&
                  failure.Message.Contains(nameof(Bare), StringComparison.Ordinal),
                "a type offered without a contract is refused, naming the missing contract");
        }
    }

    // --- helpers ------------------------------------------------------------------------------------------

    private static BinarySerializer Serializer(
        BinarySerializerContext contracts,
        Func<BinarySerializerOptionsBuilder, BinarySerializerOptionsBuilder> configure) =>
        new(configure(BinarySerializerOptions.Configure().WithContracts(contracts)).Build());

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16];

    private static void Check(bool passed, string what)
    {
        Console.WriteLine($"{(passed ? "ok" : "FAILED")} {what}");
        if (!passed)
            Failures.Add(what);
    }

    /// <summary>A member-encoded type no context makes supported.</summary>
    private sealed class Unoffered
    {
        public int Value { get; set; }
    }

    /// <summary>A context that makes a list supported but not its element type.</summary>
    private sealed class PartialContext : BinarySerializerContext
    {
        public PartialContext()
        {
            Add(SupportedTypes.List<Unoffered>());
            Items = TypeInfo<List<Unoffered>>();
        }

        public BinaryTypeInfo<List<Unoffered>> Items { get; }
    }

    /// <summary>A member-encoded type a context makes supported without a contract.</summary>
    private sealed class Bare
    {
        public int Value { get; set; }
    }

    /// <summary>A context that makes a member-encoded type supported but adds no contract for it.</summary>
    private sealed class UncontractedContext : BinarySerializerContext
    {
        public UncontractedContext()
        {
            Add(SupportedTypes.Object<Bare>());
            Value = TypeInfo<Bare>();
        }

        public BinaryTypeInfo<Bare> Value { get; }
    }
}

/// <summary>A collection the table names no definition for, filled through its public <c>Add</c>.</summary>
internal sealed class Basket : ICollection<int>
{
    private readonly List<int> _items = [];

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(int item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(int item) => _items.Contains(item);
    public void CopyTo(int[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public bool Remove(int item) => _items.Remove(item);
    public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

[BinaryContext(typeof(Basket))]
internal partial class BasketContext : BinarySerializerContext;
