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

    internal Accessor<T>[] ToArray() => [.. _entries];
}
