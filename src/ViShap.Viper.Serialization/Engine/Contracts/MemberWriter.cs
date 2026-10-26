namespace ViShap.Viper.Contracts;

/// <summary>
/// What a <see cref="TypeContract{T}"/> writes its members through: one member value per call, and
/// nothing else — no bytes, no counts and no position.
/// </summary>
/// <remarks>
/// <para>
/// The serializer frames each value, keys and measures each keyed field, and checks every call against
/// the contract's description: the member it names, its type, its key and the number of calls. A call
/// that disagrees is <see cref="BinaryTypeException"/>, never a distorted payload.
/// </para>
/// <para>
/// Only the serializer creates one, for one call of <see cref="TypeContract{T}.Write"/>, and it is
/// used through the reference it is handed. A copy — passing it to a helper by value, or assigning it
/// to a local — or a default instance is refused with <see cref="BinaryTypeException"/> as soon as
/// either it or the original is used again.
/// </para>
/// </remarks>
public ref struct MemberWriter
{
    private WireWriter _writer;
    private readonly ITypeContract _contract;
    private int _calls;
    private long _mark;

    private MemberWriter(WireWriter writer, ITypeContract contract)
    {
        _writer = writer;
        _contract = contract;
        _calls = 0;
        _mark = writer.State.MemberCalls;
    }

    /// <summary>
    /// Opens the members of one value. Under a keyed layout the field count is checked against
    /// <c>MaxKeyedFields</c>, charged to the keyed field budget and written first — one higher when
    /// <paramref name="nullFolded"/>, because it then carries the value's null.
    /// </summary>
    internal static MemberWriter Begin(scoped ref WireWriter writer, ITypeContract contract, bool nullFolded)
    {
        if (contract.Layout == MemberLayout.Keyed)
        {
            int count = contract.Members.Length;
            ref var state = ref writer.State;
            if (count > state.Limits.MaxKeyedFields)
                throw new BinaryLimitException(
                    $"Keyed field count {count} exceeds the configured maximum of " +
                    $"{state.Limits.MaxKeyedFields} (MaxKeyedFields).");

            state.Budget.ConsumeKeyedFields(count);
            writer.WriteFolded(count, nullFolded, "Keyed field count");
        }

        return new MemberWriter(writer, contract);
    }

    /// <summary>Requires every member to have been written, then hands the writer back.</summary>
    internal readonly void End(ref WireWriter writer)
    {
        EnsureCurrent();

        if (_calls != _contract.Members.Length)
            throw ContractCalls.Mismatch(
                _contract,
                _contract.Members[_calls],
                $"it wrote {_calls} of its {_contract.Members.Length} members");

        writer = _writer;
    }

    /// <summary>Writes the next member of a positional layout, in plan order.</summary>
    /// <param name="value">The member's value.</param>
    /// <typeparam name="TMember">The member's type, exactly as the contract describes it.</typeparam>
    /// <exception cref="BinaryTypeException">
    /// The layout is keyed; every member was already written; or <typeparamref name="TMember"/> is not the
    /// type the description gives the next member.
    /// </exception>
    public void Member<TMember>(TMember value)
    {
        Next<TMember>(MemberLayout.Positional, key: null);
        FormatterCache<TMember>.Instance.Write(ref _writer, value);
        _mark = _writer.State.MemberCalls;
    }

    /// <summary>
    /// Writes the next member of a keyed layout, in ascending key order, as one field: its key, its
    /// length and its value, which is a scope of its own for object references.
    /// </summary>
    /// <param name="key">The member's key, as the description gives it.</param>
    /// <param name="value">The member's value.</param>
    /// <typeparam name="TMember">The member's type, exactly as the contract describes it.</typeparam>
    /// <exception cref="BinaryTypeException">
    /// The layout is positional; every member was already written; or <paramref name="key"/> or
    /// <typeparamref name="TMember"/> is not what the description gives the next member.
    /// </exception>
    public void Member<TMember>(int key, TMember value)
    {
        var member = Next<TMember>(MemberLayout.Keyed, key);

        _writer.Write7BitEncodedInt(key);

        long lengthPosition = _writer.Position;
        _writer.WriteInt32(0);
        long payloadStart = _writer.Position;

        if (_writer.State.Graph.Written is { } references)
        {
            using var scope = references.Enter();
            FormatterCache<TMember>.Instance.Write(ref _writer, value);
        }
        else
        {
            FormatterCache<TMember>.Instance.Write(ref _writer, value);
        }

        long payloadLength = _writer.Position - payloadStart;

        if (payloadLength > int.MaxValue)
            throw new BinaryFormatException(
                $"Keyed member '{member.Name}' payload length {payloadLength} exceeds Int32 range.");

        if (!_writer.State.Phases.AdmitsPayload(payloadLength))
            _writer.State.Phases.CheckPayload(payloadLength, $"Keyed member '{member.Name}' payload length");

        _writer.PatchInt32(lengthPosition, (int)payloadLength);
        _mark = _writer.State.MemberCalls;
    }

    private MemberDescription Next<TMember>(MemberLayout layout, int? key)
    {
        EnsureCurrent();

        var members = _contract.Members;

        if (_contract.Layout != layout)
            throw ContractCalls.WrongLayout(_contract, layout);

        if (_calls >= members.Length)
            throw ContractCalls.TooManyCalls(_contract, typeof(TMember), write: true);

        var member = members[_calls];

        if (key is { } written && member.Key != written)
            throw ContractCalls.Mismatch(
                _contract, member, $"it wrote key {written} where key {member.Key} comes next");

        if (typeof(TMember) != member.MemberType)
            throw ContractCalls.Mismatch(
                _contract, member, $"it wrote a '{typeof(TMember)}' for a member of type '{member.MemberType}'");

        _calls++;
        _mark = ++_writer.State.MemberCalls;
        return member;
    }

    /// <summary>
    /// Refuses a writer that is not the one the serializer handed out for the current call: a default
    /// instance, or one of two copies after the other was used.
    /// </summary>
    private readonly void EnsureCurrent()
    {
        if (_contract is null)
            throw new BinaryTypeException(
                "This MemberWriter was not handed out by the serializer; a contract writes its members " +
                "only through the writer it is given.");

        if (_writer.State.MemberCalls != _mark)
            throw new BinaryTypeException(
                $"The contract of '{_contract.Type}' used a copy of its MemberWriter. A contract writes its " +
                "members through the writer it is handed, by reference; a copy is refused once either it " +
                "or the original has been used.");
    }
}
