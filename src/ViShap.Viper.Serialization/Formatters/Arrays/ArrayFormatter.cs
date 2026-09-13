namespace ViShap.Viper.Formatters;

internal sealed class ArrayFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType.IsArray && declaredType.GetArrayRank() == 1;

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var array = (Array)value;
        var elementType = declaredType.GetElementType()!;
        writer.WriteInt32(array.Length);
        foreach (var item in array) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetElementType()!;
        int count = reader.ReadInt32();
        var array = Array.CreateInstance(elementType, count);
        for (int i = 0; i < count; i++) array.SetValue(reader.ReadElement(elementType), i);
        return array;
    }
}