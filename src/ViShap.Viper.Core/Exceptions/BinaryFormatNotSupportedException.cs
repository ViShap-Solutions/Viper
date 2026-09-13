namespace ViShap.Viper.Exceptions;

public sealed class BinaryFormatNotSupportedException(string message) : BinarySerializerException(message);
