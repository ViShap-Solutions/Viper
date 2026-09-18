namespace ViShap.Viper;

/// <summary>
/// Assigns the wire key of a member inside a <see cref="BinaryContractAttribute"/> type.
/// </summary>
/// <remarks>
/// <para>
/// Keys identify members across schema versions, so a key must be unique within its type and must
/// never be reused for a different member once payloads exist. Members are written in ascending key
/// order; the numeric value affects size only slightly (keys are 7-bit encoded), so small keys are
/// marginally cheaper.
/// </para>
/// <para>
/// The attribute is only valid on a contract type. Using it without
/// <see cref="BinaryContractAttribute"/>, combining it with <see cref="BinaryIgnoreAttribute"/>, or
/// reusing a key throws <see cref="Exceptions.BinaryTypeException"/>.
/// </para>
/// </remarks>
/// <param name="key">A non-negative identifier, unique within the declaring type.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BinaryKeyAttribute(int key) : Attribute
{
    /// <summary>The wire key of the member.</summary>
    public int Key { get; } = key;
}
