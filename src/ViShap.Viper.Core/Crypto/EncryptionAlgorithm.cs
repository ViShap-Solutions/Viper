namespace ViShap.Viper.Crypto;

/// <summary>
/// Identifies the encryption applied to a payload. The value is recorded in the header, so a reader
/// can pick the right algorithm; the key itself is never recorded.
/// </summary>
public enum EncryptionAlgorithm : byte
{
    /// <summary>No encryption; the payload is stored as produced.</summary>
    None = 0,

    /// <summary>
    /// AES-256 in GCM mode: confidentiality plus authentication, including of the format metadata.
    /// </summary>
    Aes256Gcm = 1,

    /// <summary>
    /// A user-supplied algorithm, identified by name. Register it with
    /// <c>BinarySerializerOptions.Configure().RegisterCustomEncryption(...)</c> before reading a
    /// payload that names it.
    /// </summary>
    Custom = 255
}
