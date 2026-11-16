using System.Collections.Frozen;

namespace ViShap.Viper.Engine;

/// <summary>
/// The contracts one configuration supplies — a snapshot of its <see cref="BinarySerializerContext"/> —
/// and whether a type it does not hold may still be described by reflection (<c>RequireAll</c> refuses it). Built once by the options
/// builder and read by every operation of those options.
/// </summary>
internal sealed record ContractSet(FrozenDictionary<Type, ContractSlot> Contracts, bool RequireAll)
{

    public TypeContract<T>? Find<T>() =>
        Contracts.TryGetValue(typeof(T), out var slot) ? (TypeContract<T>)slot.Contract : null;

    public ITypeContract? Find(Type type) => Contracts.GetValueOrDefault(type)?.Contract;

    public BinaryConfigurationException Missing(Type type) =>
        new($"'{type}' has no contract in the configured BinarySerializerContext, and " +
            "RequireGeneratedContracts forbids describing it by reflection. Add the type to the " +
            "context's [BinaryContext] list, or to a type the context already reaches.");
}

/// <summary>
/// The contract a context holds for one type: added as an instance, or as a function that creates it the
/// first time the type is looked up. The first contract created is published and kept; a creation that
/// fails is not kept, so every use reports it the same way.
/// </summary>
internal sealed class ContractSlot
{
    private readonly Func<ITypeContract>? _create;
    private ITypeContract? _contract;

    private ContractSlot(ITypeContract? contract, Func<ITypeContract>? create)
    {
        _contract = contract;
        _create = create;
    }

    public static ContractSlot Of(ITypeContract contract) => new(contract, null);

    public static ContractSlot Deferred<T>(Func<TypeContract<T>> create) => new(null, () =>
    {
        var contract = create() ?? throw new BinaryConfigurationException(
            $"The function a context added for '{typeof(T)}' returned no contract.");

        contract.Seal();
        return contract;
    });

    /// <summary>The contract, created on the first call when the context added a function.</summary>
    public ITypeContract Contract => Volatile.Read(ref _contract) ?? Create();

    private ITypeContract Create()
    {
        var created = _create!();
        return Interlocked.CompareExchange(ref _contract, created, null) ?? created;
    }
}
