namespace ViShap.Viper.Formatters;

internal sealed class MemoryFormatter : MemoryLikeFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(Memory<>);
}