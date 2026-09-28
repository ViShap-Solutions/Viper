using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ViShap.Viper.Io;

/// <summary>
/// A byte budget the bytes of an operation were cut to. Running out of them is a limit violation when
/// the read would have gone past the budget, and a malformed payload otherwise.
/// </summary>
/// <param name="Resource">The budget named in a failure, e.g. "payload" or "wire".</param>
/// <param name="Maximum">The budget in bytes, counted from the operation's first byte.</param>
internal readonly record struct WireBudget(string Resource, long Maximum)
{
    /// <summary>
    /// Classifies a declaration the available bytes cannot satisfy. Exceeding the budget is a limit
    /// violation; fitting the budget but not the bytes means the payload is shorter than it claims,
    /// which is a malformed payload.
    /// </summary>
    /// <param name="requested">The bytes the declaration needs.</param>
    /// <param name="consumed">The bytes of the budget already used.</param>
    /// <param name="remaining">The bytes that can still arrive.</param>
    /// <param name="what">The declaration, used in diagnostics.</param>
    public BinaryFormatException Exceeded(long requested, long consumed, long remaining, string what) =>
        requested > Maximum - consumed
            ? new BinaryLimitException(
                $"{what} needs {requested} byte(s), which would exceed the {Resource} byte budget of " +
                $"{Maximum}.")
            : new BinaryFormatException(
                $"{what} declares {requested} byte(s) but only {remaining} remain in the {Resource} " +
                "stream.");
}

/// <summary>
/// The only way to read bytes of a payload. It reads from memory — one span, or a
/// <see cref="ReadOnlySequence{T}"/> of segments — so it always knows exactly how many bytes remain.
/// Every primitive is checked: running out produces <see cref="BinaryFormatException"/> (or
/// <see cref="BinaryLimitException"/> where the bytes were cut to a budget) instead of a framework
/// exception, a declared length is compared against both its configured maximum and
/// <see cref="Remaining"/> before any allocation happens, and a count can only be obtained as a
/// validated <see cref="ElementCount"/>. Formatters hold one of these and nothing else, so there is no
/// unchecked path to the wire.
/// <para>
/// The reader is a <see langword="ref struct"/> passed by reference: a copy would read the same bytes
/// again.
/// </para>
/// </summary>
internal ref struct WireReader
{
    private readonly ref OperationState _state;
    private readonly ReadOnlySequence<byte> _sequence;
    private readonly long _length;
    private readonly WireBudget? _budget;
    private readonly string? _scope;
    private readonly int _scopeKey;
    private ReadOnlySpan<byte> _span;
    private int _index;
    private long _spanStart;
    private SequencePosition _nextSegment;

    /// <summary>A reader over <paramref name="bytes"/>; running out of them is a malformed payload.</summary>
    public WireReader(ReadOnlySpan<byte> bytes, ref OperationState state)
        : this(bytes, ref state, budget: null, scope: "the payload")
    {
    }

    /// <summary>
    /// A reader over bytes cut to <paramref name="budget"/>: a read past them is a limit violation when
    /// it would exceed the budget, and a malformed payload otherwise.
    /// </summary>
    public WireReader(ReadOnlySpan<byte> bytes, ref OperationState state, WireBudget budget)
        : this(bytes, ref state, (WireBudget?)budget, scope: "the payload")
    {
    }

    /// <summary>A reader over <paramref name="bytes"/>, which may span several segments.</summary>
    public WireReader(ReadOnlySequence<byte> bytes, ref OperationState state)
        : this(bytes, ref state, budget: null, scope: "the payload")
    {
    }

    /// <summary>
    /// A reader over segmented bytes cut to <paramref name="budget"/>: a read past them is a limit
    /// violation when it would exceed the budget, and a malformed payload otherwise.
    /// </summary>
    public WireReader(ReadOnlySequence<byte> bytes, ref OperationState state, WireBudget budget)
        : this(bytes, ref state, (WireBudget?)budget, scope: "the payload")
    {
    }

    private WireReader(
        ReadOnlySpan<byte> bytes,
        ref OperationState state,
        WireBudget? budget,
        string? scope,
        int scopeKey = -1)
    {
        _state = ref state;
        _sequence = default;
        _length = bytes.Length;
        _budget = budget;
        _scope = scope;
        _scopeKey = scopeKey;
        _span = bytes;
        _index = 0;
        _spanStart = 0;
        _nextSegment = default;
    }

    private WireReader(
        ReadOnlySequence<byte> bytes,
        ref OperationState state,
        WireBudget? budget,
        string? scope,
        int scopeKey = -1)
    {
        _state = ref state;
        _sequence = bytes;
        _length = bytes.Length;
        _budget = budget;
        _scope = scope;
        _scopeKey = scopeKey;
        _nextSegment = bytes.Start;
        _span = bytes.TryGet(ref _nextSegment, out var first) ? first.Span : default;
        _index = 0;
        _spanStart = 0;
    }

    /// <summary>The state of the operation these bytes belong to.</summary>
    internal readonly ref OperationState State => ref _state;

    /// <summary>Bytes read so far.</summary>
    public readonly long Consumed => _spanStart + _index;

    /// <summary>Bytes still available to this reader — exact, because they are in memory.</summary>
    public readonly long Remaining => _length - Consumed;

    /// <summary>
    /// Reads a boolean. The wire admits exactly two encodings, so any other byte is a malformed
    /// payload rather than a second spelling of <see langword="true"/> — which is what keeps a flag
    /// unforgeable under authenticated encryption, where the tag covers the decoded fields.
    /// </summary>
    public bool ReadBoolean() => ReadOneByte("Boolean") switch
    {
        0 => false,
        1 => true,
        var other => throw new BinaryFormatException(
            $"Boolean value {other} is not a valid encoding; only 0 and 1 are admitted.")
    };

    public byte ReadByte() => ReadOneByte("Byte");

    public sbyte ReadSByte() => (sbyte)ReadOneByte("SByte");

    public short ReadInt16() => ReadInteger<short>("Int16");

    public ushort ReadUInt16() => ReadInteger<ushort>("UInt16");

    public char ReadChar() => (char)ReadUInt16();

    public int ReadInt32() => ReadInteger<int>("Int32");

    public uint ReadUInt32() => ReadInteger<uint>("UInt32");

    public long ReadInt64() => ReadInteger<long>("Int64");

    public ulong ReadUInt64() => ReadInteger<ulong>("UInt64");

    public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInteger<int>("Single"));

    public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInteger<long>("Double"));

    public decimal ReadDecimal()
    {
        RequireAvailable(16, "Decimal");

        Span<int> bits =
        [
            ReadInteger<int>("Decimal"),
            ReadInteger<int>("Decimal"),
            ReadInteger<int>("Decimal"),
            ReadInteger<int>("Decimal")
        ];

        try
        {
            return new decimal(bits);
        }
        catch (ArgumentException ex)
        {
            throw new BinaryFormatException("Decimal value is malformed.", ex);
        }
    }

    /// <summary>Fills <paramref name="destination"/> completely or fails.</summary>
    public void ReadExact(scoped Span<byte> destination, string what)
    {
        RequireAvailable(destination.Length, what);

        while (!destination.IsEmpty)
        {
            EnsureSpan();
            int take = Math.Min(destination.Length, _span.Length - _index);
            _span.Slice(_index, take).CopyTo(destination);
            _index += take;
            destination = destination[take..];
        }
    }

    /// <summary>Reads a length-prefixed byte blob bounded by <c>MaxByteBlobBytes</c>.</summary>
    public byte[] ReadBlob(string what)
    {
        int length = ReadBoundedLength(_state.Limits.MaxByteBlobBytes, "MaxByteBlobBytes", $"{what} byte length");
        return ReadBytes(length, what);
    }

    /// <summary>Reads a UTF-8 string bounded by <c>MaxStringBytes</c>.</summary>
    /// <exception cref="BinaryLimitException">The declared length exceeds the configured maximum.</exception>
    public string ReadString()
    {
        int length = ReadBoundedLength(
            _state.Limits.MaxStringBytes, "MaxStringBytes", "String byte length");
        return DecodeString(length, "String");
    }

    /// <summary>
    /// Reads a UTF-8 string whose declared byte length must fit a ceiling the format itself fixes,
    /// such as a header field. Exceeding it describes malformed input rather than a policy breach, so
    /// it is reported as a format error and not as a limit violation.
    /// </summary>
    /// <param name="maxBytes">The largest encoded length the format admits here.</param>
    /// <param name="what">The field being read, used in diagnostics.</param>
    /// <exception cref="BinaryFormatException">The declared length exceeds <paramref name="maxBytes"/>.</exception>
    public string ReadString(int maxBytes, string what)
    {
        int length = Read7BitEncodedInt($"{what} byte length");
        if (length > maxBytes)
            throw new BinaryFormatException(
                $"{what} declares {length} byte(s), but this field admits at most {maxBytes}.");

        RequireAvailable(length, what);
        return DecodeString(length, what);
    }

    /// <summary>
    /// Reads an element count, validating it against its limit and charging the element budget.
    /// </summary>
    public ElementCount ReadCount(CountKind kind, string what) =>
        ElementCount.Validate(ReadInt32(), kind, ref _state, what);

    /// <summary>Reads a bit count bounded by <c>MaxByteBlobBytes</c> × 8.</summary>
    public int ReadBitCount(string what)
    {
        int bits = ReadInt32();
        if (bits < 0)
            throw new BinaryFormatException($"{what} {bits} must be non-negative.");

        if (bits > (long)_state.Limits.MaxByteBlobBytes * 8L)
            throw new BinaryLimitException(
                $"{what} {bits} exceeds the configured maximum of " +
                $"{(long)_state.Limits.MaxByteBlobBytes * 8L} (MaxByteBlobBytes, in bits).");

        return bits;
    }

    /// <summary>
    /// Reads a non-negative 7-bit encoded integer, rejecting malformed encodings. The encoding must
    /// be minimal: a value spelled with trailing zero groups, such as <c>85 00</c> for 5, is a second
    /// byte sequence for the same number, so it is malformed rather than accepted.
    /// </summary>
    public int Read7BitEncodedInt(string what)
    {
        uint result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte current = ReadOneByte(what, " 7-bit integer");

            if (shift == 28 && (current & 0xF0) != 0)
                throw new BinaryFormatException($"Malformed {what} 7-bit integer.");

            result |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                if (shift > 0 && current == 0)
                    throw new BinaryFormatException(
                        $"{what} 7-bit integer is not minimally encoded.");

                if (result > int.MaxValue)
                    throw new BinaryFormatException(
                        $"{what} value {result} is outside the supported non-negative Int32 range.");

                return (int)result;
            }
        }

        throw new BinaryFormatException($"Malformed {what} 7-bit integer.");
    }

    /// <summary>
    /// Rejects a declared length that cannot possibly be satisfied, before it is used to allocate.
    /// </summary>
    public readonly void RequireAvailable(long length, string what)
    {
        if (length < 0)
            throw new BinaryFormatException($"{what} length {length} must be non-negative.");

        if (length > Remaining)
            throw Exceeded(length, what);
    }

    /// <summary>
    /// Consumes <paramref name="length"/> bytes without reading them, so skipping an unknown keyed
    /// field never materializes it.
    /// </summary>
    public void Skip(long length, string what)
    {
        RequireAvailable(length, what);
        Advance(length);
    }

    /// <summary>
    /// Takes the next <paramref name="length"/> bytes as a reader of their own and moves past them.
    /// The inner reader shares the operation, so budgets stay cumulative, but it cannot read past the
    /// declared field: running out inside it is a malformed payload.
    /// </summary>
    public WireReader Slice(int length, string what)
    {
        RequireAvailable(length, what);
        return TakeSlice(length, what, scopeKey: -1);
    }

    /// <summary>
    /// Takes the next <paramref name="length"/> bytes as the window over the keyed field
    /// <paramref name="key"/>, as <see cref="Slice"/> does. The field is named only when a read
    /// fails, so taking a window allocates nothing.
    /// </summary>
    public WireReader SliceField(int key, int length)
    {
        if (length < 0 || length > Remaining)
            RequireAvailable(length, FieldScope(key));

        return TakeSlice(length, scope: null, key);
    }

    private WireReader TakeSlice(int length, string? scope, int scopeKey)
    {
        WireReader slice = _span.Length - _index >= length
            ? new WireReader(_span.Slice(_index, length), ref _state, budget: null, scope, scopeKey)
            : new WireReader(_sequence.Slice(Consumed, length), ref _state, budget: null, scope, scopeKey);

        Advance(length);
        return slice;
    }

    /// <summary>How a keyed field is named in a diagnostic: <c>Key 3 payload</c>.</summary>
    internal static string FieldScope(int key) => $"Key {key} payload";

    internal byte[] ReadBytes(int length, string what)
    {
        if (length == 0)
            return [];

        RequireAvailable(length, what);
        byte[] result = new byte[length];
        ReadExact(result, what);
        return result;
    }

    /// <summary>
    /// Decodes strictly: a byte sequence that is not valid UTF-8 raises instead of becoming a U+FFFD
    /// replacement character. Substituting would map many byte sequences onto one string, and the
    /// authentication tag is computed over the decoded field, so each of them would carry the same
    /// tag.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private string DecodeString(int length, string what)
    {
        if (length == 0)
            return string.Empty;

        RequireAvailable(length, what);
        EnsureSpan();

        ReadOnlySpan<byte> bytes;
        if (_span.Length - _index >= length)
        {
            bytes = _span.Slice(_index, length);
            _index += length;
        }
        else
        {
            bytes = ReadBytes(length, what);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (ArgumentException ex)
        {
            throw new BinaryFormatException($"{what} payload is not valid UTF-8.", ex);
        }
    }

    private int ReadBoundedLength(long maximum, string limit, string what)
    {
        int length = Read7BitEncodedInt(what);
        if (length > maximum)
            throw new BinaryLimitException(
                $"{what} {length} exceeds the configured maximum of {maximum} ({limit}).");

        RequireAvailable(length, what);
        return length;
    }

    private byte ReadOneByte(string what) => ReadOneByte(what, suffix: string.Empty);

    /// <summary>
    /// Reads one byte. The field is named as <paramref name="what"/> followed by
    /// <paramref name="suffix"/>, joined only when the read fails, so a successful read allocates
    /// nothing.
    /// </summary>
    private byte ReadOneByte(string what, string suffix)
    {
        if (_index < _span.Length)
            return _span[_index++];

        if (Remaining == 0)
            throw _budget is { } budget && Consumed >= budget.Maximum
                ? new BinaryLimitException(
                    $"The {budget.Resource} byte budget of {budget.Maximum} would be exceeded by " +
                    "reading 1 more bytes.")
                : new BinaryFormatException($"{what}{suffix} ended early.");

        EnsureSpan();
        return _span[_index++];
    }

    /// <summary>
    /// Reads a little-endian integer straight from the current segment, or assembles it when it
    /// straddles two segments or runs past the end.
    /// </summary>
    private T ReadInteger<T>(string what) where T : unmanaged, IBinaryInteger<T>, IMinMaxValue<T>
    {
        int size = Unsafe.SizeOf<T>();
        if (_span.Length - _index >= size)
        {
            T value = Decode<T>(_span.Slice(_index, size));
            _index += size;
            return value;
        }

        Span<byte> bytes = stackalloc byte[size];
        ReadExact(bytes, what);
        return Decode<T>(bytes);
    }

    private static T Decode<T>(ReadOnlySpan<byte> bytes) where T : unmanaged, IBinaryInteger<T>, IMinMaxValue<T> =>
        BitConverter.IsLittleEndian
            ? MemoryMarshal.Read<T>(bytes)
            : T.ReadLittleEndian(bytes, isUnsigned: T.IsZero(T.MinValue));

    private void Advance(long count)
    {
        while (count > 0)
        {
            EnsureSpan();
            int take = (int)Math.Min(count, _span.Length - _index);
            _index += take;
            count -= take;
        }
    }

    /// <summary>Moves to the next non-empty segment once the current one is used up.</summary>
    private void EnsureSpan()
    {
        while (_index == _span.Length && Consumed < _length)
        {
            _spanStart += _span.Length;
            _span = _sequence.TryGet(ref _nextSegment, out var segment) ? segment.Span : default;
            _index = 0;
        }
    }

    /// <summary>
    /// Classifies a declaration these bytes cannot satisfy: against the budget they were cut to, when
    /// there is one, and otherwise as a payload shorter than it claims.
    /// </summary>
    private readonly BinaryFormatException Exceeded(long requested, string what) =>
        _budget is { } budget
            ? budget.Exceeded(requested, Consumed, Remaining, what)
            : new BinaryFormatException(
                $"{what} declares {requested} byte(s) but only {Remaining} remain in {_scope ?? FieldScope(_scopeKey)}.");
}
