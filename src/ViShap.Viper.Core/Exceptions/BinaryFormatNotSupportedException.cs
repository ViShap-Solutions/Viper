namespace ViShap.Viper.Exceptions;

/// <summary>
/// The data is recognized but this build cannot process it.
/// </summary>
/// <remarks>
/// Typical causes: a format version this library does not implement, an algorithm identifier it does
/// not know, or a custom algorithm name that was never registered on the options builder.
/// </remarks>
public sealed class BinaryFormatNotSupportedException : BinarySerializerException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the unsupported format or algorithm.</param>
    public BinaryFormatNotSupportedException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the unsupported format or algorithm.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryFormatNotSupportedException(string message, Exception? innerException)
        : base(message, innerException) { }
}
