namespace ViShap.Viper.Security;

/// <summary>
/// The resource policy of one serializer: how much a single serialize or deserialize call is allowed
/// to consume.
/// </summary>
/// <remarks>
/// <para>
/// Deserialization turns bytes you may not control into objects, so every quantity a payload can
/// declare — a length, a count, a nesting level — is capped here. A payload that exceeds a cap is
/// rejected with <see cref="Exceptions.BinaryLimitException"/> <em>before</em> the work it asked for
/// is done, so an oversized declaration never causes the allocation it describes.
/// </para>
/// <para>
/// The defaults suit general-purpose use and bound a single payload to roughly 64 MiB of logical
/// data. Tighten them when you accept untrusted input: the useful ones are usually
/// <see cref="MaxPayloadBytes"/>, <see cref="MaxWireBytes"/> and <see cref="MaxDepth"/>. Loosen them
/// only for data you produced yourself.
/// </para>
/// <para>
/// Limits are immutable and validated once, when options are built. Every value must be positive; a
/// zero or negative value is <see cref="Exceptions.BinaryConfigurationException"/>. A zero read
/// <em>from</em> a payload is legal wherever the shape allows it — an empty collection or string is
/// not an error.
/// </para>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithLimits(SerializationLimits.Default with
///     {
///         MaxPayloadBytes = 4 * 1024 * 1024,
///         MaxDepth = 32
///     })
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed record SerializationLimits
{
    /// <summary>The default policy. Use <c>with</c> to derive a stricter or looser one.</summary>
    public static SerializationLimits Default { get; } = new();

    /// <summary>
    /// Maximum structural nesting depth, counting objects and containers alike. Default 512.
    /// </summary>
    /// <remarks>
    /// This is the bound that keeps a hostile payload from exhausting the stack: nesting is refused
    /// at the limit rather than followed until the process dies. It applies to reading and writing,
    /// and to recursive containers such as a list of lists exactly as to recursive objects.
    /// </remarks>
    public int MaxDepth { get; init; } = 512;

    /// <summary>
    /// Maximum length of a single array. Default 1,000,000. For an array of rank greater than one it
    /// bounds every dimension and the product of them all, so a shape with no elements still cannot
    /// declare a dimension the runtime could not create.
    /// </summary>
    /// <remarks>
    /// It bounds every element type alike. A <c>byte[]</c> is an ordinary array and is bounded here,
    /// not by <see cref="MaxByteBlobBytes"/>, and each of its bytes is one element of
    /// <see cref="MaxTotalElements"/> — so carrying binary data larger than the default means raising
    /// both, since either one alone still refuses the value.
    /// </remarks>
    public int MaxArrayLength { get; init; } = 1_000_000;

    /// <summary>Maximum number of elements in a single collection. Default 1,000,000.</summary>
    public int MaxCollectionLength { get; init; } = 1_000_000;

    /// <summary>Maximum number of entries in a single dictionary. Default 1,000,000.</summary>
    public int MaxDictionaryEntries { get; init; } = 1_000_000;

    /// <summary>Maximum UTF-8 length of a single string, in bytes — not characters. Default 4,000,000.</summary>
    public int MaxStringBytes { get; init; } = 4_000_000;

    /// <summary>
    /// Maximum length of a single byte blob — a value written as one declared length followed by
    /// raw bytes, which is the encoding of a <c>BigInteger</c> body and of <c>BitArray</c> data.
    /// Default 16,000,000.
    /// </summary>
    /// <remarks>
    /// It bounds those encodings and nothing else. An array of bytes is not a blob: <c>byte[]</c>,
    /// like every other array, is bounded by <see cref="MaxArrayLength"/>.
    /// </remarks>
    public int MaxByteBlobBytes { get; init; } = 16_000_000;

    /// <summary>
    /// Maximum number of elements across the whole operation, summed over every collection, array and
    /// dictionary. Default 10,000,000.
    /// </summary>
    /// <remarks>
    /// The per-container limits bound one container; this one bounds a payload built from many small
    /// containers that are each individually legal. One element is one charge however many bytes it
    /// encodes to, so this budget measures structural size rather than byte volume — and a
    /// <c>byte[]</c> of a million bytes spends a million of it, while a record spends one whatever
    /// its members weigh. Byte volume is bounded by <see cref="MaxPayloadBytes"/> instead.
    /// </remarks>
    public long MaxTotalElements { get; init; } = 10_000_000;

    /// <summary>
    /// Maximum number of structural nodes — objects and containers — materialized by the whole
    /// operation. Default 1,000,000.
    /// </summary>
    /// <remarks>A repeated reference to an already materialized object does not count again.</remarks>
    public long MaxObjectGraphNodes { get; init; } = 1_000_000;

    /// <summary>Maximum number of fields in a single keyed contract object. Default 1,000,000.</summary>
    public int MaxKeyedFields { get; init; } = 1_000_000;

    /// <summary>
    /// Maximum number of keyed fields across the whole operation, including unknown fields that are
    /// skipped. Default 10,000,000.
    /// </summary>
    /// <remarks>
    /// Bounds a payload made of very many small contract objects, each of which satisfies
    /// <see cref="MaxKeyedFields"/> on its own. Field counts are schema metadata and deliberately do
    /// not consume <see cref="MaxTotalElements"/>, which counts data.
    /// </remarks>
    public long MaxTotalKeyedFields { get; init; } = 10_000_000;

    /// <summary>Maximum size of the logical, uncompressed payload. Default 64 MiB.</summary>
    /// <remarks>
    /// This is also the ceiling on decompression: compression can legitimately expand its input, so
    /// this limit is what bounds a decompression bomb.
    /// </remarks>
    public long MaxPayloadBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Maximum size of the compressed representation. Default 64 MiB.</summary>
    public long MaxCompressedBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Maximum size of the encrypted representation, which also carries nonce and tag overhead. Default 64 MiB + 64 KiB.</summary>
    public long MaxEncryptedBytes { get; init; } = 64L * 1024 * 1024 + 64L * 1024;

    /// <summary>
    /// Maximum number of bytes one operation may read from, or write to, the caller's stream.
    /// Default 80 MiB.
    /// </summary>
    /// <remarks>
    /// Counted relative to where the operation starts, so appending to a stream that already holds
    /// data costs nothing. This is the outermost bound and the cheapest one to trip, which makes it
    /// the first line of defence against an oversized message.
    /// </remarks>
    public long MaxWireBytes { get; init; } = 80L * 1024 * 1024;

    /// <summary>
    /// Verifies that every value is positive. Called once while options are built; you rarely need to
    /// call it yourself.
    /// </summary>
    /// <exception cref="Exceptions.BinaryConfigurationException">A limit is zero or negative.</exception>
    public void Validate()
    {
        Positive(MaxDepth, nameof(MaxDepth));
        Positive(MaxArrayLength, nameof(MaxArrayLength));
        Positive(MaxCollectionLength, nameof(MaxCollectionLength));
        Positive(MaxDictionaryEntries, nameof(MaxDictionaryEntries));
        Positive(MaxStringBytes, nameof(MaxStringBytes));
        Positive(MaxByteBlobBytes, nameof(MaxByteBlobBytes));
        Positive(MaxTotalElements, nameof(MaxTotalElements));
        Positive(MaxObjectGraphNodes, nameof(MaxObjectGraphNodes));
        Positive(MaxKeyedFields, nameof(MaxKeyedFields));
        Positive(MaxTotalKeyedFields, nameof(MaxTotalKeyedFields));
        Positive(MaxPayloadBytes, nameof(MaxPayloadBytes));
        Positive(MaxCompressedBytes, nameof(MaxCompressedBytes));
        Positive(MaxEncryptedBytes, nameof(MaxEncryptedBytes));
        Positive(MaxWireBytes, nameof(MaxWireBytes));
    }

    private static void Positive(long value, string name)
    {
        if (value <= 0)
            throw new BinaryConfigurationException($"{name} must be positive.");
    }
}
