namespace ViShap.Viper;

/// <summary>
/// Excludes a member from serialization. The member keeps its default value after deserialization.
/// </summary>
/// <remarks>
/// Use it for derived or cached state, and for anything that must not reach the wire. In a
/// <see cref="BinaryContractAttribute"/> type it is the explicit alternative to
/// <see cref="BinaryKeyAttribute"/>; combining the two is a contract error, so a member can never be
/// silently included against the author's intent.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BinaryIgnoreAttribute : Attribute;
