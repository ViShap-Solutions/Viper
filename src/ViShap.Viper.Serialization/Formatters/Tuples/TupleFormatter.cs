namespace ViShap.Viper.Formatters;

internal sealed class TupleFormatter : ITypeFormatter
{
    private static readonly HashSet<Type> Definitions =
    [
        typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>),
        typeof(Tuple<,,,,>), typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>), typeof(Tuple<,,,,,,,>),
        typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>),
        typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>), typeof(ValueTuple<,,,,,,,>),
    ];

    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && Definitions.Contains(declaredType.GetGenericTypeDefinition());

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var accessors = TupleAccessorCache.GetAccessors(declaredType);
        for (int i = 0; i < accessors.ArgTypes.Length; i++)
        {
            writer.WriteElement(accessors.Getters[i](value), accessors.ArgTypes[i]);
        }
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var accessors = TupleAccessorCache.GetAccessors(declaredType);
        var values = new object?[accessors.ArgTypes.Length];
        for (int i = 0; i < accessors.ArgTypes.Length; i++)
        {
            values[i] = reader.ReadElement(accessors.ArgTypes[i]);
        }

        return accessors.Construct(values);
    }
}