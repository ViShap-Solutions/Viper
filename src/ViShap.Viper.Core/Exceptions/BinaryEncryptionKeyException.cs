namespace ViShap.Viper.Exceptions;

public sealed class BinaryEncryptionKeyException : BinaryEncryptionException
{
    public BinaryEncryptionKeyException(string message) : base(message) { }

    public BinaryEncryptionKeyException(string message, Exception? innerException) : base(message, innerException) { }
}