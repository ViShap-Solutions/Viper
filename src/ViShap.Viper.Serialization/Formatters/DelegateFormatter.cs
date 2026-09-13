namespace ViShap.Viper.Formatters;

internal sealed class DelegateFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => typeof(Delegate).IsAssignableFrom(declaredType);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        throw new BinaryTypeException(
            $"Delegate types cannot be serialized ('{declaredType}') — as a root value, a member, " +
            "or a collection element. Exclude the containing member with [BinaryIgnore] instead.");

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        throw new BinaryTypeException($"Delegate types cannot be deserialized ('{declaredType}').");
}