namespace ViShap.Viper.Exceptions;

public class BinaryFormatException : BinarySerializerException
{
    public BinaryFormatException(string message) : base(message) { }
    
    public BinaryFormatException(string message, Exception? innerException) : base(message, innerException) { }
}