namespace ViShap.Viper.Formatters;

internal sealed class ReadOnlyMemoryFormatter : MemoryLikeFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>);
}