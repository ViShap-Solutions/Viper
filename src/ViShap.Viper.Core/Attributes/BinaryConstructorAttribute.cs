namespace ViShap.Viper;

/// <summary>
/// Names the constructor a member-encoded class or struct is read through: its parameters receive the
/// values of the members with the same names (ignoring case) and types, and every other member read is
/// assigned after it.
/// </summary>
/// <remarks>
/// A marked constructor also brings in the members only a constructor can set — properties without a
/// setter and read-only fields — that its parameters name. A marked parameterless constructor is called
/// before the members are assigned, on a struct instead of creating it as <see langword="default"/>.
/// Without the attribute a class is read through its parameterless constructor of any visibility, and
/// otherwise through its single public constructor whose parameters each name one of its members; a
/// struct is created as <see langword="default"/>. At most one constructor of a type carries the attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Constructor)]
public sealed class BinaryConstructorAttribute : Attribute;
