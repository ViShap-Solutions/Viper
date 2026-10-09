using System.Collections.Frozen;

namespace ViShap.Viper;

/// <summary>
/// A set of type contracts the serializer uses instead of the ones it builds by reflection. Derive a
/// <see langword="partial"/> class from it, list the root types with <see cref="BinaryContextAttribute"/>,
/// and the ViShap.Viper source generator writes the contracts and a <c>Default</c> instance; or add
/// contracts of your own from the constructor with <see cref="Add{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Options built with <see cref="BinarySerializerOptionsBuilder.WithContracts"/> use the context's
/// contract for each type it holds; every other type is described by reflection, unless
/// <see cref="BinarySerializerOptionsBuilder.RequireGeneratedContracts"/> refuses that. A contract
/// held for a type the serializer encodes by a dedicated formatter — a collection, a string, a
/// scalar — is never consulted.
/// </para>
/// <para>
/// A context is filled once: the first options built with it take a snapshot, and adding a contract
/// after that is refused. It holds no other state, so one context may serve any number of options and
/// threads.
/// </para>
/// <example>
/// <code>
/// [BinaryContext(typeof(Order))]
/// public partial class AppContracts : BinarySerializerContext;
///
/// var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
///     .WithContracts(AppContracts.Default)
///     .Build());
/// </code>
/// </example>
/// </remarks>
public abstract class BinarySerializerContext
{
    private readonly Dictionary<Type, ITypeContract> _contracts = [];
    private readonly Lock _gate = new();
    private FrozenDictionary<Type, ITypeContract>? _snapshot;

    /// <summary>Creates an empty context; a derived constructor adds its contracts.</summary>
    protected BinarySerializerContext() { }

    /// <summary>Adds the contract the serializer uses for <typeparamref name="T"/>.</summary>
    /// <param name="contract">The contract.</param>
    /// <typeparam name="T">The member-encoded type the contract describes.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is null.</exception>
    /// <exception cref="BinaryConfigurationException">
    /// The context already holds a contract for <typeparamref name="T"/>, or options have already been
    /// built with it.
    /// </exception>
    protected void Add<T>(TypeContract<T> contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        lock (_gate)
        {
            if (_snapshot is not null)
                throw new BinaryConfigurationException(
                    $"'{GetType()}' is already in use by built options; contracts are added before " +
                    "the context is first used, from its constructor.");

            if (!_contracts.TryAdd(typeof(T), contract))
                throw new BinaryConfigurationException(
                    $"'{GetType()}' already holds a contract for '{typeof(T)}'.");
        }
    }

    /// <summary>The contracts, frozen the first time options are built with the context.</summary>
    internal FrozenDictionary<Type, ITypeContract> Snapshot()
    {
        lock (_gate)
            return _snapshot ??= _contracts.ToFrozenDictionary();
    }
}
