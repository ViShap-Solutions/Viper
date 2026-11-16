namespace ViShap.Viper.Contracts;

/// <summary>Returns the value of one member of <paramref name="owner"/>.</summary>
/// <typeparam name="T">The type that declares the member.</typeparam>
/// <typeparam name="TMember">The member's type.</typeparam>
/// <param name="owner">The instance the member is read from.</param>
/// <returns>The member's value.</returns>
public delegate TMember MemberGetter<T, out TMember>(in T owner);

/// <summary>Assigns one member of <paramref name="owner"/>.</summary>
/// <typeparam name="T">The type that declares the member.</typeparam>
/// <typeparam name="TMember">The member's type.</typeparam>
/// <param name="owner">The instance the member is assigned on; a struct is assigned in place.</param>
/// <param name="value">The value read for the member.</param>
public delegate void MemberSetter<T, in TMember>(ref T owner, TMember value);

/// <summary>Creates an instance of <typeparamref name="T"/> from the values read for its members.</summary>
/// <typeparam name="TValues">What the values were read into.</typeparam>
/// <typeparam name="T">The type constructed.</typeparam>
/// <param name="values">The values read; a member absent from a keyed payload keeps the value it started with.</param>
/// <returns>The new instance.</returns>
public delegate T MemberConstructor<TValues, out T>(ref TValues values);

/// <summary>
/// The members of a type contract as a table of typed accessors, which the serializer walks itself to
/// write and read the members. A contract builds one entry per member, in the order of its description,
/// and hands the table over with <see cref="TypeContract{T}.UseAccessors"/>.
/// </summary>
/// <typeparam name="T">The type the contract describes.</typeparam>
/// <example>
/// <code>
/// UseAccessors(new MemberAccessors&lt;Order&gt;()
///     .Member(static (in Order o) =&gt; o.Id, static (ref Order o, int v) =&gt; o.Id = v)
///     .Member(static (in Order o) =&gt; o.Note, static (ref Order o, string? v) =&gt; o.Note = v));
/// </code>
/// </example>
public sealed class MemberAccessors<T>
{
    private readonly List<Accessor<T>> _entries = [];

    /// <summary>Adds the next member: how to read its value from an instance and how to assign it.</summary>
    /// <typeparam name="TMember">The member's type, exactly as the contract's description gives it.</typeparam>
    /// <param name="get">Returns the member's value.</param>
    /// <param name="set">Assigns the member's value.</param>
    /// <returns>This table, to add the next member to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="get"/> or <paramref name="set"/> is null.</exception>
    public MemberAccessors<T> Member<TMember>(MemberGetter<T, TMember> get, MemberSetter<T, TMember> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        _entries.Add(Accessor<T>.For(get, set));
        return this;
    }

    /// <summary>
    /// Adds the next member as one that is written but never assigned on read: a member a constructor
    /// sets, of a contract that reads through <see cref="TypeContract{T}.UseConstructor"/>.
    /// </summary>
    /// <typeparam name="TMember">The member's type, exactly as the contract's description gives it.</typeparam>
    /// <param name="get">Returns the member's value.</param>
    /// <returns>This table, to add the next member to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="get"/> is null.</exception>
    public MemberAccessors<T> Member<TMember>(MemberGetter<T, TMember> get)
    {
        ArgumentNullException.ThrowIfNull(get);

        _entries.Add(Accessor<T>.For(get, set: null));
        return this;
    }

    internal Accessor<T>[] ToArray() => [.. _entries];
}
