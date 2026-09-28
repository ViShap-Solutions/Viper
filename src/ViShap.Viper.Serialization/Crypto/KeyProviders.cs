using System.Security.Cryptography;
using System.Text;

namespace ViShap.Viper.Crypto;

/// <summary>
/// A single key, optionally bound to a key id.
/// </summary>
/// <remarks>
/// <para>
/// The key bytes are copied when the provider is constructed, so the array you pass in stays yours:
/// Viper never modifies or clears it, and disposing the provider clears only its own copy.
/// </para>
/// <para>
/// When a key id is supplied it is recorded in the headers this serializer writes, and reading a
/// payload that names a <em>different</em> id fails with <see cref="BinaryEncryptionKeyException"/>
/// rather than silently trying the wrong key. Give the provider an id as soon as you have more than
/// one key in circulation — it is what makes rotation safe.
/// </para>
/// </remarks>
public sealed class StaticKeyProvider : IKeyProvider, IDisposable
{
    private readonly SecretKey _key;
    private readonly string? _keyId;
    private bool _disposed;

    /// <summary>Creates a provider holding a copy of <paramref name="key"/>.</summary>
    /// <param name="key">Key material to copy. The source is not modified.</param>
    /// <param name="keyId">
    /// Identifier recorded in payloads written with this key, and checked against payloads read with
    /// it. <see langword="null"/> means the payload's id, if any, is not checked.
    /// </param>
    /// <exception cref="BinaryEncryptionKeyException"><paramref name="key"/> is empty.</exception>
    public StaticKeyProvider(ReadOnlySpan<byte> key, string? keyId = null)
    {
        _key = SecretKey.CopyFrom(key);
        _keyId = keyId;
    }

    /// <inheritdoc />
    /// <exception cref="BinaryEncryptionKeyException">
    /// The payload names a different key than this provider holds.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public SecretKey Resolve(string? keyId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_keyId is not null && keyId is not null &&
            !string.Equals(_keyId, keyId, StringComparison.Ordinal))
        {
            throw new BinaryEncryptionKeyException(
                $"This data is marked as encrypted with key '{keyId}', but the configured provider " +
                $"holds key '{_keyId}'.");
        }

        return SecretKey.CopyFrom(_key.Span);
    }

    /// <summary>
    /// Clears this provider's copy of the key. The array originally passed to the constructor is not
    /// touched.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _key.Dispose();
    }
}

/// <summary>
/// Looks a key up by id through a function you supply — a keyring, a secrets manager, a cache.
/// </summary>
/// <remarks>
/// <para>
/// The resolver receives the key id recorded in the payload header, or <see langword="null"/> when
/// the payload names none, and returns the matching key bytes. Whatever it returns is copied
/// immediately; Viper never modifies, clears or retains the array, so returning a cached buffer is
/// safe.
/// </para>
/// <para>
/// This is the usual way to support key rotation: one serializer can read payloads encrypted under
/// any key the resolver can still produce.
/// </para>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithEncryption(new Aes256GcmEncryption(), id => _keyring.Find(id), keyId: "2026-q3")
///     .Build();
/// </code>
/// </example>
/// </remarks>
/// <param name="resolver">
/// Returns the key for a given id, or <see langword="null"/> when it has none.
/// </param>
public sealed class DelegateKeyProvider(Func<string?, byte[]?> resolver) : IKeyProvider
{
    private readonly Func<string?, byte[]?> _resolver =
        resolver ?? throw new ArgumentNullException(nameof(resolver));

    /// <inheritdoc />
    /// <exception cref="BinaryEncryptionKeyException">The resolver returned no key for the id.</exception>
    public SecretKey Resolve(string? keyId)
    {
        byte[]? material = _resolver(keyId);
        if (material is null || material.Length == 0)
            throw new BinaryEncryptionKeyException(
                $"No decryption key is available for key id '{keyId ?? "(none)"}'.");

        return SecretKey.CopyFrom(material);
    }
}

/// <summary>
/// Derives a distinct key for every key id from one root key, with HKDF-SHA-256 (RFC 5869).
/// </summary>
/// <remarks>
/// <para>
/// The key for an id is <c>HKDF(rootKey, salt, info = the UTF-8 bytes of the id)</c>: the same id
/// always yields the same key, and different ids yield independent keys. One secret then serves any
/// number of ids — per tenant, per message type, per period — and rotating to a new id needs no new
/// secret to be distributed.
/// </para>
/// <para>
/// The root key is copied when the provider is constructed and is never exposed: <see cref="Resolve"/>
/// hands out only derived keys, each an owned copy that its caller disposes. The array you pass in
/// stays yours, and disposing the provider clears only its own copy.
/// </para>
/// <para>
/// A key is derived from an id, so a payload that names no id cannot be decrypted with this provider,
/// and options that write with it must give <c>WithEncryption</c> a key id.
/// </para>
/// <example>
/// <code>
/// using var keys = new HkdfKeyProvider(rootKey);
/// var options = BinarySerializerOptions.Configure()
///     .WithEncryption(new Aes256GcmEncryption(), keys, keyId: $"tenant-{tenantId}")
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed class HkdfKeyProvider : IKeyProvider, IDisposable
{
    private const int MaxDerivedKeyBytes = 255 * 32;
    private const int StackBytes = 256;

    private readonly SecretKey _rootKey;
    private readonly byte[] _salt;
    private readonly int _keySizeInBytes;
    private bool _disposed;

    /// <summary>Creates a provider that derives keys from a copy of <paramref name="rootKey"/>.</summary>
    /// <param name="rootKey">The secret every key is derived from. It is copied; the source is not modified.</param>
    /// <param name="keySizeInBytes">
    /// The length of each derived key, which must match the encryption algorithm's key size — 32 for
    /// the built-in algorithms.
    /// </param>
    /// <param name="salt">
    /// An optional non-secret value that separates this deployment's keys from any other derived from
    /// the same root key. Empty means no salt.
    /// </param>
    /// <exception cref="BinaryEncryptionKeyException"><paramref name="rootKey"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="keySizeInBytes"/> is not between 1 and 8160, the most HKDF-SHA-256 can derive.
    /// </exception>
    public HkdfKeyProvider(ReadOnlySpan<byte> rootKey, int keySizeInBytes = 32, ReadOnlySpan<byte> salt = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keySizeInBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keySizeInBytes, MaxDerivedKeyBytes);

        _rootKey = SecretKey.CopyFrom(rootKey);
        _salt = salt.ToArray();
        _keySizeInBytes = keySizeInBytes;
    }

    /// <inheritdoc />
    /// <exception cref="BinaryEncryptionKeyException"><paramref name="keyId"/> is null: there is no id to derive a key from.</exception>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public SecretKey Resolve(string? keyId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (keyId is null)
            throw new BinaryEncryptionKeyException(
                "A key is derived from a key id, but none was given. Write with a key id so that " +
                "readers can derive the same key.");

        int infoLength = Encoding.UTF8.GetByteCount(keyId);
        Span<byte> info = infoLength <= StackBytes ? stackalloc byte[StackBytes] : new byte[infoLength];
        info = info[..Encoding.UTF8.GetBytes(keyId, info)];

        Span<byte> derived = _keySizeInBytes <= StackBytes
            ? stackalloc byte[StackBytes]
            : new byte[_keySizeInBytes];
        derived = derived[.._keySizeInBytes];

        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, _rootKey.Span, derived, _salt, info);
            return SecretKey.CopyFrom(derived);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
        }
    }

    /// <summary>
    /// Clears this provider's copy of the root key. The array originally passed to the constructor is
    /// not touched.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _rootKey.Dispose();
    }
}
