using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableListFormatter : ImmutableBuilderFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(ImmutableList<>) || def == typeof(IImmutableList<>));

    protected override Type ConcreteType(Type elementType) =>
        typeof(ImmutableList<>).MakeGenericType(elementType);
}