namespace ViShap.Viper.Exceptions;

public abstract class BinarySerializerException : Exception
{
    protected BinarySerializerException(string message) : base(message) { }
    protected BinarySerializerException(string message, Exception? innerException) : base(message, innerException) { }
}
