using System.Buffers;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile.Fuzz;

/// <summary>
/// A broken promise of the read path: an exception outside the taxonomy, an accepted frame with a second
/// encoding, or a read that allocated beyond the bound the bytes it was given allow.
/// </summary>
internal sealed class FuzzFinding(string message, Exception? cause = null) : Exception(message, cause);

/// <summary>
/// What a frame is read as, under which options, and through which entry points. Every entry point is
/// held to the same rules: only a <see cref="BinarySerializerException"/> leaves it; a frame the span
/// reader accepts re-serializes to the same bytes when the model is canonical; and a read allocates at
/// most <see cref="AllocationPerInputByte"/> bytes for each byte it was given, plus
/// <see cref="AllocationAllowance"/>.
/// </summary>
internal abstract class FuzzTarget(
    string name,
    BinarySerializerOptions options,
    BinarySerializerOptions unreferenced,
    BinarySerializerOptions referencing)
{
    /// <summary>Bytes of managed memory a read may take for each byte of its input.</summary>
    public const long AllocationPerInputByte = 64;

    /// <summary>Bytes of managed memory a read may take beyond its share of the input.</summary>
    public const long AllocationAllowance = 64 * 1024;

    public string Name { get; } = name;

    public BinarySerializerOptions Options { get; } = options;

    protected BinarySerializer Serializer { get; } = new(options);

    /// <summary>The same options without reference framing, which a frame whose header does not ask for it is written back with.</summary>
    protected BinarySerializer Unreferenced { get; } = new(unreferenced);

    /// <summary>The same options with reference framing, which a frame whose header asks for it is written back with.</summary>
    protected BinarySerializer Referencing { get; } = new(referencing);

    /// <summary>Whether the frame is the payload's only spelling: no compression, no cipher, every keyed field known.</summary>
    public bool Canonical { get; init; }

    /// <summary>
    /// Whether the model holds a value whose wire form is normalized on read — a container that orders or
    /// hashes its elements itself, or a local <see cref="DateTime"/>, which travels as its instant: an
    /// accepted frame then need not be what is written back, but what is written back is written back
    /// unchanged.
    /// </summary>
    public bool Normalizes { get; init; }

    /// <summary>Whether the frame is read without decompression, so its memory follows its bytes.</summary>
    public bool Bounded { get; init; }

    /// <summary>Whether the frame is a bare V0 payload, which no asynchronous or forward-only reader accepts.</summary>
    public bool Headerless { get; init; }

    /// <summary>The valid frames the mutations start from.</summary>
    public abstract IReadOnlyList<byte[]> Seeds { get; }

    /// <summary>Reads <paramref name="input"/> through every entry point and checks every rule.</summary>
    public void Run(byte[] input)
    {
        Entry("span", () => ReadSpanChecked(input));
        Entry("sequence", () => ReadSequence(input));
        Entry("stream", () => ReadStream(new MemoryStream(input, writable: false)));
        Entry("dump", () => BinaryFormatDumper.Dump(input, Options).ToString());
        Entry("dump typed", () => DumpTyped(input));
        Entry("populate", () => Populate(input));

        if (Headerless)
            return;

        Entry("dump header", () => BinaryFormatDumper.DumpHeader(input));
        Entry("forward-only stream", () => ReadStream(new NonSeekableStream(input)));
        Entry("partial reads", () => ReadStream(new PartialReadStream(input, 3)));
        Entry("async stream", () => ReadAsync(new PartialReadStream(input, 5)));
        Entry("async pipe", () => ReadPipe(new ChunkedPipeReader(input, 7)));
        Entry("frames", () => ReadFrames(new MemoryStream(input, writable: false)));
    }

    protected abstract object? ReadSpan(ReadOnlySpan<byte> input);

    protected abstract object? ReadSequence(byte[] input);

    protected abstract object? ReadStream(Stream input);

    protected abstract object? ReadAsync(Stream input);

    protected abstract object? ReadPipe(System.IO.Pipelines.PipeReader input);

    protected abstract object? ReadFrames(Stream input);

    protected abstract object? DumpTyped(byte[] input);

    protected abstract object? Populate(byte[] input);

    protected abstract byte[] Write(object? value, bool references);

    /// <summary>Where the payload mode lies in a V1 header: after the magic and the one-byte version.</summary>
    private const int ModeOffset = 5;

    /// <summary>
    /// Whether a V1 header carries a service record other than a checksum: a compression or an encryption,
    /// whose bytes are the algorithm's and not the payload's one encoding, or a record a reader skips by
    /// its length and a writer never writes.
    /// </summary>
    private bool SkipsARecord(byte[] input)
    {
        if (Headerless || input.Length <= ModeOffset + 1)
            return false;

        int at = ModeOffset + 1;
        if (!TryVarint(input, ref at, out int records))
            return false;

        for (int record = 0; record < records; record++)
        {
            if (!TryVarint(input, ref at, out int tag) || !TryVarint(input, ref at, out int length))
                return false;

            if ((tag >> 1) != 1)
                return true;

            at += length;
        }

        return false;
    }

    private static bool TryVarint(byte[] input, ref int at, out int value)
    {
        value = 0;
        for (int shift = 0; shift < 35 && at < input.Length; shift += 7)
        {
            byte next = input[at++];
            value |= (next & 0x7F) << shift;
            if (next < 0x80)
                return true;
        }

        return false;
    }

    private object? ReadSpanChecked(byte[] input)
    {
        long bound = AllocationPerInputByte * input.Length + AllocationAllowance;
        var (value, refused, allocated) = Measured(input);

        // A first read of a path also builds what the process keeps for it — a codec, a message, a
        // compiled method — so a read above the bound is measured a second time before it counts.
        if (Bounded && allocated > bound && Measured(input).Allocated is var again && again > bound)
            throw new FuzzFinding($"Reading {input.Length} byte(s) allocated {again} byte(s), above the bound of {bound}.");

        if (refused is not null)
            throw refused;

        if (Canonical && !SkipsAField(input) && !SkipsARecord(input))
        {
            // A reader follows the payload mode of the header, so the frame is written back in that mode.
            // Reference ids are explicit and only need to be unique, and a container that orders or
            // hashes its elements, like a local time, keeps no form of the wire: then what is written back must only be
            // written back unchanged.
            bool references = !Headerless && input.Length > ModeOffset && (input[ModeOffset] & 1) == 1;
            byte[] rewritten = Write(value, references);
            byte[] expected = Normalizes || references ? Write(ReadSpan(rewritten), references) : input;
            if (!rewritten.AsSpan().SequenceEqual(expected))
                throw new FuzzFinding(
                    $"An accepted frame has a second encoding: read {Convert.ToHexString(expected)}, wrote {Convert.ToHexString(rewritten)}.");
        }

        return value;
    }

    /// <summary>Reads <paramref name="input"/> and the bytes the read allocated, whether it was accepted or refused.</summary>
    private (object? Value, BinarySerializerException? Refused, long Allocated) Measured(byte[] input)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            object? value = ReadSpan(input);
            return (value, null, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        catch (BinarySerializerException refused)
        {
            return (null, refused, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }

    /// <summary>Whether reading <paramref name="input"/> skipped a keyed field its model does not know.</summary>
    private bool SkipsAField(byte[] input) => Skips(DumpRoot(input));

    private static bool Skips(BinaryDumpNode? node) =>
        node is not null && (node.Kind == BinaryDumpNodeKind.UnknownKeyedField || node.Children.Any(Skips));

    protected abstract BinaryDumpNode? DumpRoot(byte[] input);

    private void Entry(string entry, Func<object?> read)
    {
        try
        {
            read();
        }
        catch (BinarySerializerException)
        {
        }
        catch (FuzzFinding finding)
        {
            throw new FuzzFinding($"{Name}, {entry}: {finding.Message}");
        }
        catch (Exception escaped)
        {
            throw new FuzzFinding($"{Name}, {entry}: {escaped.GetType()} escaped the taxonomy: {escaped.Message}", escaped);
        }
    }
}

/// <summary>The entry points of <see cref="FuzzTarget"/> for a model of type <typeparamref name="T"/>.</summary>
internal sealed class FuzzTarget<T>(
    string name,
    BinarySerializerOptions options,
    BinarySerializerOptions unreferenced,
    BinarySerializerOptions referencing,
    Func<T>? create,
    Func<BinarySerializer, IEnumerable<byte[]>> seeds)
    : FuzzTarget(name, options, unreferenced, referencing)
    where T : class
{
    private IReadOnlyList<byte[]>? _seeds;

    public override IReadOnlyList<byte[]> Seeds => _seeds ??= [.. seeds(Serializer)];

    protected override object? ReadSpan(ReadOnlySpan<byte> input) => Serializer.Deserialize<T>(input);

    protected override object? ReadSequence(byte[] input)
    {
        int first = input.Length / 3;
        int second = 2 * input.Length / 3;
        return Serializer.Deserialize<T>(Sequences.Of(input[..first], input[first..second], input[second..]));
    }

    protected override object? ReadStream(Stream input) => Serializer.Deserialize<T>(input);

    protected override object? ReadAsync(Stream input) =>
        Serializer.DeserializeAsync<T>(input).AsTask().GetAwaiter().GetResult();

    protected override object? ReadPipe(System.IO.Pipelines.PipeReader input) =>
        Serializer.DeserializeAsync<T>(input).AsTask().GetAwaiter().GetResult();

    protected override object? ReadFrames(Stream input) => DrainFrames(input).GetAwaiter().GetResult();

    protected override object? DumpTyped(byte[] input) => BinaryFormatDumper.Dump<T>(input, Options).ToString();

    protected override BinaryDumpNode? DumpRoot(byte[] input) => BinaryFormatDumper.Dump<T>(input, Options).Root;

    protected override object? Populate(byte[] input)
    {
        if (create is null)
            return null;

        var target = create();
        Serializer.Populate(input.AsSpan(), target);
        return target;
    }

    protected override byte[] Write(object? value, bool references) =>
        (references ? Referencing : Unreferenced).Serialize((T?)value);

    private async Task<int> DrainFrames(Stream input)
    {
        int frames = 0;
        await foreach (var _ in Serializer.DeserializeAsyncEnumerable<T>(input).ConfigureAwait(false))
            frames++;

        return frames;
    }
}
