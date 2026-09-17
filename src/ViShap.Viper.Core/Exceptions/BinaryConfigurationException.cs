namespace ViShap.Viper.Exceptions;

public sealed class BinaryConfigurationException : BinarySerializerException
{
    public BinaryConfigurationException(string message) : base(message) { }
    
    public BinaryConfigurationException(string message, Exception? innerException) : base(message, innerException) { }
}