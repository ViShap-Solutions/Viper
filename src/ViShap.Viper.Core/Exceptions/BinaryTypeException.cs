namespace ViShap.Viper.Exceptions;

public sealed class BinaryTypeException : BinarySerializerException
{
    public BinaryTypeException(string message) : base(message) { }

    public BinaryTypeException(string message, Exception? innerException) : base(message, innerException) { }
}
