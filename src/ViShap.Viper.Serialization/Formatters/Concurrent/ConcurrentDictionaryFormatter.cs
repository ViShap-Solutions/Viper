using System.Collections.Concurrent;

namespace ViShap.Viper.Formatters;

internal sealed class ConcurrentDictionaryFormatter : DictionaryFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>);
    
    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(ConcurrentDictionary<,>).MakeGenericType(keyType, valueType);
    
    protected override string AddMethodName => "TryAdd";
}