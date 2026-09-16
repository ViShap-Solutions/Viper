using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableSortedSetFormatter : ImmutableBuilderFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ImmutableSortedSet<>);

    protected override Type BuilderFactoryType(Type elementType) => typeof(ImmutableSortedSet);
}