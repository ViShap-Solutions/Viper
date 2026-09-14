namespace ViShap.Viper.Utils;

internal sealed class TypeAccessorPlan
{
    public required Type Type { get; init; }
    public required MemberAccessor[] Members { get; init; }
    public IReadOnlyDictionary<int, MemberAccessor>? MembersByKey { get; init; }
    public bool UseKeyedEncoding => MembersByKey is not null;
}