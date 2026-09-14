namespace ViShap.Viper;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class BinaryUnionAttribute(int tag, Type derivedType) : Attribute
{
    public int Tag { get; } = tag;
    public Type DerivedType { get; } = derivedType;
}