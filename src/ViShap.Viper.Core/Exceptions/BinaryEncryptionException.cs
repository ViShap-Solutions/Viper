namespace ViShap.Viper.Exceptions;

/// <summary>
/// An encryption or decryption operation failed for a reason that is not a format, integrity or key
/// availability problem.
/// </summary>
/// <remarks>
/// Integrity failures are <see cref="BinaryIntegrityException"/> and missing or mismatched key
/// material is <see cref="BinaryEncryptionKeyException"/>, so this type covers the remainder —
/// typically a cryptographic provider failing for an operational reason.
/// </remarks>
public class BinaryEncryptionException : BinarySerializerException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    public BinaryEncryptionException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryEncryptionException(string message, Exception? innerException)
        : base(message, innerException) { }
}
