namespace ViShap.Viper.Formatters;

internal abstract class MemoryLikeFormatterBase : ITypeFormatter
{
    public abstract bool CanHandle(Type declaredType);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var array = (Array)MethodInvokerCache.GetInstanceFinalizerInvoker(declaredType, "ToArray")(value);
        writer.WriteInt32(array.Length);
        foreach (var item in array) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        int count = reader.ReadInt32();
        var array = Array.CreateInstance(elementType, count);
        for (int i = 0; i < count; i++) array.SetValue(reader.ReadElement(elementType), i);

        return ActivatorCache.GetOneArgConstructor(declaredType, elementType.MakeArrayType())(array);
    }
}