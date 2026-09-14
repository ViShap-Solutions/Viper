namespace ViShap.Viper;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BinaryKeyAttribute(int key) : Attribute
{
    public int Key { get; } = key;
}