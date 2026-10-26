using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Engine;

/// <summary>
/// The type contract built by reflection: one typed accessor per member, in the order of the
/// description, each compiled once. Nothing is boxed — a member travels as its own type from the
/// getter to its codec and from its codec to the setter, and a struct owner is assigned in place.
/// </summary>
internal sealed class ReflectedContract<T> : TypeContract<T>
{
    private readonly MemberAccessor<T>[] _accessors;
    private readonly FrozenDictionary<int, MemberAccessor<T>> _byKey;
    private readonly Func<T>? _create;

    internal ReflectedContract(
        MemberLayout layout,
        MemberAccessor<T>[] accessors,
        bool canBeConstructed,
        ConstructorInfo? constructor)
        : base(layout, [.. accessors.Select(accessor => accessor.Description)], canBeConstructed)
    {
        _accessors = accessors;
        _byKey = layout == MemberLayout.Keyed
            ? accessors.ToFrozenDictionary(accessor => accessor.Description.Key!.Value)
            : FrozenDictionary<int, MemberAccessor<T>>.Empty;
        _create = constructor is null
            ? null
            : Expression.Lambda<Func<T>>(Expression.New(constructor)).Compile();
    }

    public override T Create() =>
        typeof(T).IsValueType ? default! :
        _create is not null ? _create() :
        throw new BinaryTypeException($"'{typeof(T)}' cannot be constructed during deserialization.");

    public override void Write(ref MemberWriter writer, in T value)
    {
        foreach (var accessor in _accessors)
            accessor.WriteTo(ref writer, in value);
    }

    public override void ReadPositional(ref MemberReader reader, ref T value)
    {
        foreach (var accessor in _accessors)
            accessor.ReadPositionalFrom(ref reader, ref value);
    }

    public override bool ReadKeyed(ref MemberReader reader, int key, ref T value) =>
        _byKey.TryGetValue(key, out var accessor) && accessor.ReadKeyedFrom(ref reader, ref value);
}

internal delegate TMember Getter<T, TMember>(in T owner);

internal delegate void Setter<T, TMember>(ref T owner, TMember value);

/// <summary>Reads and writes one member of <typeparamref name="T"/> through a <see cref="MemberWriter"/> or <see cref="MemberReader"/>.</summary>
internal abstract class MemberAccessor<T>(MemberDescription description)
{
    public MemberDescription Description { get; } = description;

    public abstract void WriteTo(ref MemberWriter writer, in T owner);

    public abstract void ReadPositionalFrom(ref MemberReader reader, ref T owner);

    public abstract bool ReadKeyedFrom(ref MemberReader reader, ref T owner);
}

/// <summary>One member of <typeparamref name="T"/>, of type <typeparamref name="TMember"/>, with its getter and setter compiled once.</summary>
internal sealed class MemberAccessor<T, TMember> : MemberAccessor<T>
{
    private readonly Getter<T, TMember> _get;
    private readonly Setter<T, TMember> _set;
    private readonly int? _key;

    public MemberAccessor(MemberDescription description, MemberInfo member)
        : base(description)
    {
        _key = description.Key;

        var owner = Expression.Parameter(typeof(T).MakeByRefType(), "owner");
        var value = Expression.Parameter(typeof(TMember), "value");
        Expression access = member is FieldInfo field
            ? Expression.Field(owner, field)
            : Expression.Property(owner, (PropertyInfo)member);

        _get = Expression.Lambda<Getter<T, TMember>>(access, owner).Compile();
        _set = Expression.Lambda<Setter<T, TMember>>(Expression.Assign(access, value), owner, value).Compile();
    }

    public override void WriteTo(ref MemberWriter writer, in T owner)
    {
        if (_key is { } key)
            writer.Member(key, _get(in owner));
        else
            writer.Member(_get(in owner));
    }

    public override void ReadPositionalFrom(ref MemberReader reader, ref T owner) =>
        _set(ref owner, reader.Member<TMember>());

    public override bool ReadKeyedFrom(ref MemberReader reader, ref T owner)
    {
        _set(ref owner, reader.Member<TMember>());
        return true;
    }
}

/// <summary>
/// Builds the <see cref="ReflectedContract{T}"/> of a type: collects its members level by level up
/// the inheritance chain, applies every inclusion rule and every rejection, orders them, and
/// compiles one accessor per member.
/// </summary>
[RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
[RequiresDynamicCode(ReflectionPath.DynamicCode)]
internal static class ReflectedContract
{
    public static ITypeContract Build(Type type)
    {
        var candidates = Candidates(type).ToArray();
        bool isContract = type.GetCustomAttribute<BinaryContractAttribute>() is not null;

        var constructor = type.IsValueType || type.IsAbstract
            ? null
            : type.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                Type.EmptyTypes);

        bool canBeConstructed = type.IsValueType || constructor is not null;

        var (layout, members) = isContract
            ? (MemberLayout.Keyed, Keyed(type, candidates))
            : (MemberLayout.Positional, Positional(type, candidates));

        var accessors = Array.CreateInstance(typeof(MemberAccessor<>).MakeGenericType(type), members.Length);
        for (int i = 0; i < members.Length; i++)
            accessors.SetValue(Accessor(type, members[i]), i);

        return (ITypeContract)Activator.CreateInstance(
            typeof(ReflectedContract<>).MakeGenericType(type),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [layout, accessors, canBeConstructed, constructor],
            culture: null)!;
    }

    private static object Accessor(Type type, Candidate candidate)
    {
        Type closed;
        try
        {
            closed = typeof(MemberAccessor<,>).MakeGenericType(type, candidate.MemberType);
        }
        catch (ArgumentException ex)
        {
            throw new BinaryTypeException(
                $"'{type}' member '{candidate.Name}' is of type '{candidate.MemberType}', which has no " +
                "representation on the wire. Mark it [BinaryIgnore].", ex);
        }

        var description = new MemberDescription(candidate.Name, candidate.MemberType, candidate.Key);
        return Activator.CreateInstance(closed, description, candidate.Member)!;
    }

    private static Candidate[] Positional(Type type, Candidate[] candidates)
    {
        var stray = candidates.Where(c => c.Key is not null).ToArray();
        if (stray.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{Join(stray)}] have [BinaryKey], but '{type.Name}' is not " +
                "marked [BinaryContract] — [BinaryKey] only applies to contract types.");

        var contradictory = candidates.Where(c => c.HasIgnore && c.HasInclude).ToArray();
        if (contradictory.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{Join(contradictory)}] have both [BinaryInclude] and " +
                "[BinaryIgnore] — a member needs at most one of them.");

        var eligible = candidates
            .Where(c => !c.HasIgnore)
            .Where(c => c.IsPubliclyVisible || c.HasInclude)
            .ToArray();

        RejectDelegates(type, eligible);

        var duplicateOrders = eligible
            .Where(c => c.Order != int.MaxValue)
            .GroupBy(c => c.Order)
            .Where(g => g.Count() > 1)
            .ToArray();

        if (duplicateOrders.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' has duplicate [BinaryOrder] value(s): " +
                $"{string.Join(", ", duplicateOrders.Select(g => g.Key))}.");

        // Order, then ordinal name, then the declaring level — a total order, so the plan never
        // depends on the order reflection happened to return members in.
        return
        [
            .. eligible
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ThenByDescending(c => c.Distance)
        ];
    }

    private static Candidate[] Keyed(Type type, Candidate[] candidates)
    {
        var strayInclude = candidates.Where(c => c.HasInclude).ToArray();
        if (strayInclude.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{Join(strayInclude)}] have [BinaryInclude], which has no " +
                "meaning under [BinaryContract] — [BinaryKey] already grants inclusion regardless " +
                "of visibility. Remove [BinaryInclude].");

        var strayOrder = candidates.Where(c => c.Order != int.MaxValue).ToArray();
        if (strayOrder.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{Join(strayOrder)}] have [BinaryOrder], which has no meaning " +
                "under [BinaryContract] — order is determined by the [BinaryKey] value.");

        var contradictory = candidates.Where(c => c.Key is not null && c.HasIgnore).ToArray();
        if (contradictory.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{Join(contradictory)}] have both [BinaryKey] and " +
                "[BinaryIgnore] — a contract member needs exactly one of them.");

        var unmarked = candidates.Where(c => c.Key is null && !c.HasIgnore).ToArray();
        if (unmarked.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' is [BinaryContract] — every eligible member needs exactly one of " +
                $"[BinaryKey(n)] or [BinaryIgnore]. Unmarked: [{Join(unmarked)}].");

        var keyed = candidates.Where(c => c.Key is not null).ToArray();

        RejectDelegates(type, keyed);

        var negative = keyed.Where(c => c.Key!.Value < 0).ToArray();
        if (negative.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' has negative [BinaryKey] value(s): " +
                $"{string.Join(", ", negative.Select(c => c.Key!.Value))}.");

        var duplicates = keyed.GroupBy(c => c.Key!.Value).Where(g => g.Count() > 1).ToArray();
        if (duplicates.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' has duplicate [BinaryKey] value(s): " +
                $"{string.Join(", ", duplicates.Select(g => g.Key))}.");

        return [.. keyed.OrderBy(c => c.Key!.Value)];
    }

    private static string Join(IEnumerable<Candidate> candidates) =>
        string.Join(", ", candidates.Select(c => c.Name));

    private static void RejectDelegates(Type type, IEnumerable<Candidate> members)
    {
        var delegates = members
            .Where(c => typeof(Delegate).IsAssignableFrom(c.MemberType))
            .ToArray();

        if (delegates.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{Join(delegates)}] are delegates, which carry behaviour rather " +
                "than data and have no representation on the wire. Mark them [BinaryIgnore] to state " +
                "that they are not part of the serialized state.");
    }

    /// <summary>
    /// Walks the inheritance chain one level at a time, most derived first, so a member declared on a
    /// base class is part of the plan whatever its visibility. Asking the most derived type alone,
    /// as reflection does by default, silently drops a non-public base member that
    /// <see cref="BinaryIncludeAttribute"/> said belonged to the value.
    /// <para>
    /// An override is collected once, at its most derived declaration, so its attributes are the ones
    /// that count. A member that merely hides another with <c>new</c> is a second member, and both
    /// travel; <paramref name="type"/>'s distance from each declaration is what orders them.
    /// </para>
    /// </summary>
    private static IEnumerable<Candidate> Candidates(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var claimed = new HashSet<MethodInfo>();
        int distance = 0;

        for (var level = type; level is not null && level != typeof(object); level = level.BaseType, distance++)
        {
            foreach (var property in level.GetProperties(flags))
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (property.GetIndexParameters().Length != 0) continue;
                if (!claimed.Add(property.GetMethod!.GetBaseDefinition())) continue;

                yield return Candidate.From(
                    distance, property.Name, property.GetMethod!.IsPublic, property, property.PropertyType);
            }

            foreach (var field in level.GetFields(flags))
            {
                if (field.IsInitOnly) continue;
                if (field.GetCustomAttribute<CompilerGeneratedAttribute>() is not null) continue;

                yield return Candidate.From(distance, field.Name, field.IsPublic, field, field.FieldType);
            }
        }
    }

    /// <summary>
    /// One member the plan may include. <c>Distance</c> counts the steps from the concrete type up to
    /// the type that declares the member; it is the tiebreaker that keeps the plan a total order when
    /// two declarations share a name, and nothing else — a base declaration is written before the one
    /// that hides it.
    /// </summary>
    private sealed record Candidate(
        int Distance,
        string Name,
        bool IsPubliclyVisible,
        bool HasIgnore,
        bool HasInclude,
        int Order,
        int? Key,
        MemberInfo Member,
        Type MemberType)
    {
        public static Candidate From(
            int distance,
            string name,
            bool isPubliclyVisible,
            MemberInfo member,
            Type memberType) =>
            new(
                distance,
                name,
                isPubliclyVisible,
                member.GetCustomAttribute<BinaryIgnoreAttribute>() is not null,
                member.GetCustomAttribute<BinaryIncludeAttribute>() is not null,
                member.GetCustomAttribute<BinaryOrderAttribute>()?.Order ?? int.MaxValue,
                member.GetCustomAttribute<BinaryKeyAttribute>()?.Key,
                member,
                memberType);
    }
}
