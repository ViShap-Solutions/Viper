using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Engine;

internal enum MemberLayout
{
    Positional,
    Keyed
}

internal sealed class MemberBinding
{
    public required string Name { get; init; }
    public required Type MemberType { get; init; }
    public required Func<object, object?> Get { get; init; }
    public required Action<object, object?> Set { get; init; }
    public int? Key { get; init; }
}

/// <summary>
/// The single materialized description of how one concrete type is encoded: its members, their
/// order or keys, and the layout mode. Reader and writer consult the same object, which is what
/// keeps the two sides from disagreeing about a layout.
/// </summary>
internal sealed class TypeContract
{
    public required Type Type { get; init; }
    public required MemberLayout Layout { get; init; }
    public required MemberBinding[] Members { get; init; }
    public IReadOnlyDictionary<int, MemberBinding>? MembersByKey { get; init; }
    public required bool CanBeConstructed { get; init; }
}

/// <summary>Tag ↔ type map declared by <see cref="BinaryUnionAttribute"/> on a base type.</summary>
internal sealed class UnionMap(
    IReadOnlyDictionary<Type, byte> tagByType,
    IReadOnlyDictionary<byte, Type> typeByTag)
{
    public bool TryGetTag(Type runtimeType, out byte tag) => tagByType.TryGetValue(runtimeType, out tag);

    public bool TryGetType(byte tag, out Type? runtimeType)
    {
        bool found = typeByTag.TryGetValue(tag, out var type);
        runtimeType = type;
        return found;
    }
}

internal static class TypeContractCache
{
    private const int MaxUnionTag = 255;

    private static readonly ConcurrentDictionary<Type, TypeContract> Contracts = new();
    private static readonly ConcurrentDictionary<Type, UnionMap?> Unions = new();

    public static TypeContract Get(Type type) => Contracts.GetOrAdd(type, Build);

    public static UnionMap? GetUnion(Type declaredType) => Unions.GetOrAdd(declaredType, BuildUnion);

    public static int CachedTypeCount => Contracts.Count;

    private static UnionMap? BuildUnion(Type declaredType)
    {
        var attributes = declaredType
            .GetCustomAttributes(typeof(BinaryUnionAttribute), inherit: false)
            .Cast<BinaryUnionAttribute>()
            .ToArray();

        if (attributes.Length == 0)
            return null;

        var duplicateTags = attributes.GroupBy(a => a.Tag).Where(g => g.Count() > 1).ToArray();
        if (duplicateTags.Length > 0)
            throw new BinaryTypeException(
                $"'{declaredType}' has duplicate [BinaryUnion] tag(s): " +
                $"{string.Join(", ", duplicateTags.Select(g => g.Key))}.");

        foreach (var attribute in attributes)
        {
            if (attribute.Tag is < 0 or > MaxUnionTag)
                throw new BinaryTypeException(
                    $"[BinaryUnion] tag {attribute.Tag} on '{declaredType}' must fit in a byte " +
                    $"(0-{MaxUnionTag}).");

            if (!declaredType.IsAssignableFrom(attribute.DerivedType))
                throw new BinaryTypeException(
                    $"Known type '{attribute.DerivedType}' is not assignable to '{declaredType}'.");
        }

        return new UnionMap(
            attributes.ToDictionary(a => a.DerivedType, a => (byte)a.Tag),
            attributes.ToDictionary(a => (byte)a.Tag, a => a.DerivedType));
    }

    private static TypeContract Build(Type type)
    {
        var candidates = Candidates(type).ToArray();
        bool isContract = type.GetCustomAttribute<BinaryContractAttribute>() is not null;
 
        bool canBeConstructed =
            type.IsValueType ||
            (!type.IsAbstract &&
             type.GetConstructor(
                 BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                 Type.EmptyTypes) is not null);

        return isContract
            ? BuildKeyed(type, candidates, canBeConstructed)
            : BuildPositional(type, candidates, canBeConstructed);
    }

    private static TypeContract BuildPositional(
        Type type,
        Candidate[] candidates,
        bool canBeConstructed)
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

        return new TypeContract
        {
            Type = type,
            Layout = MemberLayout.Positional,
            // Order, then ordinal name, then the declaring level — a total order, so the plan never
            // depends on the order reflection happened to return members in.
            Members = eligible
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ThenByDescending(c => c.Distance)
                .Select(c => c.Binding)
                .ToArray(),
            MembersByKey = null,
            CanBeConstructed = canBeConstructed
        };
    }

    private static TypeContract BuildKeyed(
        Type type,
        Candidate[] candidates,
        bool canBeConstructed)
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

        var ordered = keyed.OrderBy(c => c.Key!.Value).ToArray();

        return new TypeContract
        {
            Type = type,
            Layout = MemberLayout.Keyed,
            Members = ordered.Select(c => c.Binding).ToArray(),
            MembersByKey = ordered.ToDictionary(c => c.Key!.Value, c => c.Binding),
            CanBeConstructed = canBeConstructed
        };
    }

    private static string Join(IEnumerable<Candidate> candidates) =>
        string.Join(", ", candidates.Select(c => c.Name));

    private static void RejectDelegates(Type type, IEnumerable<Candidate> members)
    {
        var delegates = members
            .Where(c => typeof(Delegate).IsAssignableFrom(c.Binding.MemberType))
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
                    distance,
                    property.Name,
                    property.GetMethod!.IsPublic,
                    property,
                    property.PropertyType,
                    BuildGetter(property),
                    BuildSetter(property));
            }

            foreach (var field in level.GetFields(flags))
            {
                if (field.IsInitOnly) continue;
                if (field.GetCustomAttribute<CompilerGeneratedAttribute>() is not null) continue;

                yield return Candidate.From(
                    distance,
                    field.Name,
                    field.IsPublic,
                    field,
                    field.FieldType,
                    BuildGetter(field),
                    BuildSetter(field));
            }
        }
    }

    private static Func<object, object?> BuildGetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var typed = Typed(instance, property.DeclaringType!);
        return Expression
            .Lambda<Func<object, object?>>(
                Expression.Convert(Expression.Property(typed, property), typeof(object)), instance)
            .Compile();
    }

    private static Action<object, object?> BuildSetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");
        var typed = Typed(instance, property.DeclaringType!);
        return Expression
            .Lambda<Action<object, object?>>(
                Expression.Assign(
                    Expression.Property(typed, property),
                    Expression.Convert(value, property.PropertyType)),
                instance, value)
            .Compile();
    }

    private static Func<object, object?> BuildGetter(FieldInfo field)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var typed = Typed(instance, field.DeclaringType!);
        return Expression
            .Lambda<Func<object, object?>>(
                Expression.Convert(Expression.Field(typed, field), typeof(object)), instance)
            .Compile();
    }

    private static Action<object, object?> BuildSetter(FieldInfo field)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");
        var typed = Typed(instance, field.DeclaringType!);
        return Expression
            .Lambda<Action<object, object?>>(
                Expression.Assign(
                    Expression.Field(typed, field),
                    Expression.Convert(value, field.FieldType)),
                instance, value)
            .Compile();
    }

    private static Expression Typed(ParameterExpression instance, Type declaringType) =>
        declaringType.IsValueType
            ? Expression.Unbox(instance, declaringType)
            : Expression.Convert(instance, declaringType);

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
        MemberBinding Binding)
    {
        public static Candidate From(
            int distance,
            string name,
            bool isPubliclyVisible,
            MemberInfo member,
            Type memberType,
            Func<object, object?> get,
            Action<object, object?> set) =>
            new(
                distance,
                name,
                isPubliclyVisible,
                member.GetCustomAttribute<BinaryIgnoreAttribute>() is not null,
                member.GetCustomAttribute<BinaryIncludeAttribute>() is not null,
                member.GetCustomAttribute<BinaryOrderAttribute>()?.Order ?? int.MaxValue,
                member.GetCustomAttribute<BinaryKeyAttribute>()?.Key,
                new MemberBinding
                {
                    Name = name,
                    MemberType = memberType,
                    Get = get,
                    Set = set,
                    Key = member.GetCustomAttribute<BinaryKeyAttribute>()?.Key
                });
    }
}
