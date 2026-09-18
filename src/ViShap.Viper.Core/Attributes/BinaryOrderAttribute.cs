namespace ViShap.Viper;

/// <summary>
/// Pins the position of a member in the default positional layout.
/// </summary>
/// <remarks>
/// <para>
/// Without it, members are ordered by ordinal name, which means renaming a member silently changes
/// the format. Declaring an explicit order makes the layout independent of names.
/// </para>
/// <para>
/// Ordered members come first, sorted by <see cref="Order"/>; the rest follow in ordinal name order.
/// Duplicate values within a type throw <see cref="Exceptions.BinaryTypeException"/>. The attribute
/// has no meaning in a <see cref="BinaryContractAttribute"/> type and is rejected there.
/// </para>
/// <para>
/// Positional layout has no schema tolerance: adding, removing or reordering a member breaks
/// existing payloads. Use <see cref="BinaryContractAttribute"/> when the type has to evolve.
/// </para>
/// </remarks>
/// <param name="order">Relative position; lower values are written first.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BinaryOrderAttribute(int order) : Attribute
{
    /// <summary>Relative position of the member.</summary>
    public int Order { get; } = order;
}
