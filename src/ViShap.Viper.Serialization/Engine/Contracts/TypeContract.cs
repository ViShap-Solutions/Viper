using System.Collections.ObjectModel;

namespace ViShap.Viper.Contracts;

/// <summary>How the members of a member-encoded type are laid out on the wire.</summary>
public enum MemberLayout
{
    /// <summary>
    /// Members follow one another in the order of the description, with nothing between them: the
    /// order is the format. Reflection orders them by <see cref="BinaryOrderAttribute"/>, then by name.
    /// </summary>
    Positional,

    /// <summary>
    /// Each member is a field identified by its key, written in ascending key order; a reader skips
    /// the keys it does not know, so a keyed type can evolve.
    /// </summary>
    Keyed
}

/// <summary>
/// One member of a <see cref="TypeContract{T}"/>: its name, its declared type and, under a keyed
/// layout, its key.
/// </summary>
/// <remarks>
/// The serializer checks every call a contract makes against these descriptions: the member type is
/// the exact type the contract writes and reads the member as, and the key is the one it writes the
/// member under.
/// </remarks>
public sealed class MemberDescription
{
    /// <summary>Describes one member.</summary>
    /// <param name="name">The member's name, used in diagnostics.</param>
    /// <param name="memberType">The exact type the member is written and read as.</param>
    /// <param name="key">
    /// The member's key under a keyed layout, a non-negative number; <see langword="null"/> under a
    /// positional layout.
    /// </param>
    /// <exception cref="BinaryConfigurationException">
    /// <paramref name="name"/> is null or empty, <paramref name="memberType"/> is null, or
    /// <paramref name="key"/> is negative.
    /// </exception>
    public MemberDescription(string name, Type memberType, int? key)
    {
        if (string.IsNullOrEmpty(name))
            throw new BinaryConfigurationException("A member description needs a name.");

        if (memberType is null)
            throw new BinaryConfigurationException($"The description of member '{name}' needs a member type.");

        if (key < 0)
            throw new BinaryConfigurationException(
                $"The description of member '{name}' has key {key}; a key is a non-negative number.");

        Name = name;
        MemberType = memberType;
        Key = key;
    }

    /// <summary>The member's name.</summary>
    public string Name { get; }

    /// <summary>The exact type the member is written and read as.</summary>
    public Type MemberType { get; }

    /// <summary>The member's key under a keyed layout; <see langword="null"/> under a positional one.</summary>
    public int? Key { get; }
}

/// <summary>
/// How a member-encoded type is written and read member by member: its member order, how each member
/// is reached, how an instance is created, and which keys it knows. The serializer builds one by
/// reflection for every member-encoded type; a contract added to a <see cref="BinarySerializerContext"/>
/// takes its place.
/// </summary>
/// <remarks>
/// <para>
/// Everything around the members belongs to the serializer: null and object references, the
/// <see cref="BinaryUnionAttribute"/> tag, depth, limits and cycles, the keyed field count, each
/// field's key and length, skipping unknown keys, and requiring a field to be read exactly. A
/// contract reaches the members in one of two ways, and neither exposes a byte, a count or a position:
/// it implements <see cref="Write"/>, <see cref="ReadPositional"/> and <see cref="ReadKeyed"/>, which
/// are handed a <see cref="MemberWriter"/> or a <see cref="MemberReader"/> that take and return one
/// member value per call; or it hands the serializer a <see cref="MemberAccessors{T}"/> table with
/// <see cref="UseAccessors"/>, one typed getter and setter per member, which the serializer walks
/// itself. A type read through a constructor with parameters adds <see cref="UseConstructor"/>.
/// </para>
/// <para>
/// The serializer holds the contract to the description given to the constructor. It checks every
/// call of a <see cref="MemberWriter"/> or a <see cref="MemberReader"/> — the member each call names,
/// its type, its key and the number of calls — and a call that disagrees with it is a
/// <see cref="BinaryTypeException"/> naming the type and the member, never a distorted payload. It
/// checks an accessor table once, when the contract hands it over: one entry per member, each of the
/// member's exact type.
/// </para>
/// <para>
/// A contract that describes a type the way the reflection-built one does — the same layout, members,
/// types and keys, in the same order — writes the same bytes and reads the same payloads.
/// </para>
/// <example>
/// <code>
/// sealed class PersonContract() : TypeContract&lt;Person&gt;(
///     MemberLayout.Keyed,
///     [new("Name", typeof(string), 1), new("Age", typeof(int), 2)],
///     canBeConstructed: true)
/// {
///     public override Person Create() =&gt; new();
///
///     public override void Write(ref MemberWriter writer, in Person value)
///     {
///         writer.Member(1, value.Name);
///         writer.Member(2, value.Age);
///     }
///
///     public override void ReadPositional(ref MemberReader reader, ref Person value) =&gt;
///         throw new NotSupportedException();
///
///     public override bool ReadKeyed(ref MemberReader reader, int key, ref Person value)
///     {
///         switch (key)
///         {
///             case 1: value.Name = reader.Member&lt;string&gt;(); return true;
///             case 2: value.Age = reader.Member&lt;int&gt;(); return true;
///             default: return false;
///         }
///     }
/// }
/// </code>
/// </example>
/// </remarks>
/// <typeparam name="T">The member-encoded type the contract describes.</typeparam>
public abstract class TypeContract<T> : ITypeContract
{
    private readonly MemberDescription[] _members;
    private readonly int[] _keys;
    private Accessor<T>[]? _accessors;
    private Construction<T>? _construction;
    private bool _inUse;

    /// <summary>Describes the contract the serializer checks every call against.</summary>
    /// <param name="layout">How the members are laid out on the wire.</param>
    /// <param name="members">
    /// The members in the order the contract writes them: the position order of the type under a
    /// positional layout, ascending key order under a keyed one. The array is copied.
    /// </param>
    /// <param name="canBeConstructed">
    /// Whether <see cref="Create"/> produces an instance to read into; <see langword="false"/> for an
    /// abstract type or an interface, which is read only through a <see cref="BinaryUnionAttribute"/> arm,
    /// and for a type read through a constructor with parameters (<see cref="UseConstructor"/>).
    /// </param>
    /// <exception cref="BinaryConfigurationException">
    /// <paramref name="layout"/> is not a defined layout; <paramref name="members"/> is null or holds a
    /// null; a positional member has a key, or a keyed member has none; or the keys are not strictly
    /// ascending.
    /// </exception>
    protected TypeContract(MemberLayout layout, MemberDescription[] members, bool canBeConstructed)
    {
        if (layout is not (MemberLayout.Positional or MemberLayout.Keyed))
            throw new BinaryConfigurationException(
                $"The contract of '{typeof(T)}' names layout {(int)layout}, which is not a member layout.");

        if (members is null)
            throw new BinaryConfigurationException($"The contract of '{typeof(T)}' needs its member descriptions.");

        _members = [.. members];
        _keys = new int[layout == MemberLayout.Keyed ? _members.Length : 0];

        for (int i = 0; i < _members.Length; i++)
        {
            var member = _members[i] ?? throw new BinaryConfigurationException(
                $"The contract of '{typeof(T)}' describes a null member at position {i}.");

            if (layout == MemberLayout.Positional)
            {
                if (member.Key is not null)
                    throw new BinaryConfigurationException(
                        $"The contract of '{typeof(T)}' has a positional layout, but member " +
                        $"'{member.Name}' has key {member.Key}.");

                continue;
            }

            int key = member.Key ?? throw new BinaryConfigurationException(
                $"The contract of '{typeof(T)}' has a keyed layout, but member '{member.Name}' has no key.");

            if (i > 0 && key <= _keys[i - 1])
                throw new BinaryConfigurationException(
                    $"The contract of '{typeof(T)}' lists key {key} of member '{member.Name}' after key " +
                    $"{_keys[i - 1]}; keys are described once each, in ascending order.");

            _keys[i] = key;
        }

        Layout = layout;
        Members = new ReadOnlyCollection<MemberDescription>(_members);
        CanBeConstructed = canBeConstructed;
    }

    /// <summary>How the members are laid out on the wire.</summary>
    public MemberLayout Layout { get; }

    /// <summary>
    /// The members in the order the contract writes them: the position order of the type under a
    /// positional layout, ascending key order under a keyed one.
    /// </summary>
    public IReadOnlyList<MemberDescription> Members { get; }

    /// <summary>Whether <see cref="Create"/> produces an instance to read into.</summary>
    public bool CanBeConstructed { get; }

    Type ITypeContract.Type => typeof(T);

    bool ITypeContract.Constructs => _construction is not null;

    MemberDescription[] ITypeContract.Members => _members;

    int ITypeContract.IndexOfKey(int key) => Array.BinarySearch(_keys, key);

    /// <summary>
    /// Hands the serializer the members as a table of typed accessors, which it then walks itself to write
    /// and read them. <see cref="Write"/> is still called when a value is written as an arm of a
    /// <see cref="BinaryUnionAttribute"/> base, and <see cref="ReadPositional"/> and
    /// <see cref="ReadKeyed"/> when a read is traced for diagnostics, so they must agree with the table.
    /// Called once, from the contract's constructor.
    /// </summary>
    /// <param name="accessors">One entry per member, in the order of the description.</param>
    /// <exception cref="ArgumentNullException"><paramref name="accessors"/> is null.</exception>
    /// <exception cref="BinaryConfigurationException">
    /// The table has another number of entries than the description has members, an entry is for another
    /// type than its member's, a table was already handed over, or the contract is already in use.
    /// </exception>
    protected void UseAccessors(MemberAccessors<T> accessors)
    {
        ArgumentNullException.ThrowIfNull(accessors);

        if (_inUse)
            throw new BinaryConfigurationException(
                $"The contract of '{typeof(T)}' is already in use; its accessors are handed over from its constructor.");

        if (_accessors is not null)
            throw new BinaryConfigurationException($"The contract of '{typeof(T)}' already handed over its accessors.");

        var entries = accessors.ToArray();
        RequireDescribed(entries, "accessor table");

        for (int index = 0; index < entries.Length; index++)
        {
            if (!entries[index].Assigns && _construction is null)
                throw new BinaryConfigurationException(
                    $"The accessor of member '{_members[index].Name}' of '{typeof(T)}' has no setter, which only a " +
                    "contract that reads through a constructor can leave out; call UseConstructor first.");
        }

        _accessors = entries;
    }

    /// <summary>
    /// Has the serializer read the type through a constructor: the members are read into
    /// <typeparamref name="TValues"/>, which <paramref name="start"/> creates with the value each member
    /// keeps when a keyed payload leaves it out, and the instance is then made from them by
    /// <paramref name="construct"/>. Called once, from the contract's constructor, before
    /// <see cref="UseAccessors"/>.
    /// </summary>
    /// <typeparam name="TValues">What the values are read into: one place per member, in the order of the description.</typeparam>
    /// <param name="start">Creates the values before any member is read.</param>
    /// <param name="values">One entry per member, in the order of the description, each assigning its place in the values.</param>
    /// <param name="construct">Creates the instance from the values: calls the constructor and assigns the members it does not take.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="BinaryConfigurationException">
    /// The table has another number of entries than the description has members, an entry is for another
    /// type than its member's or assigns nothing, a constructor was already handed over, or the contract is
    /// already in use.
    /// </exception>
    protected void UseConstructor<TValues>(Func<TValues> start, MemberAccessors<TValues> values, MemberConstructor<TValues, T> construct)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(construct);

        if (_inUse)
            throw new BinaryConfigurationException(
                $"The contract of '{typeof(T)}' is already in use; its constructor is handed over from its own constructor.");

        if (_construction is not null)
            throw new BinaryConfigurationException($"The contract of '{typeof(T)}' already handed over its constructor.");

        var entries = values.ToArray();
        RequireDescribed(entries, "values table");

        for (int index = 0; index < entries.Length; index++)
        {
            if (!entries[index].Assigns)
                throw new BinaryConfigurationException(
                    $"The values entry of member '{_members[index].Name}' of '{typeof(T)}' has no setter; every value read is kept.");
        }

        _construction = new Construction<T, TValues>(start, entries, construct);
    }

    /// <summary>Holds a table to the description: one entry per member, each for the member's exact type.</summary>
    private void RequireDescribed<TOwner>(Accessor<TOwner>[] entries, string table)
    {
        if (entries.Length != _members.Length)
            throw new BinaryConfigurationException(
                $"The contract of '{typeof(T)}' describes {_members.Length} members but its {table} has " +
                $"{entries.Length} entries.");

        for (int index = 0; index < entries.Length; index++)
        {
            if (entries[index].MemberType != _members[index].MemberType)
                throw new BinaryConfigurationException(
                    $"The {table} entry of member '{_members[index].Name}' of '{typeof(T)}' is for " +
                    $"'{entries[index].MemberType}', but the member is a '{_members[index].MemberType}'.");
        }
    }

    /// <summary>How the contract constructs an instance from the values read, or <c>null</c>.</summary>
    internal Construction<T>? Construction => _construction;

    /// <summary>The accessor table the contract handed over, or <c>null</c>.</summary>
    internal Accessor<T>[]? Accessors => _accessors;

    /// <summary>Marks the contract as in use, after which its accessors can no longer be handed over.</summary>
    internal void Seal() => _inUse = true;

    /// <summary>
    /// Creates the instance a payload is read into, before any of its members are read: through the
    /// type's parameterless constructor, or <see langword="default"/> for a struct. Not called when
    /// <see cref="CanBeConstructed"/> is <see langword="false"/>, or for a contract that reads through
    /// <see cref="UseConstructor"/>.
    /// </summary>
    /// <returns>The new instance.</returns>
    public abstract T Create();

    /// <summary>
    /// Writes the members of <paramref name="value"/>: under a positional layout one
    /// <see cref="MemberWriter.Member{TMember}(TMember)"/> call per member, in the order of the
    /// description; under a keyed layout one <see cref="MemberWriter.Member{TMember}(int, TMember)"/> call
    /// per member, in ascending key order.
    /// </summary>
    /// <param name="writer">What the members are written through.</param>
    /// <param name="value">The value whose members are written; never null.</param>
    public abstract void Write(ref MemberWriter writer, in T value);

    /// <summary>
    /// Under a positional layout, reads the members into <paramref name="value"/>: one
    /// <see cref="MemberReader.Member{TMember}"/> call per member, in the order of the description. Not
    /// called under a keyed layout.
    /// </summary>
    /// <param name="reader">What the members are read through.</param>
    /// <param name="value">The instance being read into; a struct is assigned in place.</param>
    public abstract void ReadPositional(ref MemberReader reader, ref T value);

    /// <summary>
    /// Under a keyed layout, reads one field of the payload: called once for each field, with that
    /// field's key. A known key reads its member with one <see cref="MemberReader.Member{TMember}"/>
    /// call and returns <see langword="true"/>; an unknown key reads nothing and returns
    /// <see langword="false"/>, and the field is skipped. Not called under a positional layout.
    /// </summary>
    /// <param name="reader">What the field's member is read through.</param>
    /// <param name="key">The key of the field on the wire.</param>
    /// <param name="value">The instance being read into; a struct is assigned in place.</param>
    /// <returns>Whether the contract knows <paramref name="key"/>.</returns>
    public abstract bool ReadKeyed(ref MemberReader reader, int key, ref T value);

    void ITypeContract.WriteBoxed(ref MemberWriter writer, object value) => Write(ref writer, (T)value);

    object ITypeContract.ReadBoxed(ref WireReader reader, int referenceId, object? target) =>
        ObjectMembers.ReadBoxed(ref reader, this, referenceId, target);
}
