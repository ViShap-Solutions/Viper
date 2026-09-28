namespace ViShap.Viper.Engine;

/// <summary>
/// What a type contract writes its members through: one value per call, and nothing else — no bytes,
/// no counts and no position. The engine frames each value, keys and measures each keyed field, and
/// checks every call against the contract's description: the member it names, its type, its key and
/// the number of calls. A mismatch is <see cref="BinaryTypeException"/>, never a distorted wire.
/// <para>
/// Only <see cref="Begin"/> creates one: it lends the engine's writer for one contract call, and
/// <see cref="End"/> takes it back, advanced.
/// </para>
/// </summary>
internal ref struct MemberWriter
{
    private WireWriter _writer;
    private readonly TypeContract _contract;
    private int _calls;

    private MemberWriter(WireWriter writer, TypeContract contract)
    {
        _writer = writer;
        _contract = contract;
        _calls = 0;
    }

    /// <summary>
    /// Opens the members of one value. Under a keyed layout the field count is checked against
    /// <c>MaxKeyedFields</c>, charged to the keyed field budget and written first — one higher when
    /// <paramref name="nullFolded"/>, because it then carries the value's null.
    /// </summary>
    internal static MemberWriter Begin(scoped ref WireWriter writer, TypeContract contract, bool nullFolded)
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
        if (_calls != _contract.Members.Length)
            throw Contracts.Mismatch(
                _contract,
                _contract.Members[_calls],
                $"it wrote {_calls} of its {_contract.Members.Length} members");

        writer = _writer;
    }

    /// <summary>Writes the next positional member: the whole value, framed.</summary>
    public void Member<TMember>(TMember value)
    {
        Next<TMember>(MemberLayout.Positional, key: null);
        FormatterCache<TMember>.Instance.Write(ref _writer, value);
    }

    /// <summary>
    /// Writes the next keyed field: its key, a length reserved ahead of the value, the value — inside
    /// its own reference scope — and then the length, patched in the serializer's buffer.
    /// </summary>
    public void Field<TMember>(int key, TMember value)
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
    }

    private MemberDescription Next<TMember>(MemberLayout layout, int? key)
    {
        var members = _contract.Members;

        if (_contract.Layout != layout)
            throw Contracts.WrongLayout(_contract, layout, write: true);

        if (_calls >= members.Length)
            throw Contracts.TooManyCalls(_contract, typeof(TMember), write: true);

        var member = members[_calls];

        if (key is { } written && member.Key != written)
            throw Contracts.Mismatch(
                _contract, member, $"it wrote key {written} where key {member.Key} comes next");

        if (typeof(TMember) != member.MemberType)
            throw Contracts.Mismatch(
                _contract, member, $"it wrote a '{typeof(TMember)}' for a member of type '{member.MemberType}'");

        _calls++;
        return member;
    }
}
