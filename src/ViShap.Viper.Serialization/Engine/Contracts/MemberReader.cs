namespace ViShap.Viper.Engine;

/// <summary>
/// What a type contract reads its members through: one value per call, and nothing else — no bytes,
/// no counts and no position. Under a positional layout each <see cref="Member{TMember}"/> call reads
/// the next member in plan order; under a keyed layout the engine opens one reader per field on the
/// wire, over exactly that field's bytes, and <see cref="Value{TMember}"/> reads its value once. Every
/// call is checked against the contract's description, and a mismatch is
/// <see cref="BinaryTypeException"/>.
/// </summary>
internal ref struct MemberReader
{
    private WireReader _reader;
    private readonly TypeContract _contract;
    private readonly int _key;
    private readonly int _fieldIndex;
    private int _calls;

    private MemberReader(WireReader reader, TypeContract contract, int key, int fieldIndex)
    {
        _reader = reader;
        _contract = contract;
        _key = key;
        _fieldIndex = fieldIndex;
        _calls = 0;
    }

    /// <summary>Whether the current keyed field's value was read.</summary>
    internal readonly bool ValueRead => _calls > 0;

    /// <summary>The bytes of the current keyed field not yet read.</summary>
    internal readonly long Remaining => _reader.Remaining;

    /// <summary>Opens the members of one positional value over the engine's reader.</summary>
    internal static MemberReader Positional(scoped ref WireReader reader, TypeContract contract) =>
        new(reader, contract, key: -1, fieldIndex: -1);

    /// <summary>Opens one keyed field, over a reader bounded to exactly its bytes.</summary>
    internal static MemberReader Field(WireReader field, TypeContract contract, int key) =>
        new(field, contract, key, contract.IndexOfKey(key));

    /// <summary>Requires every positional member to have been read, then hands the reader back.</summary>
    internal readonly void End(ref WireReader reader)
    {
        if (_calls != _contract.Members.Length)
            throw Contracts.Mismatch(
                _contract,
                _contract.Members[_calls],
                $"it read {_calls} of its {_contract.Members.Length} members");

        reader = _reader;
    }

    /// <summary>Reads the next positional member.</summary>
    public TMember Member<TMember>()
    {
        var members = _contract.Members;

        if (_contract.Layout != MemberLayout.Positional)
            throw Contracts.WrongLayout(_contract, MemberLayout.Positional, write: false);

        if (_calls >= members.Length)
            throw Contracts.TooManyCalls(_contract, typeof(TMember), write: false);

        var member = members[_calls];
        if (typeof(TMember) != member.MemberType)
            throw Contracts.Mismatch(
                _contract, member, $"it read a '{typeof(TMember)}' for a member of type '{member.MemberType}'");

        _calls++;
        _reader.State.Trace?.Label(member.Name);
        return FormatterCache<TMember>.Instance.Read(ref _reader);
    }

    /// <summary>Reads the value of the current keyed field, inside its own reference scope.</summary>
    public TMember Value<TMember>()
    {
        if (_contract.Layout != MemberLayout.Keyed)
            throw Contracts.WrongLayout(_contract, MemberLayout.Keyed, write: false);

        if (_fieldIndex < 0)
            throw new BinaryTypeException(
                $"The contract of '{_contract.Type}' read key {_key}, which none of its members has.");

        var member = _contract.Members[_fieldIndex];

        if (_calls > 0)
            throw Contracts.Mismatch(_contract, member, $"it read key {_key} more than once");

        if (typeof(TMember) != member.MemberType)
            throw Contracts.Mismatch(
                _contract, member, $"it read a '{typeof(TMember)}' for a member of type '{member.MemberType}'");

        _calls = 1;

        if (_reader.State.Graph.Read is { } references)
        {
            using var scope = references.Enter();
            return FormatterCache<TMember>.Instance.Read(ref _reader);
        }

        return FormatterCache<TMember>.Instance.Read(ref _reader);
    }
}

/// <summary>The diagnostics of a type contract whose calls disagree with its own description.</summary>
internal static class Contracts
{
    public static BinaryTypeException Mismatch(TypeContract contract, MemberDescription member, string detail) =>
        new($"The contract of '{contract.Type}' does not match its description at member " +
            $"'{member.Name}': {detail}.");

    public static BinaryTypeException TooManyCalls(TypeContract contract, Type memberType, bool write) =>
        new($"The contract of '{contract.Type}' {(write ? "wrote" : "read")} a '{memberType}' after " +
            $"all {contract.Members.Length} of its members.");

    public static BinaryTypeException WrongLayout(TypeContract contract, MemberLayout called, bool write) =>
        new($"The contract of '{contract.Type}' has a {contract.Layout.ToString().ToLowerInvariant()} " +
            $"layout but {(write ? "wrote" : "read")} a {called.ToString().ToLowerInvariant()} member.");
}
