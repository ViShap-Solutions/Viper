using System.Collections.ObjectModel;

namespace ViShap.Viper.Contracts;

/// <summary>How the members of a member-encoded type are laid out on the wire.</summary>
public enum MemberLayout
{
    /// <summary>
    /// Members follow one another in plan order, with nothing between them: the order is the format.
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
/// contract is handed only a <see cref="MemberWriter"/> or a <see cref="MemberReader"/>, which take
/// and return one member value per call and expose no bytes, no counts and no position.
/// </para>
/// <para>
/// The serializer checks every call against the description given to the constructor — the member
/// each call names, its type, its key and the number of calls — and a call that disagrees with it is
/// a <see cref="BinaryTypeException"/> naming the type and the member, never a distorted payload.
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

    /// <summary>Describes the contract the serializer checks every call against.</summary>
    /// <param name="layout">How the members are laid out on the wire.</param>
    /// <param name="members">
    /// The members in the order the contract writes them: plan order under a positional layout,
    /// ascending key order under a keyed one. The array is copied.
    /// </param>
    /// <param name="canBeConstructed">
    /// Whether <see cref="Create"/> produces an instance to read into; <see langword="false"/> for an
    /// abstract type or an interface, which is read only through a <see cref="BinaryUnionAttribute"/> arm.
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
    /// The members in the order the contract writes them: plan order under a positional layout,
    /// ascending key order under a keyed one.
    /// </summary>
    public IReadOnlyList<MemberDescription> Members { get; }

    /// <summary>Whether <see cref="Create"/> produces an instance to read into.</summary>
    public bool CanBeConstructed { get; }

    Type ITypeContract.Type => typeof(T);

    MemberDescription[] ITypeContract.Members => _members;

    int ITypeContract.IndexOfKey(int key) => Array.BinarySearch(_keys, key);

    /// <summary>
    /// Creates the instance a payload is read into, before any of its members are read: through the
    /// type's parameterless constructor, or <see langword="default"/> for a struct.
    /// </summary>
    /// <returns>The new instance.</returns>
    public abstract T Create();

    /// <summary>
    /// Writes the members of <paramref name="value"/>: under a positional layout one
    /// <see cref="MemberWriter.Member{TMember}(TMember)"/> call per member, in plan order; under a keyed
    /// layout one <see cref="MemberWriter.Member{TMember}(int, TMember)"/> call per member, in ascending
    /// key order.
    /// </summary>
    /// <param name="writer">What the members are written through.</param>
    /// <param name="value">The value whose members are written; never null.</param>
    public abstract void Write(ref MemberWriter writer, in T value);

    /// <summary>
    /// Under a positional layout, reads the members into <paramref name="value"/>: one
    /// <see cref="MemberReader.Member{TMember}"/> call per member, in plan order. Not called under a
    /// keyed layout.
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
