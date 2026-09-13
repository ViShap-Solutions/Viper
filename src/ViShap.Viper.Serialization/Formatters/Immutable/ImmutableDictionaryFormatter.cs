using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableDictionaryFormatter : ImmutableDictionaryFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(ImmutableDictionary<,>) || def == typeof(IImmutableDictionary<,>));

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(ImmutableDictionary<,>).MakeGenericType(keyType, valueType);
}