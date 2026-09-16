using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableHashSetFormatter : ImmutableBuilderFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(ImmutableHashSet<>) || def == typeof(IImmutableSet<>));

    protected override Type BuilderFactoryType(Type elementType) => typeof(ImmutableHashSet);
}