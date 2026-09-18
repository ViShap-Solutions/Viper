namespace ViShap.Viper;

/// <summary>
/// Declares a derived type that may appear where the annotated base class or interface is expected,
/// and the one-byte tag that identifies it on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Polymorphism is closed and explicit: only declared types travel, identified by tag. Type names are
/// never written, so a payload cannot name a type to construct — the attack surface that makes
/// unrestricted binary formatters dangerous does not exist here.
/// </para>
/// <para>
/// Writing a value whose runtime type differs from the declared type <em>without</em> a matching
/// declaration throws <see cref="Exceptions.BinaryTypeException"/>, because a reader could not
/// reconstruct it. Reading an unknown tag throws as well.
/// </para>
/// <para>
/// Apply once per derived type. Tags must be unique within the base type and fit in a byte (0-255);
/// a tag must never be reused for a different type once payloads exist.
/// </para>
/// <example>
/// <code>
/// [BinaryUnion(1, typeof(Circle))]
/// [BinaryUnion(2, typeof(Square))]
/// public abstract class Shape { }
/// </code>
/// </example>
/// </remarks>
/// <param name="tag">Wire identifier of <paramref name="derivedType"/>, in the range 0-255.</param>
/// <param name="derivedType">A type assignable to the annotated base class or interface.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class BinaryUnionAttribute(int tag, Type derivedType) : Attribute
{
    /// <summary>Wire identifier of <see cref="DerivedType"/>.</summary>
    public int Tag { get; } = tag;

    /// <summary>The runtime type this tag selects.</summary>
    public Type DerivedType { get; } = derivedType;
}
