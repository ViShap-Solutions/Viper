using System.Collections.Frozen;

namespace ViShap.Viper;

/// <summary>
/// A set of type contracts the serializer uses instead of the ones it builds by reflection. Derive a
/// <see langword="partial"/> class from it, list the root types with <see cref="BinaryContextAttribute"/>,
/// and the ViShap.Viper source generator writes the contracts and a <c>Default</c> instance; or add
/// contracts of your own from the constructor with <see cref="Add{T}(TypeContract{T})"/> or, to create
/// each one when its type is first used, <see cref="Add{T}(Func{TypeContract{T}})"/>.
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
    private readonly Dictionary<Type, ContractSlot> _contracts = [];
    private readonly HashSet<Type> _supported = [];
    private readonly Lock _gate = new();
    private FrozenDictionary<Type, ContractSlot>? _snapshot;

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

            if (!_contracts.TryAdd(typeof(T), ContractSlot.Of(contract)))
                throw new BinaryConfigurationException(
                    $"'{GetType()}' already holds a contract for '{typeof(T)}'.");

            contract.Seal();
        }
    }

    /// <summary>
    /// Adds the contract the serializer uses for <typeparamref name="T"/>, created by
    /// <paramref name="contract"/> the first time an operation looks the type up, so a context pays for
    /// the contracts of the types a process uses and no others.
    /// </summary>
    /// <param name="contract">
    /// Creates the contract. It is called at most once for each use that finds no contract yet: several
    /// threads may call it at once, and one of the contracts they create is kept. An exception it throws
    /// is raised by the operation that looked the type up, and is not kept, so the next use calls it again.
    /// </param>
    /// <typeparam name="T">The member-encoded type the contract describes.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is null.</exception>
    /// <exception cref="BinaryConfigurationException">
    /// The context already holds a contract for <typeparamref name="T"/>, or options have already been
    /// built with it. An operation raises it as well when <paramref name="contract"/> returns null.
    /// </exception>
    protected void Add<T>(Func<TypeContract<T>> contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        lock (_gate)
        {
            if (_snapshot is not null)
                throw new BinaryConfigurationException(
                    $"'{GetType()}' is already in use by built options; contracts are added before " +
                    "the context is first used, from its constructor.");

            if (!_contracts.TryAdd(typeof(T), ContractSlot.Deferred(contract)))
                throw new BinaryConfigurationException(
                    $"'{GetType()}' already holds a contract for '{typeof(T)}'.");
        }
    }

    /// <summary>
    /// Makes <paramref name="type"/> supported: the serializer encodes it as the entry of the table of
    /// supported types that <see cref="SupportedTypes"/> closed over it, without building anything for
    /// it by reflection.
    /// </summary>
    /// <param name="type">A value created by one of the methods of <see cref="SupportedTypes"/>.</param>
    /// <remarks>
    /// The encoding of a type is the same whichever context supplies it, so the first context of the
    /// process that adds a type supplies it for every serializer.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    /// <exception cref="BinaryConfigurationException">
    /// The context already added the same type, or options have already been built with it.
    /// </exception>
    protected void Add(SupportedType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (_gate)
        {
            if (_snapshot is not null)
                throw new BinaryConfigurationException(
                    $"'{GetType()}' is already in use by built options; supported types are added before " +
                    "the context is first used, from its constructor.");

            if (!_supported.Add(type.Type))
                throw new BinaryConfigurationException(
                    $"'{GetType()}' already makes '{type.Type}' supported.");

            type.Offer();
        }
    }

    /// <summary>
    /// The type information of <typeparamref name="T"/>, which the serializer's methods take in place of
    /// building anything for the type by reflection.
    /// </summary>
    /// <typeparam name="T">A type the context made supported with <see cref="Add(SupportedType)"/>, or a scalar.</typeparam>
    /// <returns>The type information, bound to this context.</returns>
    /// <exception cref="BinaryConfigurationException">The context does not describe <typeparamref name="T"/>.</exception>
    protected BinaryTypeInfo<T> TypeInfo<T>() =>
        TryGetTypeInfo<T>() ?? throw new BinaryConfigurationException(
            $"'{GetType()}' does not make '{typeof(T)}' supported; add SupportedTypes for it before " +
            "creating its type information.");

    /// <summary>
    /// The type information of <typeparamref name="T"/> when the context describes it — a type it made
    /// supported, or a scalar — and otherwise <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The declared type.</typeparam>
    /// <returns>The type information, bound to this context, or <see langword="null"/>.</returns>
    public BinaryTypeInfo<T>? TryGetTypeInfo<T>()
    {
        bool described;
        lock (_gate)
            described = _supported.Contains(typeof(T));

        return described || BuiltInCodecs.Scalars.ContainsKey(typeof(T)) ? new BinaryTypeInfo<T>(this) : null;
    }

    /// <summary>The contracts, frozen the first time options are built with the context.</summary>
    internal FrozenDictionary<Type, ContractSlot> Snapshot()
    {
        lock (_gate)
            return _snapshot ??= _contracts.ToFrozenDictionary();
    }
}
