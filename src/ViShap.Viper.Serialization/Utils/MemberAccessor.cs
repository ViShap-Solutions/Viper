namespace ViShap.Viper.Utils;

internal sealed class MemberAccessor
{
    public required string Name { get; init; }
    public required Type MemberType { get; init; }
    public required Func<object, object?> Getter { get; init; }
    public required Action<object, object?> Setter { get; init; }
}