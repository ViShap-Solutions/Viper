namespace ViShap.Viper.Exceptions;

/// <summary>
/// The serializer is configured incorrectly. Raised while options are built or a serializer is
/// constructed, never while data is processed.
/// </summary>
/// <remarks>
/// Typical causes: a limit that is zero or negative, requiring encryption without an encryption
/// algorithm or with one that cannot authenticate format metadata, configuring encryption without key
/// material, requiring a checksum without a checksum algorithm, or a custom algorithm factory that
/// returns <see langword="null"/>.
/// </remarks>
public sealed class BinaryConfigurationException : BinarySerializerException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the configuration problem.</param>
    public BinaryConfigurationException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the configuration problem.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryConfigurationException(string message, Exception? innerException) : base(message, innerException) { }
}
