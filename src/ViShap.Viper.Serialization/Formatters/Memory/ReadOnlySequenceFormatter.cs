using System.Buffers;

namespace ViShap.Viper.Formatters;

internal sealed class ReadOnlySequenceFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ReadOnlySequence<>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        ReadOnlySequenceAccessorCache.GetWriter(declaredType.GetGenericArguments()[0])(writer, value);

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        ReadOnlySequenceAccessorCache.GetReader(declaredType.GetGenericArguments()[0])(reader);
}