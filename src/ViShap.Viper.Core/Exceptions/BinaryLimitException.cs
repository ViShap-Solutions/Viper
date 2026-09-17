namespace ViShap.Viper.Exceptions;

public sealed class BinaryLimitException : BinaryFormatException
{
    public BinaryLimitException(string message) : base(message) { }
    
    public BinaryLimitException(string message, Exception? innerException) : base(message, innerException) { }
}