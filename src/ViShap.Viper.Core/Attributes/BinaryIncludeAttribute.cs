namespace ViShap.Viper;

/// <summary>
/// Includes a non-public member in the default positional layout, which otherwise covers only public
/// read/write properties and public non-readonly fields.
/// </summary>
/// <remarks>
/// Only meaningful outside <see cref="BinaryContractAttribute"/> types, where a
/// <see cref="BinaryKeyAttribute"/> already grants inclusion regardless of visibility. Combining it
/// with <see cref="BinaryIgnoreAttribute"/> is a contract error.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BinaryIncludeAttribute : Attribute;
