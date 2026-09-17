namespace ViShap.Viper.Exceptions;

public class BinaryEncryptionException : BinarySerializerException
{
    public BinaryEncryptionException(string message) : base(message) { }
    
    public BinaryEncryptionException(string message, Exception? innerException) : base(message, innerException) { }
}