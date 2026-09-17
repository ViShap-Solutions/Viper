namespace ViShap.Viper.Exceptions;

public sealed class BinaryFormatNotSupportedException : BinarySerializerException
{
    public BinaryFormatNotSupportedException(string message) : base(message) { }

    public BinaryFormatNotSupportedException(string message, Exception? innerException) : base(message, innerException) { }
}