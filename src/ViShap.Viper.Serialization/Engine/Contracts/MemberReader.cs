namespace ViShap.Viper.Contracts;

/// <summary>
/// What a <see cref="TypeContract{T}"/> reads its members through: one member value per call, and
/// nothing else — no bytes, no counts and no position.
/// </summary>
/// <remarks>
/// <para>
/// Under a positional layout each <see cref="Member{TMember}"/> call reads the next member in plan
/// order. Under a keyed layout the serializer hands out one reader per field on the wire, over exactly
/// that field's bytes, and <see cref="Member{TMember}"/> reads that field's member once. Every call is
/// checked against the contract's description, and a call that disagrees is
/// <see cref="BinaryTypeException"/>.
/// </para>
/// <para>
/// Only the serializer creates one, for one call of <see cref="TypeContract{T}.ReadPositional"/> or
/// <see cref="TypeContract{T}.ReadKeyed"/>, and it is used through the reference it is handed. A copy
/// or a default instance is refused with <see cref="BinaryTypeException"/> as soon as either it or the
/// original is used again.
/// </para>
/// </remarks>
public ref struct MemberReader
{
    private WireReader _reader;
    private readonly ITypeContract _contract;
    private readonly bool _keyed;
    private readonly int _key;
    private readonly int _fieldIndex;
    private int _calls;
    private long _mark;

    private MemberReader(WireReader reader, ITypeContract contract, bool keyed, int key, int fieldIndex)
    {
        _reader = reader;
        _contract = contract;
        _keyed = keyed;
        _key = key;
        _fieldIndex = fieldIndex;
        _calls = 0;
        _mark = reader.State.MemberCalls;
    }

    /// <summary>Whether the current keyed field's member was read.</summary>
    internal readonly bool ValueRead => _calls > 0;

    /// <summary>The bytes of the current keyed field not yet read.</summary>
    internal readonly long Remaining => _reader.Remaining;

    /// <summary>Opens the members of one positional value over the engine's reader.</summary>
    internal static MemberReader Positional(scoped ref WireReader reader, ITypeContract contract) =>
        new(reader, contract, keyed: false, key: -1, fieldIndex: -1);

    /// <summary>Opens one keyed field, over a reader bounded to exactly its bytes.</summary>
    internal static MemberReader Field(WireReader field, ITypeContract contract, int key) =>
        new(field, contract, keyed: true, key, contract.IndexOfKey(key));

    /// <summary>Requires every positional member to have been read, then hands the reader back.</summary>
    internal readonly void End(ref WireReader reader)
    {
        EnsureCurrent();

        if (_calls != _contract.Members.Length)
            throw ContractCalls.Mismatch(
                _contract,
                _contract.Members[_calls],
                $"it read {_calls} of its {_contract.Members.Length} members");

        reader = _reader;
    }

    /// <summary>Requires the keyed field's reader to be the one the contract was handed.</summary>
    internal readonly void EndField() => EnsureCurrent();

    /// <summary>
    /// Reads a member: under a positional layout the next one in plan order; under a keyed layout the
    /// member of the current field, which is a scope of its own for object references.
    /// </summary>
    /// <typeparam name="TMember">The member's type, exactly as the contract describes it.</typeparam>
    /// <returns>The member's value.</returns>
    /// <exception cref="BinaryTypeException">
    /// <typeparamref name="TMember"/> is not the type the description gives the member; under a
    /// positional layout every member was already read; under a keyed layout the field's member was
    /// already read, or no member has the field's key.
    /// </exception>
    public TMember Member<TMember>()
    {
        EnsureCurrent();

        // One frame per member on the recursion path: a nested value is read from here, so a helper
        // between this method and the codec would deepen the stack at every level of the graph.
        var members = _contract.Members;
        MemberDescription member;

        if (_keyed)
        {
            if (_fieldIndex < 0)
                throw new BinaryTypeException(
                    $"The contract of '{_contract.Type}' read key {_key}, which none of its members has.");

            member = members[_fieldIndex];

            if (_calls > 0)
                throw ContractCalls.Mismatch(_contract, member, $"it read key {_key} more than once");
        }
        else
        {
            if (_calls >= members.Length)
                throw ContractCalls.TooManyCalls(_contract, typeof(TMember), write: false);

            member = members[_calls];
        }

        if (typeof(TMember) != member.MemberType)
            throw ContractCalls.Mismatch(
                _contract, member, $"it read a '{typeof(TMember)}' for a member of type '{member.MemberType}'");

        _calls++;
        _reader.State.MemberCalls++;

        TMember value;
        if (!_keyed)
        {
            _reader.State.Trace?.Label(member.Name);
            value = FormatterCache<TMember>.Instance.Read(ref _reader);
        }
        else if (_reader.State.Graph.Read is { } references)
        {
            using var scope = references.Enter();
            value = FormatterCache<TMember>.Instance.Read(ref _reader);
        }
        else
        {
            value = FormatterCache<TMember>.Instance.Read(ref _reader);
        }

        _mark = _reader.State.MemberCalls;
        return value;
    }

    /// <summary>
    /// Refuses a reader that is not the one the serializer handed out for the current call: a default
    /// instance, or one of two copies after the other was used.
    /// </summary>
    private readonly void EnsureCurrent()
    {
        if (_contract is null)
            throw new BinaryTypeException(
                "This MemberReader was not handed out by the serializer; a contract reads its members " +
                "only through the reader it is given.");

        if (_reader.State.MemberCalls != _mark)
            throw new BinaryTypeException(
                $"The contract of '{_contract.Type}' used a copy of its MemberReader. A contract reads its " +
                "members through the reader it is handed, by reference; a copy is refused once either it " +
                "or the original has been used.");
    }
}

/// <summary>The diagnostics of a type contract whose calls disagree with its own description.</summary>
internal static class ContractCalls
{
    public static BinaryTypeException Mismatch(ITypeContract contract, MemberDescription member, string detail) =>
        new($"The contract of '{contract.Type}' does not match its description at member " +
            $"'{member.Name}': {detail}.");

    public static BinaryTypeException TooManyCalls(ITypeContract contract, Type memberType, bool write) =>
        new($"The contract of '{contract.Type}' {(write ? "wrote" : "read")} a '{memberType}' after " +
            $"all {contract.Members.Length} of its members.");

    public static BinaryTypeException WrongLayout(ITypeContract contract, MemberLayout called) =>
        new($"The contract of '{contract.Type}' has a {contract.Layout.ToString().ToLowerInvariant()} " +
            $"layout but wrote a {called.ToString().ToLowerInvariant()} member.");
}
