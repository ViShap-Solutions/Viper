namespace ViShap.Viper.Crypto;

/// <summary>
/// An encryption primitive. Implement it to plug a cipher of your own into Viper.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are pure mechanics: they transform spans under a key. They never own key material,
/// never see the framing around the payload, and never enforce a resource limit — the serializer does
/// all three around the call.
/// </para>
/// <para>
/// Prefer an AEAD construction. Viper passes the payload's format metadata as associated data, so an
/// algorithm that authenticates it makes the header unforgeable: changing a single header byte then
/// fails verification. An algorithm that ignores the associated data leaves that metadata malleable,
/// which is why it must report <see cref="AuthenticatesAssociatedData"/> as <see langword="false"/>
/// and cannot be used with <c>RequireEncryption</c>.
/// </para>
/// <para>
/// An implementation must be safe for concurrent use, or be supplied through a factory that returns a
/// fresh instance per resolution. A nonce must never repeat for a given key; generating it inside
/// <see cref="Encrypt(ReadOnlySpan{byte}, ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte})"/> and
/// prefixing it to the ciphertext, as <c>Aes256Gcm</c> does, is the simplest way to guarantee that.
/// </para>
/// </remarks>
public interface IEncryptionAlgorithm
{
    /// <summary>The identifier recorded in the payload header.</summary>
    EncryptionAlgorithm Kind { get; }

    /// <summary>
    /// The name recorded in the header when <see cref="Kind"/> is
    /// <see cref="EncryptionAlgorithm.Custom"/>; otherwise <see langword="null"/>.
    /// </summary>
    string? CustomName { get; }

    /// <summary>
    /// <see langword="true"/> when the algorithm authenticates the associated data it is given, and
    /// therefore protects the payload's format metadata against tampering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see langword="false"/>, which is correct for an implementation that does not
    /// override the associated-data overloads. Such an algorithm leaves the format header as
    /// unauthenticated metadata: it cannot satisfy <c>RequireEncryption</c>, and a payload written
    /// with it is refused when read under that policy.
    /// </para>
    /// <para>
    /// The associated data is always passed to the associated-data overloads, whatever this property
    /// says, so returning <see langword="true"/> is an undertaking that those bytes take part in the
    /// authentication tag. Nothing can verify that for you — an algorithm that claims it and ignores
    /// the associated data silently removes the protection <c>RequireEncryption</c> exists to give.
    /// </para>
    /// </remarks>
    bool AuthenticatesAssociatedData => false;

    /// <summary>
    /// The largest output <see cref="Encrypt(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte})"/>
    /// can produce for a plaintext of <paramref name="plaintextLength"/> bytes, including any nonce
    /// and authentication tag the algorithm stores alongside the ciphertext.
    /// </summary>
    /// <param name="plaintextLength">Length of the plaintext, in bytes.</param>
    /// <returns>An upper bound on the ciphertext size, in bytes.</returns>
    int GetMaxCiphertextLength(int plaintextLength);

    /// <summary>Encrypts without associated data.</summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="key">Key material, valid only for the duration of the call.</param>
    /// <param name="destination">Output buffer, at least <see cref="GetMaxCiphertextLength"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, Span<byte> destination);

    /// <summary>Decrypts without associated data.</summary>
    /// <param name="ciphertext">The bytes to decrypt.</param>
    /// <param name="key">Key material, valid only for the duration of the call.</param>
    /// <param name="destination">Output buffer for the plaintext.</param>
    /// <returns>The number of plaintext bytes written.</returns>
    /// <exception cref="Exceptions.BinaryIntegrityException">Authentication failed: wrong key or modified data.</exception>
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, Span<byte> destination);

    /// <summary>
    /// Encrypts <paramref name="plaintext"/>, binding <paramref name="associatedData"/> to the result.
    /// </summary>
    /// <remarks>
    /// Viper always calls this overload, passing the payload's format metadata. The default
    /// implementation discards the associated data and forwards to
    /// <see cref="Encrypt(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte})"/>, which is why
    /// <see cref="AuthenticatesAssociatedData"/> defaults to <see langword="false"/>.
    /// </remarks>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="key">Key material, valid only for the duration of the call.</param>
    /// <param name="associatedData">Metadata to authenticate but not encrypt.</param>
    /// <param name="destination">Output buffer, at least <see cref="GetMaxCiphertextLength"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    int Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination) => Encrypt(plaintext, key, destination);

    /// <summary>
    /// Decrypts <paramref name="ciphertext"/>, verifying <paramref name="associatedData"/>.
    /// </summary>
    /// <remarks>
    /// Viper always calls this overload. Verification must fail if the associated data differs from
    /// what encryption bound.
    /// </remarks>
    /// <param name="ciphertext">The bytes to decrypt.</param>
    /// <param name="key">Key material, valid only for the duration of the call.</param>
    /// <param name="associatedData">Metadata that must match what encryption bound.</param>
    /// <param name="destination">Output buffer for the plaintext.</param>
    /// <returns>The number of plaintext bytes written.</returns>
    /// <exception cref="Exceptions.BinaryIntegrityException">Authentication failed: wrong key, modified data, or modified metadata.</exception>
    int Decrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination) => Decrypt(ciphertext, key, destination);
}
