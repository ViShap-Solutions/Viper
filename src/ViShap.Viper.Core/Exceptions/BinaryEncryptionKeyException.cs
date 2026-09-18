namespace ViShap.Viper.Exceptions;

/// <summary>
/// Key material is unavailable, unusable, or does not match the key the payload names.
/// </summary>
/// <remarks>
/// <para>
/// Typical causes: an encrypted payload read by a serializer with no key provider, a key resolver
/// that returns nothing for the requested id, a payload written with a different key id than the
/// configured provider holds, or a key of the wrong length for the algorithm.
/// </para>
/// <para>
/// A key id is a selector, not a secret: it travels in the header in the clear so a reader can pick
/// the right key. Key bytes never travel.
/// </para>
/// </remarks>
public sealed class BinaryEncryptionKeyException : BinaryEncryptionException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the key problem.</param>
    public BinaryEncryptionKeyException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the key problem.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryEncryptionKeyException(string message, Exception? innerException)
        : base(message, innerException) { }
}
