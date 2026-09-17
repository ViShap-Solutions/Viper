namespace ViShap.Viper.Exceptions;

public sealed class BinaryStreamException : BinarySerializerException
{
    public BinaryStreamException(string message) : base(message) { }

    public BinaryStreamException(string message, Exception? innerException) : base(message, innerException) { }
}