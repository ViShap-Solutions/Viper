namespace ViShap.Viper.Exceptions;

/// <summary>
/// Base class for every error the serializer raises. Catching this type catches all of them, which is
/// what a caller normally wants around a deserialization of untrusted input.
/// </summary>
/// <remarks>
/// The hierarchy is part of the public contract, and each category answers a different question:
/// <list type="table">
/// <item><term><see cref="BinaryConfigurationException"/></term><description>the serializer was set up incorrectly</description></item>
/// <item><term><see cref="BinaryFormatException"/></term><description>the data is malformed or truncated</description></item>
/// <item><term><see cref="BinaryLimitException"/></term><description>the data is well-formed but exceeds a configured limit</description></item>
/// <item><term><see cref="BinaryFormatNotSupportedException"/></term><description>the data is recognized but this build cannot read it</description></item>
/// <item><term><see cref="BinaryIntegrityException"/></term><description>the data cannot be trusted: checksum, authentication tag, or a protection downgrade</description></item>
/// <item><term><see cref="BinaryEncryptionException"/></term><description>an encryption operation failed for another reason</description></item>
/// <item><term><see cref="BinaryEncryptionKeyException"/></term><description>key material is missing or does not match</description></item>
/// <item><term><see cref="BinaryStreamException"/></term><description>the underlying stream failed</description></item>
/// <item><term><see cref="BinaryTypeException"/></term><description>the CLR type, contract or object graph is invalid</description></item>
/// </list>
/// Standard argument exceptions and <see cref="NotSupportedException"/> stay outside this hierarchy:
/// they report a mistake by the calling code, not a problem with the data.
/// </remarks>
public abstract class BinarySerializerException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    protected BinarySerializerException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    protected BinarySerializerException(string message, Exception? innerException) : base(message, innerException) { }
}
