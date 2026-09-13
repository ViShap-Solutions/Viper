namespace ViShap.Viper.Utils;

internal sealed class TypeAccessorPlan
{
    public required Type Type { get; init; }
    public required MemberAccessor[] Members { get; init; }
}