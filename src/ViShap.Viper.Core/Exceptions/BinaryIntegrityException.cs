namespace ViShap.Viper.Exceptions;

/// <summary>
/// The data cannot be trusted as intact and authentic.
/// </summary>
/// <remarks>
/// <para>
/// Raised on a checksum mismatch, on an authenticated-encryption tag failure — which also covers a
/// tampered header, since format metadata is bound to the tag — and on a protection downgrade, when a
/// serializer configured with <c>RequireEncryption</c> or <c>RequireChecksum</c> is handed a payload
/// without it.
/// </para>
/// <para>
/// A wrong decryption key surfaces here too: authenticated decryption cannot tell a wrong key from
/// modified data, and both mean the same thing to the caller — do not use this payload.
/// </para>
/// </remarks>
public sealed class BinaryIntegrityException : BinarySerializerException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the integrity failure.</param>
    public BinaryIntegrityException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the integrity failure.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryIntegrityException(string message, Exception? innerException)
        : base(message, innerException) { }
}
