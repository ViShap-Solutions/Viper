namespace ViShap.Viper.Exceptions;

/// <summary>
/// The binary data is malformed: truncated, structurally invalid, or inconsistent with what it
/// declares about itself.
/// </summary>
/// <remarks>
/// <para>
/// Typical causes: a truncated header or payload, a malformed variable-length integer, a negative
/// count or length, an invalid reference marker, a declared length longer than the bytes present,
/// trailing bytes after the root value, or decompression output that does not match the declared
/// size.
/// </para>
/// <para>
/// Raw parser exceptions never escape as themselves: end-of-stream and similar framework errors are
/// reported here, with the original preserved as <see cref="Exception.InnerException"/> when it adds
/// diagnostic value.
/// </para>
/// <para><see cref="BinaryLimitException"/> derives from this type, so catching it also catches limit breaches.</para>
/// </remarks>
public class BinaryFormatException : BinarySerializerException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    public BinaryFormatException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryFormatException(string message, Exception? innerException) : base(message, innerException) { }
}
