namespace ViShap.Viper.Exceptions;

/// <summary>
/// The underlying stream failed. The original <see cref="IOException"/> is preserved as
/// <see cref="Exception.InnerException"/>.
/// </summary>
/// <remarks>
/// This is an infrastructure failure — a disk, socket or pipe problem — not a statement about the
/// data. The serializer never disposes a caller-owned stream, and after a failure the stream is left
/// where the failure occurred, except for inspection APIs that promise to restore the position.
/// </remarks>
public sealed class BinaryStreamException : BinarySerializerException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the stream failure.</param>
    public BinaryStreamException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the underlying I/O failure.</summary>
    /// <param name="message">Description of the stream failure.</param>
    /// <param name="innerException">The underlying I/O failure.</param>
    public BinaryStreamException(string message, Exception? innerException)
        : base(message, innerException) { }
}
