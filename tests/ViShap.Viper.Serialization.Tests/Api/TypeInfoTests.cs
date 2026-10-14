using System.Buffers;
using System.IO.Pipelines;
using ViShap.Viper.Contracts;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins NAOT-08…NAOT-11: every entry point that takes a context's type information writes the bytes and
/// reads the values of its counterpart without it; it refuses type information of another context than
/// the options', or of none, before touching the destination; a context hands out type information only
/// for a type it describes; and the dumper's forms with type information report what the others report.
/// </summary>
public class TypeInfoTests
{
    private static readonly BinarySerializerOptions Options =
        BinarySerializerOptions.Configure().WithContracts(GeneratedConformanceContracts.Default).Build();

    private static readonly BinarySerializer Serializer = new(Options);

    private static readonly BinaryTypeInfo<ConformanceKeyed> Keyed = GeneratedConformanceContracts.Default.ConformanceKeyed;

    private static ConformanceKeyed Value()
    {
        var value = new ConformanceKeyed { Id = 7, Name = "Ada", Big = long.MaxValue, Defaulted = 9 };
        value.SetCode(-3);
        return value;
    }

    private static void AssertSame(ConformanceKeyed? expected, ConformanceKeyed? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(
            (expected.Id, expected.Name, expected.Big, expected.Defaulted, expected.GetCode()),
            (actual.Id, actual.Name, actual.Big, actual.Defaulted, actual.GetCode()));
    }

    // --- NAOT-08: the same bytes and the same values ---------------------------------------------------

    [Fact]
    public async Task Serialize_WithTypeInfo_WritesTheBytesOfEveryCounterpart()
    {
        byte[] expected = Serializer.Serialize(Value());

        Assert.Equal(expected, Serializer.Serialize(Value(), Keyed));

        var buffer = new ArrayBufferWriter<byte>();
        Serializer.Serialize(buffer, Value(), Keyed);
        Assert.Equal(expected, buffer.WrittenSpan.ToArray());

        using (var pooled = Serializer.SerializePooled(Value(), Keyed))
            Assert.Equal(expected, pooled.Span.ToArray());

        var stream = new MemoryStream();
        Serializer.Serialize(stream, Value(), Keyed);
        Assert.Equal(expected, stream.ToArray());

        var asyncStream = new MemoryStream();
        await Serializer.SerializeAsync(asyncStream, Value(), Keyed);
        Assert.Equal(expected, asyncStream.ToArray());

        var pipe = new Pipe();
        await Serializer.SerializeAsync(pipe.Writer, Value(), Keyed);
        await pipe.Writer.CompleteAsync();
        var read = await pipe.Reader.ReadAsync();
        Assert.Equal(expected, read.Buffer.ToArray());
        pipe.Reader.AdvanceTo(read.Buffer.End);
    }

    [Fact]
    public async Task Deserialize_WithTypeInfo_ReadsTheValueOfEveryCounterpart()
    {
        byte[] frame = Serializer.Serialize(Value());
        byte[] trailed = [.. frame, 0xEE];

        AssertSame(Value(), Serializer.Deserialize(frame, Keyed));

        AssertSame(Value(), Serializer.Deserialize(trailed, Keyed, out int bytesConsumed));
        Assert.Equal(frame.Length, bytesConsumed);

        AssertSame(Value(), Serializer.Deserialize(new ReadOnlySequence<byte>(frame), Keyed));

        var sequence = new ReadOnlySequence<byte>(trailed);
        AssertSame(Value(), Serializer.Deserialize(sequence, Keyed, out SequencePosition consumed));
        Assert.Equal(frame.Length, sequence.Slice(0, consumed).Length);

        AssertSame(Value(), Serializer.Deserialize(new MemoryStream(frame), Keyed));
        AssertSame(Value(), await Serializer.DeserializeAsync(new MemoryStream(frame), Keyed));
        AssertSame(Value(), await Serializer.DeserializeAsync(PipeReader.Create(new ReadOnlySequence<byte>(frame)), Keyed));

        var values = new List<ConformanceKeyed?>();
        await foreach (var value in Serializer.DeserializeAsyncEnumerable(new MemoryStream([.. frame, .. frame]), Keyed))
            values.Add(value);
        Assert.Equal(2, values.Count);
        AssertSame(Value(), values[1]);

        values.Clear();
        await foreach (var value in Serializer.DeserializeAsyncEnumerable(PipeReader.Create(new ReadOnlySequence<byte>([.. frame, .. frame])), Keyed))
            values.Add(value);
        Assert.Equal(2, values.Count);
        AssertSame(Value(), values[0]);
    }

    [Fact]
    public async Task Populate_WithTypeInfo_FillsTheTargetAsEveryCounterpartDoes()
    {
        byte[] frame = Serializer.Serialize(Value());
        byte[] trailed = [.. frame, 0xEE];

        var target = new ConformanceKeyed();
        Serializer.Populate(frame, target, Keyed);
        AssertSame(Value(), target);

        target = new ConformanceKeyed();
        Serializer.Populate(trailed, target, Keyed, out int bytesConsumed);
        AssertSame(Value(), target);
        Assert.Equal(frame.Length, bytesConsumed);

        target = new ConformanceKeyed();
        Serializer.Populate(new ReadOnlySequence<byte>(frame), target, Keyed);
        AssertSame(Value(), target);

        target = new ConformanceKeyed();
        var sequence = new ReadOnlySequence<byte>(trailed);
        Serializer.Populate(sequence, target, Keyed, out SequencePosition consumed);
        AssertSame(Value(), target);
        Assert.Equal(frame.Length, sequence.Slice(0, consumed).Length);

        target = new ConformanceKeyed();
        Serializer.Populate(new MemoryStream(frame), target, Keyed);
        AssertSame(Value(), target);

        target = new ConformanceKeyed();
        await Serializer.PopulateAsync(new MemoryStream(frame), target, Keyed);
        AssertSame(Value(), target);

        target = new ConformanceKeyed();
        await Serializer.PopulateAsync(PipeReader.Create(new ReadOnlySequence<byte>(frame)), target, Keyed);
        AssertSame(Value(), target);
    }

    // --- NAOT-09: type information of another context, or of none, is refused -----------------------

    [Fact]
    public void Serialize_WithTypeInfoOfAnotherContext_ThrowsConfigurationAndWritesNothing()
    {
        var other = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(HandWrittenContracts.Default).Build());
        var none = new BinarySerializer();

        var buffer = new ArrayBufferWriter<byte>();
        AssertEx.Throws<BinaryConfigurationException>("belongs to", () => other.Serialize(buffer, Value(), Keyed));
        AssertEx.Throws<BinaryConfigurationException>("no context", () => none.Serialize(buffer, Value(), Keyed));
        Assert.Equal(0, buffer.WrittenCount);

        var stream = new MemoryStream();
        AssertEx.Throws<BinaryConfigurationException>("belongs to", () => other.Serialize(stream, Value(), Keyed));
        Assert.Equal(0, stream.Length);

        AssertEx.Throws<BinaryConfigurationException>("belongs to", () => other.Deserialize(Serializer.Serialize(Value()), Keyed));
    }

    [Fact]
    public void EveryEntryPoint_WithNullTypeInfo_ThrowsArgumentNull()
    {
        byte[] frame = Serializer.Serialize(Value());

        Assert.Throws<ArgumentNullException>(() => Serializer.Serialize(Value(), null!));
        Assert.Throws<ArgumentNullException>(() => Serializer.Deserialize<ConformanceKeyed>(frame, (BinaryTypeInfo<ConformanceKeyed>)null!));
        Assert.Throws<ArgumentNullException>(() => Serializer.Populate(frame, new ConformanceKeyed(), null!));
        Assert.Throws<ArgumentNullException>(() => BinaryFormatDumper.Dump<ConformanceKeyed>(frame, null!, Options));
        Assert.Throws<ArgumentNullException>(() => BinaryFormatDumper.Dump(frame, Keyed, null!));
    }

    // --- NAOT-10: a context hands out type information only for what it describes ---------------------

    [Fact]
    public void TryGetTypeInfo_OfASupportedTypeOrAScalar_IsBoundToTheContext()
    {
        var context = GeneratedConformanceContracts.Default;

        Assert.Same(context, context.TryGetTypeInfo<ConformanceKeyed>()!.Context);
        Assert.Same(context, context.TryGetTypeInfo<int>()!.Context);
        Assert.Same(context, context.TryGetTypeInfo<string>()!.Context);
        Assert.Equal(typeof(ConformanceKeyed), context.TryGetTypeInfo<ConformanceKeyed>()!.Type);
        Assert.Null(context.TryGetTypeInfo<List<Guid>>());
        Assert.Null(context.TryGetTypeInfo<FrozenKeyed>());
    }

    [Fact]
    public void TypeInfo_OfATypeTheContextDoesNotDescribe_ThrowsConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>("does not make", () => new UndescribedContext());
    }

    [Fact]
    public void TypeInfo_OfASupportedTypeOfAHandWrittenContext_SerializesThroughIt()
    {
        var context = new ListContext();
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

        byte[] bytes = serializer.Serialize(new List<int> { 1, 2, 3 }, context.Numbers);

        Assert.Equal(new BinarySerializer().Serialize(new List<int> { 1, 2, 3 }), bytes);
        Assert.Equal([1, 2, 3], serializer.Deserialize(bytes, context.Numbers)!);
    }

    // --- NAOT-11: the dumper ----------------------------------------------------------------------------

    [Fact]
    public void Dump_WithTypeInfo_ReportsWhatTheCounterpartReports()
    {
        byte[] frame = Serializer.Serialize(Value());

        Assert.Equal(BinaryFormatDumper.Dump<ConformanceKeyed>(frame, Options).ToString(), BinaryFormatDumper.Dump(frame, Keyed, Options).ToString());
        Assert.Equal(
            BinaryFormatDumper.Dump<ConformanceKeyed>(new ReadOnlySequence<byte>(frame), Options).ToString(),
            BinaryFormatDumper.Dump(new ReadOnlySequence<byte>(frame), Keyed, Options).ToString());
        Assert.Equal(BinaryFormatDumper.DumpValue(Value(), Options).ToString(), BinaryFormatDumper.DumpValue(Value(), Keyed, Options).ToString());

        var changed = Value();
        changed.Id = 8;
        byte[] other = Serializer.Serialize(changed);
        Assert.Null(BinaryFormatDumper.Compare(frame, frame, Keyed, Options));
        Assert.Equal(BinaryFormatDumper.Compare<ConformanceKeyed>(frame, other, Options)!.Path, BinaryFormatDumper.Compare(frame, other, Keyed, Options)!.Path);
    }

    [Fact]
    public void Dump_WithTypeInfoOfAnotherContext_ThrowsConfiguration()
    {
        byte[] frame = Serializer.Serialize(Value());

        AssertEx.Throws<BinaryConfigurationException>("belongs to", () => BinaryFormatDumper.Dump(frame, Keyed, BinarySerializerOptions.Default));
    }

    private sealed class UndescribedContext : BinarySerializerContext
    {
        public UndescribedContext() => _ = TypeInfo<List<Guid>>();
    }

    private sealed class ListContext : BinarySerializerContext
    {
        public ListContext()
        {
            Add(SupportedTypes.List<int>());
            Numbers = TypeInfo<List<int>>();
        }

        public BinaryTypeInfo<List<int>> Numbers { get; }
    }
}
