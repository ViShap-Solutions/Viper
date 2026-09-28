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
/// The serializer sizes every buffer from <see cref="GetCiphertextLength"/> before it encrypts, so
/// the ciphertext length must be a function of the plaintext length alone. A cipher whose output
/// length varies from call to call, such as one that adds random padding, cannot be plugged in.
/// </para>
/// <para>
/// An implementation must be safe for concurrent use, or be supplied through a factory that returns a
/// fresh instance per resolution. A nonce must never repeat for a given key; generating it inside
/// <see cref="Encrypt"/> and storing it with the ciphertext, as <c>Aes256GcmEncryption</c> does, is the
/// simplest way to guarantee that.
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
    /// An algorithm that reports <see langword="false"/> leaves the format header as unauthenticated
    /// metadata: it cannot satisfy <c>RequireEncryption</c>, and a payload written with it is refused
    /// when read under that policy.
    /// </para>
    /// <para>
    /// Returning <see langword="true"/> is an undertaking that the associated data takes part in the
    /// authentication tag. Nothing can verify that for you — an algorithm that claims it and ignores
    /// the associated data silently removes the protection <c>RequireEncryption</c> exists to give.
    /// </para>
    /// </remarks>
    bool AuthenticatesAssociatedData { get; }

    /// <summary>The exact key length the algorithm requires, in bytes.</summary>
    /// <remarks>
    /// A fixed key given to the options builder is checked against it when the options are built; a
    /// key obtained from a provider is checked when it is resolved.
    /// </remarks>
    int KeySizeInBytes { get; }

    /// <summary>
    /// The exact length <see cref="Encrypt"/> produces for a plaintext of
    /// <paramref name="plaintextLength"/> bytes, including any nonce and authentication tag stored with
    /// the ciphertext.
    /// </summary>
    /// <param name="plaintextLength">Length of the plaintext, in bytes.</param>
    /// <returns>The ciphertext length, which is never less than <paramref name="plaintextLength"/>.</returns>
    int GetCiphertextLength(int plaintextLength);

    /// <summary>
    /// Encrypts <paramref name="plaintext"/>, binding <paramref name="associatedData"/> to the result.
    /// </summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="key">Key material of <see cref="KeySizeInBytes"/> bytes, valid only for the duration of the call.</param>
    /// <param name="associatedData">Metadata to authenticate but not encrypt.</param>
    /// <param name="destination">
    /// Exactly <see cref="GetCiphertextLength"/> of the plaintext length; the algorithm fills all of it.
    /// </param>
    /// <returns>The number of bytes written, which must equal the length of <paramref name="destination"/>.</returns>
    int Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination);

    /// <summary>
    /// Decrypts <paramref name="ciphertext"/>, verifying <paramref name="associatedData"/>.
    /// </summary>
    /// <remarks>
    /// Verification must fail if the associated data differs from what encryption bound.
    /// </remarks>
    /// <param name="ciphertext">The bytes to decrypt.</param>
    /// <param name="key">Key material of <see cref="KeySizeInBytes"/> bytes, valid only for the duration of the call.</param>
    /// <param name="associatedData">Metadata that must match what encryption bound.</param>
    /// <param name="destination">A buffer as long as <paramref name="ciphertext"/>.</param>
    /// <returns>The number of plaintext bytes written, between 0 and the ciphertext length.</returns>
    /// <exception cref="Exceptions.BinaryIntegrityException">Authentication failed: wrong key, modified data, or modified metadata.</exception>
    int Decrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination);
}
