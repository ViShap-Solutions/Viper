using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableArrayFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ImmutableArray<>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];

        bool isDefault = (bool)MethodInvokerCache.GetInstanceFinalizerInvoker(declaredType, "get_IsDefault")(value);
        writer.WriteBool(!isDefault);
        if (isDefault) return;

        var array = (Array)ImmutableFactoryCache.GetStructFactory(typeof(ImmutableCollectionsMarshal), "AsArray", elementType)(value);
        var items = array.Cast<object?>().ToList();
        writer.WriteInt32(items.Count);
        foreach (var item in items) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        bool hasValue = reader.ReadBool();
        if (!hasValue) return ActivatorCache.CreateInstance(declaredType);

        int count = DeserializationGuard.ValidateCount(
            reader, 
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxArrayLength, 
            "ImmutableArray length");
        
        var array = DeserializationGuard.ReadIntoArray(
            reader, 
            elementType, 
            count);

        return ImmutableFactoryCache.GetArrayFactory(typeof(ImmutableCollectionsMarshal), "AsImmutableArray", elementType)(array);
    }
}