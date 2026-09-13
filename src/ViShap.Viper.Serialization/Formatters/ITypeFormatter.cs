namespace ViShap.Viper.Formatters;

internal interface ITypeFormatter
{
    bool CanHandle(Type declaredType);
    void Write(BinaryPayloadWriter writer, object value, Type declaredType);
    object Read(BinaryPayloadReader reader, Type declaredType);
}