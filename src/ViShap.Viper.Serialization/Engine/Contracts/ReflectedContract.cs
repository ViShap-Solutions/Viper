using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Engine;

/// <summary>
/// The type contract built by reflection: one typed accessor per member, in the order of the
/// description, each compiled once. A member travels as its own type from the getter to its codec and
/// from its codec to the setter, and a struct owner is assigned in place, so nothing is boxed; the one
/// exception is a type read through a constructor with parameters, whose values are read into an array
/// of objects, so a value-type member of such a type is boxed on read.
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
        ConstructorInfo? constructor,
        ConstructorPlan? plan)
        : base(layout, [.. accessors.Select(accessor => accessor.Description)], canBeConstructed)
    {
        _accessors = accessors;
        _byKey = layout == MemberLayout.Keyed
            ? accessors.ToFrozenDictionary(accessor => accessor.Description.Key!.Value)
            : FrozenDictionary<int, MemberAccessor<T>>.Empty;
        _create = constructor is null
            ? null
            : Expression.Lambda<Func<T>>(Expression.New(constructor)).Compile();

        if (plan is not null)
        {
            var values = new MemberAccessors<object?[]>();
            for (int index = 0; index < accessors.Length; index++)
                accessors[index].AddValueTo(values, index);

            var defaults = plan.Defaults;
            UseConstructor(() => (object?[])defaults.Clone(), values, Construct(plan, accessors));
        }

        var table = new MemberAccessors<T>();
        foreach (var accessor in accessors)
            accessor.AddTo(table);

        UseAccessors(table);
    }

    /// <summary>
    /// Compiles the constructor call over the values read: the constructor with its parameters' values,
    /// then every other member that was read, assigned on the new instance.
    /// </summary>
    private static MemberConstructor<object?[], T> Construct(ConstructorPlan plan, MemberAccessor<T>[] accessors)
    {
        var values = Expression.Parameter(typeof(object?[]).MakeByRefType(), "values");
        var instance = Expression.Variable(typeof(T), "instance");
        var body = new List<Expression>();

        var parameters = plan.Constructor.GetParameters();
        var arguments = new Expression[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
        {
            var slot = Expression.ArrayIndex(values, Expression.Constant(plan.ParameterMembers[index]));
            arguments[index] = Expression.Convert(slot, parameters[index].ParameterType);
        }

        body.Add(Expression.Assign(instance, Expression.New(plan.Constructor, arguments)));

        for (int index = 0; index < accessors.Length; index++)
        {
            if (plan.ParameterMembers.Contains(index))
                continue;

            var slot = Expression.ArrayIndex(values, Expression.Constant(index));
            body.Add(Expression.IfThen(
                Expression.NotEqual(slot, Expression.Constant(ConstructorPlan.Missing)),
                accessors[index].Assign(instance, Expression.Convert(slot, accessors[index].Description.MemberType))));
        }

        body.Add(instance);
        return Expression.Lambda<MemberConstructor<object?[], T>>(Expression.Block([instance], body), values).Compile();
    }

    public override T Create() =>
        _create is not null ? _create() :
        typeof(T).IsValueType ? default! :
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

/// <summary>
/// The constructor a type is read through, which member each of its parameters takes, and the values
/// the members start with: a parameter's C# default, or <see cref="Missing"/> for a member assigned after.
/// </summary>
internal sealed record ConstructorPlan(ConstructorInfo Constructor, int[] ParameterMembers, object?[] Defaults)
{
    /// <summary>The value of a member assigned after construction that the payload did not carry.</summary>
    public static readonly object Missing = new();
}

/// <summary>Reads and writes one member of <typeparamref name="T"/> through a <see cref="MemberWriter"/> or <see cref="MemberReader"/>.</summary>
internal abstract class MemberAccessor<T>(MemberDescription description)
{
    public MemberDescription Description { get; } = description;

    public abstract void WriteTo(ref MemberWriter writer, in T owner);

    public abstract void ReadPositionalFrom(ref MemberReader reader, ref T owner);

    public abstract bool ReadKeyedFrom(ref MemberReader reader, ref T owner);

    /// <summary>Adds this member's getter and setter to the contract's accessor table.</summary>
    public abstract void AddTo(MemberAccessors<T> table);

    /// <summary>Adds this member's place among the values a constructor is called with.</summary>
    public abstract void AddValueTo(MemberAccessors<object?[]> values, int index);

    /// <summary>An expression assigning <paramref name="value"/> to this member of <paramref name="owner"/>.</summary>
    public abstract Expression Assign(Expression owner, Expression value);
}

/// <summary>One member of <typeparamref name="T"/>, of type <typeparamref name="TMember"/>, with its getter and setter compiled once.</summary>
internal sealed class MemberAccessor<T, TMember> : MemberAccessor<T>
{
    private readonly MemberGetter<T, TMember> _get;
    private readonly MemberSetter<T, TMember>? _set;
    private readonly MemberInfo _member;
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

        _member = member;
        _get = Expression.Lambda<MemberGetter<T, TMember>>(access, owner).Compile();
        _set = IsAssignable(member)
            ? Expression.Lambda<MemberSetter<T, TMember>>(Expression.Assign(access, value), owner, value).Compile()
            : null;
    }

    public override void WriteTo(ref MemberWriter writer, in T owner)
    {
        if (_key is { } key)
            writer.Member(key, _get(in owner));
        else
            writer.Member(_get(in owner));
    }

    public override void ReadPositionalFrom(ref MemberReader reader, ref T owner) =>
        _set!(ref owner, reader.Member<TMember>());

    public override bool ReadKeyedFrom(ref MemberReader reader, ref T owner)
    {
        _set!(ref owner, reader.Member<TMember>());
        return true;
    }

    public override void AddTo(MemberAccessors<T> table)
    {
        if (_set is null)
            table.Member(_get);
        else
            table.Member(_get, _set);
    }

    public override void AddValueTo(MemberAccessors<object?[]> values, int index) =>
        values.Member(
            (in object?[] slots) => (TMember)slots[index]!,
            (ref object?[] slots, TMember value) => slots[index] = value);

    public override Expression Assign(Expression owner, Expression value) =>
        Expression.Assign(
            _member is FieldInfo field ? Expression.Field(owner, field) : Expression.Property(owner, (PropertyInfo)_member),
            value);

    /// <summary>Whether the member can be assigned after construction: a property with a setter or an <c>init</c>, a field not readonly.</summary>
    private static bool IsAssignable(MemberInfo member) =>
        member is FieldInfo field ? !field.IsInitOnly : ((PropertyInfo)member).CanWrite;
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

        var (constructor, marked) = Constructors(type);
        if (marked is not null)
            candidates = [.. candidates, .. Matched(type, marked)];

        bool canBeConstructed = (type.IsValueType && marked is null) || constructor is not null;

        var (layout, members) = isContract
            ? (MemberLayout.Keyed, Keyed(type, candidates))
            : (MemberLayout.Positional, Positional(type, candidates));

        var withParameters = marked ?? (canBeConstructed || type.IsAbstract || type.IsValueType ? null : Single(type, members));
        var plan = withParameters is null ? null : Plan(type, withParameters, members);

        var accessors = Array.CreateInstance(typeof(MemberAccessor<>).MakeGenericType(type), members.Length);
        for (int i = 0; i < members.Length; i++)
            accessors.SetValue(Accessor(type, members[i]), i);

        return (ITypeContract)Activator.CreateInstance(
            typeof(ReflectedContract<>).MakeGenericType(type),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [layout, accessors, canBeConstructed, constructor, plan],
            culture: null)!;
    }

    /// <summary>
    /// The constructors a type is read through before its members are known: the one marked
    /// <see cref="BinaryConstructorAttribute"/>, as the parameterless one when it takes no parameter;
    /// otherwise, for a class, a parameterless one of any visibility. An unmarked struct is created as
    /// <see langword="default"/>, and an abstract type is never created.
    /// </summary>
    private static (ConstructorInfo? Parameterless, ConstructorInfo? Marked) Constructors(Type type)
    {
        if (type.IsAbstract)
            return (null, null);

        var all = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var marked = all.Where(c => c.GetCustomAttribute<BinaryConstructorAttribute>() is not null).ToArray();

        if (marked.Length > 1)
            throw new BinaryTypeException(
                $"'{type}' marks {marked.Length} constructors [BinaryConstructor]; at most one constructor of a type carries it.");

        if (marked.Length == 1 && marked[0].GetParameters().Length > 0)
            return (null, marked[0]);

        if (marked.Length == 1)
            return (marked[0], null);

        return (type.IsValueType ? null : all.FirstOrDefault(c => c.GetParameters().Length == 0), null);
    }

    /// <summary>
    /// The single public constructor of a class with no parameterless one, when each of its parameters
    /// names one of the type's members; otherwise <c>null</c>, and the type is written but not read. Only a
    /// marked constructor brings in the members nothing but a constructor sets, so the members of a type,
    /// and its bytes, never depend on its constructors alone.
    /// </summary>
    private static ConstructorInfo? Single(Type type, Candidate[] members)
    {
        var candidates = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (candidates.Length != 1)
            return null;

        return candidates[0].GetParameters().All(parameter => Match(members, parameter) >= 0) ? candidates[0] : null;
    }

    /// <summary>
    /// The members only a constructor sets that a parameter of <paramref name="constructor"/> names, which
    /// the type's members include when it is read through that constructor.
    /// </summary>
    private static IEnumerable<Candidate> Matched(Type type, ConstructorInfo constructor)
    {
        var names = new HashSet<string>(constructor.GetParameters().Select(p => p.Name!), StringComparer.OrdinalIgnoreCase);
        return ConstructorOnly(type).Where(candidate => names.Contains(candidate.Name));
    }

    /// <summary>
    /// The index of the one member <paramref name="parameter"/> names, by name ignoring case and of exactly
    /// its type; or -1 when no member, or more than one, has its name, or the member is of another type.
    /// </summary>
    private static int Match(Candidate[] members, ParameterInfo parameter)
    {
        int found = -1;
        for (int index = 0; index < members.Length; index++)
        {
            if (!string.Equals(members[index].Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (found >= 0)
                return -1;

            found = index;
        }

        return found >= 0 && members[found].MemberType == parameter.ParameterType ? found : -1;
    }

    /// <summary>
    /// Pairs each parameter of <paramref name="constructor"/> with the member of the same name and type,
    /// and records the values the members start with.
    /// </summary>
    private static ConstructorPlan Plan(Type type, ConstructorInfo constructor, Candidate[] members)
    {
        var parameters = constructor.GetParameters();
        var parameterMembers = new int[parameters.Length];
        var defaults = new object?[members.Length];
        Array.Fill(defaults, ConstructorPlan.Missing);

        for (int index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            int member = Match(members, parameter);
            if (member < 0)
                throw new BinaryTypeException(
                    $"Parameter '{parameter.Name}' of the [BinaryConstructor] of '{type}' names no single member of the " +
                    "same name and type. Name it after a member, or mark another constructor.");

            parameterMembers[index] = member;
            defaults[member] = DefaultOf(parameter);
        }

        return new ConstructorPlan(constructor, parameterMembers, defaults);
    }

    /// <summary>The value a parameter receives when a keyed payload leaves its member out: its C# default, or the type's.</summary>
    private static object? DefaultOf(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        object? value = parameter.HasDefaultValue ? parameter.DefaultValue : null;

        if (value is null)
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum ? Enum.ToObject(underlying, value) : value;
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
    /// <summary>
    /// The members a type's ordinary candidates leave out because nothing but a constructor can set them:
    /// properties with a getter and no setter, and readonly fields that are not compiler-generated.
    /// </summary>
    private static IEnumerable<Candidate> ConstructorOnly(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        int distance = 0;
        for (var level = type; level is not null && level != typeof(object); level = level.BaseType, distance++)
        {
            foreach (var property in level.GetProperties(flags))
            {
                if (!property.CanRead || property.CanWrite || property.GetIndexParameters().Length != 0)
                    continue;

                yield return Candidate.From(distance, property.Name, property.GetMethod!.IsPublic, property, property.PropertyType);
            }

            foreach (var field in level.GetFields(flags))
            {
                if (!field.IsInitOnly || field.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
                    continue;

                yield return Candidate.From(distance, field.Name, field.IsPublic, field, field.FieldType);
            }
        }
    }

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
