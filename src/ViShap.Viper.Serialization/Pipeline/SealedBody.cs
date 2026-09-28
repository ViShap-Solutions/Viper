namespace ViShap.Viper.Pipeline;

/// <summary>
/// The body of an encrypted frame, held as everything its encryption needs rather than as
/// ciphertext: the plaintext, the associated data, the resolved key and the exact ciphertext length.
/// The length is known before anything is encrypted, so the frame is sized and checked against the
/// wire budget first, and the algorithm then encrypts straight into the space the destination hands
/// out — the ciphertext is never copied.
/// <para>
/// Everything that can fail on the way to the key — the ciphertext length, the key and its size — has
/// been checked when the body is built, so a destination sees only the cipher itself. The body owns
/// its buffers and the key; disposing it clears them.
/// </para>
/// </summary>
internal readonly struct SealedBody : IDisposable
{
    private readonly IEncryptionAlgorithm? _algorithm;
    private readonly RentedBytes _plaintext;
    private readonly RentedBytes _associatedData;
    private readonly SecretKey? _key;

    /// <summary>Takes ownership of <paramref name="plaintext"/>, <paramref name="associatedData"/> and <paramref name="key"/>.</summary>
    public SealedBody(
        IEncryptionAlgorithm algorithm,
        RentedBytes plaintext,
        RentedBytes associatedData,
        SecretKey key,
        int ciphertextLength)
    {
        _algorithm = algorithm;
        _plaintext = plaintext;
        _associatedData = associatedData;
        _key = key;
        Length = ciphertextLength;
    }

    /// <summary>Whether this is a body at all; the default value is none.</summary>
    public bool Exists => _algorithm is not null;

    /// <summary>The exact length of the ciphertext.</summary>
    public int Length { get; }

    /// <summary>Encrypts into the first <see cref="Length"/> bytes of <paramref name="destination"/>.</summary>
    /// <exception cref="BinaryConfigurationException">The algorithm did not fill exactly the length it stated.</exception>
    /// <exception cref="BinaryEncryptionException">The cipher failed.</exception>
    public void SealInto(Span<byte> destination) =>
        EncryptionService.Seal(
            _algorithm!, _plaintext.Span, _key!.Span, _associatedData.Span, destination[..Length]);

    /// <summary>Clears the plaintext, the associated data and the key.</summary>
    public void Dispose()
    {
        _plaintext.Dispose();
        _associatedData.Dispose();
        _key?.Dispose();
    }
}
