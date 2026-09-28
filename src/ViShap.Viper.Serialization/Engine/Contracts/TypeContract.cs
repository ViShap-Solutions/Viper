namespace ViShap.Viper.Engine;

internal enum MemberLayout
{
    Positional,
    Keyed
}

/// <summary>One member of a type contract: its name, its declared type and, under a keyed layout, its key.</summary>
internal sealed class MemberDescription(string name, Type memberType, int? key)
{
    public string Name { get; } = name;

    public Type MemberType { get; } = memberType;

    public int? Key { get; } = key;
}

/// <summary>
/// The description of how one concrete type is encoded member by member: its layout, its members in
/// plan order — or in ascending key order under a keyed layout — and whether it can be constructed.
/// Reader and writer consult the same description, which is what keeps the two sides from
/// disagreeing about a layout, and the engine checks every call a contract makes against it.
/// </summary>
internal abstract class TypeContract
{
    private readonly int[] _keys;

    protected TypeContract(Type type, MemberLayout layout, MemberDescription[] members, bool canBeConstructed)
    {
        Type = type;
        Layout = layout;
        Members = members;
        CanBeConstructed = canBeConstructed;
        _keys = layout == MemberLayout.Keyed ? [.. members.Select(member => member.Key!.Value)] : [];
    }

    public Type Type { get; }

    public MemberLayout Layout { get; }

    public MemberDescription[] Members { get; }

    public bool CanBeConstructed { get; }

    /// <summary>The position of the member with <paramref name="key"/>, or a negative number when no member has it.</summary>
    public int IndexOfKey(int key) => Array.BinarySearch(_keys, key);

    /// <summary>
    /// Writes the members of <paramref name="value"/>, whose type the declared type did not name — the
    /// polymorphic slot, and the one place the engine boxes.
    /// </summary>
    internal abstract void WriteBoxed(ref MemberWriter writer, object value);

    /// <summary>
    /// Reads a value of this contract's type in a polymorphic slot: creates it — or takes
    /// <paramref name="target"/> when one is being populated — registers it under
    /// <paramref name="referenceId"/>, and reads its members.
    /// </summary>
    internal abstract object ReadBoxed(ref WireReader reader, int referenceId, object? target);
}

/// <summary>
/// The type contract of <typeparamref name="T"/>: member order, member access, construction and the
/// response to a known key — and nothing else. Everything around it belongs to the engine: null and
/// the reference frame, the union tag, depth, budgets and cycles, the keyed field count, keys and
/// field lengths, the field window, the loop over the fields on the wire, skipping unknown keys, and
/// requiring a field to be read exactly. A contract receives only a <see cref="MemberWriter"/> or a
/// <see cref="MemberReader"/>, which expose no bytes, no counts and no position.
/// </summary>
internal abstract class TypeContract<T> : TypeContract
{
    protected TypeContract(MemberLayout layout, MemberDescription[] members, bool canBeConstructed)
        : base(typeof(T), layout, members, canBeConstructed)
    {
    }

    /// <summary>Creates an instance to read into: the parameterless constructor; default for a struct.</summary>
    public abstract T Create();

    /// <summary>
    /// Positional: one <see cref="MemberWriter.Member{TMember}"/> call per member, in plan order.
    /// Keyed: one <see cref="MemberWriter.Field{TMember}"/> call per member, in ascending key order.
    /// </summary>
    public abstract void Write(ref MemberWriter writer, in T value);

    /// <summary>Positional only: one <see cref="MemberReader.Member{TMember}"/> call per member, in plan order.</summary>
    public abstract void Read(ref MemberReader reader, ref T value);

    /// <summary>
    /// Keyed only. Called by the engine for each field on the wire; <see langword="false"/> means the
    /// key is unknown to this contract, and the engine skips the field by its length.
    /// </summary>
    public abstract bool ReadField(ref MemberReader reader, int key, ref T value);

    internal sealed override void WriteBoxed(ref MemberWriter writer, object value) =>
        Write(ref writer, (T)value);

    internal sealed override object ReadBoxed(ref WireReader reader, int referenceId, object? target) =>
        ObjectMembers.ReadBoxed(ref reader, this, referenceId, target);
}
