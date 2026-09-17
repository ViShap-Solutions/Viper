namespace ViShap.Viper.Exceptions;

public sealed class BinaryIntegrityException : BinarySerializerException
{
    public BinaryIntegrityException(string message) : base(message) { }
    
    public BinaryIntegrityException(string message, Exception? innerException) : base(message, innerException) { }
}
