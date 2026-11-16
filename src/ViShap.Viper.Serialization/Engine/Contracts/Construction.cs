namespace ViShap.Viper.Engine;

/// <summary>How a contract builds an instance from the values read for its members.</summary>
internal abstract class Construction<T>
{
    /// <summary>Reads the members into the values and calls the constructor with them.</summary>
    public abstract T Read(ref WireReader reader, ITypeContract description, bool nullFolded);
}

/// <summary>
/// Reads the members into <typeparamref name="TValues"/>, started by the contract, through the values'
/// accessor table, then constructs the instance from them.
/// </summary>
internal sealed class Construction<T, TValues>(
    Func<TValues> start,
    Accessor<TValues>[] values,
    MemberConstructor<TValues, T> construct) : Construction<T>
{
    public override T Read(ref WireReader reader, ITypeContract description, bool nullFolded)
    {
        var target = start();
        ObjectMembers.ReadValues(ref reader, description, values, ref target, nullFolded);

        var instance = construct(ref target);
        if (!typeof(T).IsValueType && instance is null)
            throw new BinaryTypeException(
                $"The contract of '{typeof(T)}' constructed null instead of an instance from the values it read.");

        return instance;
    }
}
