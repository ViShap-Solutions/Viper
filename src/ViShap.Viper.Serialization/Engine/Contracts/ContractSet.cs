using System.Collections.Frozen;

namespace ViShap.Viper.Engine;

/// <summary>
/// The contracts one configuration supplies — a snapshot of its <see cref="BinarySerializerContext"/> —
/// and whether a type it does not hold may still be described by reflection (<c>RequireAll</c> refuses it). Built once by the options
/// builder and read by every operation of those options.
/// </summary>
internal sealed record ContractSet(FrozenDictionary<Type, ITypeContract> Contracts, bool RequireAll)
{

    public TypeContract<T>? Find<T>() =>
        Contracts.TryGetValue(typeof(T), out var contract) ? (TypeContract<T>)contract : null;

    public ITypeContract? Find(Type type) => Contracts.GetValueOrDefault(type);

    public BinaryConfigurationException Missing(Type type) =>
        new($"'{type}' has no contract in the configured BinarySerializerContext, and " +
            "RequireGeneratedContracts forbids describing it by reflection. Add the type to the " +
            "context's [BinaryContext] list, or to a type the context already reaches.");
}
