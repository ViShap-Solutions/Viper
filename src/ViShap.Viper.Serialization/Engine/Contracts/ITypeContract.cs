namespace ViShap.Viper.Engine;

/// <summary>
/// What the engine needs of a type contract whatever its type: its description, which every call
/// is checked against, and the polymorphic slot's untyped entries.
/// </summary>
internal interface ITypeContract
{
    Type Type { get; }

    MemberLayout Layout { get; }

    /// <summary>The members, in the order the contract writes them.</summary>
    MemberDescription[] Members { get; }

    bool CanBeConstructed { get; }

    /// <summary>The position of the member with <paramref name="key"/>, or a negative number when no member has it.</summary>
    int IndexOfKey(int key);

    /// <summary>
    /// Writes the members of <paramref name="value"/>, whose type the declared type did not name — the
    /// polymorphic slot, and the one place the engine boxes.
    /// </summary>
    void WriteBoxed(ref MemberWriter writer, object value);

    /// <summary>
    /// Reads a value of this contract's type in a polymorphic slot: creates it — or takes
    /// <paramref name="target"/> when one is being populated — registers it under
    /// <paramref name="referenceId"/>, and reads its members.
    /// </summary>
    object ReadBoxed(ref WireReader reader, int referenceId, object? target);
}
