using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableSortedDictionaryFormatter : ImmutableDictionaryFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ImmutableSortedDictionary<,>);

    protected override Type BuilderFactoryType(Type keyType, Type valueType) => typeof(ImmutableSortedDictionary);
}