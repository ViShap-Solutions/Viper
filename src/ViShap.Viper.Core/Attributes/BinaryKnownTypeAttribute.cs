namespace ViShap.Viper;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class BinaryKnownTypeAttribute(Type derivedType) : Attribute
{
    public Type DerivedType { get; } = derivedType;
}