namespace ViShap.Viper.Engine;

/// <summary>
/// Write-side graph traversal. This is the only place in the project that recurses over an object
/// graph, so depth, object-graph nodes, cycle detection, reference identity and the keyed layout are
/// each implemented exactly once. Formatters contribute encoding, never accounting.
/// </summary>
internal sealed class GraphWriter
{
    private readonly ValueWriter _values;
    private readonly SerializationOperation _operation;
    private readonly WriteReferenceTable? _references;
    private readonly HashSet<object>? _activeAncestors;

    public GraphWriter(ValueWriter values, SerializationOperation operation)
    {
        _values = values;
        _operation = operation;
        _references = operation.PreserveReferences ? new WriteReferenceTable() : null;
        _activeAncestors = operation.PreserveReferences
            ? null
            : new HashSet<object>(ReferenceEqualityComparer.Instance);
    }

    public void WriteRoot<T>(T value) => WriteValue(value, typeof(T));

    public void WriteValue(object? value, Type declaredType)
    {
        var effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        bool canBeNull = !declaredType.IsValueType || effectiveType != declaredType;

        if (canBeNull)
        {
            _values.WriteBoolean(value is not null);
            if (value is null)
                return;
        }
        else if (value is null)
        {
            throw new BinaryTypeException(
                $"A null value cannot be written for the non-nullable type '{declaredType}'.");
        }

        var formatter = FormatterRegistry.Resolve(effectiveType);

        if (formatter is IScalarFormatter scalar)
        {
            scalar.Write(_values, value!, effectiveType);
            return;
        }

        WriteStructural(value!, effectiveType, formatter);
    }

    private void WriteStructural(object value, Type effectiveType, ITypeFormatter? formatter)
    {
        if (_references is not null && !effectiveType.IsValueType)
        {
            if (_references.TryGetVisibleId(value, out int existingId))
            {
                _values.WriteByte(1);
                _values.WriteInt32(existingId);
                return;
            }

            _values.WriteByte(0);
            _values.WriteInt32(_references.Register(value));
        }

        using var depth = _operation.Budget.EnterDepth();
        _operation.Budget.ConsumeObjectGraphNodes(1);

        bool tracksCycles = _activeAncestors is not null && !effectiveType.IsValueType;
        if (tracksCycles && !_activeAncestors!.Add(value))
            throw new BinaryTypeException(
                $"Circular reference detected while serializing '{value.GetType()}' — an object of " +
                "this type refers back to an ancestor already being written. Enable " +
                "BinarySerializerOptions.Configure().PreserveReferences(), break the cycle, or " +
                "exclude one side with [BinaryIgnore].");

        try
        {
            switch (formatter)
            {
                case ISequenceFormatter sequence:
                    WriteSequence(sequence, value, effectiveType);
                    break;
                case IMapFormatter map:
                    WriteMap(map, value, effectiveType);
                    break;
                case ICompositeFormatter composite:
                    composite.Write(new CompositeWriter(this, _values), value, effectiveType);
                    break;
                default:
                    WriteObject(value, effectiveType);
                    break;
            }
        }
        finally
        {
            if (tracksCycles)
                _activeAncestors!.Remove(value);
        }
    }

    private void WriteSequence(ISequenceFormatter formatter, object value, Type declaredType)
    {
        var elementType = formatter.ElementType(declaredType);
        long maximum = MaximumFor(formatter.CountKind);

        if (!formatter.ReverseOnWrite && formatter.CountOf(value) is { } knownCount)
        {
            var count = _values.WriteCount(knownCount, formatter.CountKind, formatter.CountName);

            int written = 0;
            foreach (var element in formatter.Enumerate(value, declaredType))
            {
                if (written == count.Value)
                    throw new BinaryFormatException(
                        $"{formatter.CountName} changed while writing '{declaredType}'.");

                WriteValue(element, elementType);
                written++;
            }

            if (written != count.Value)
                throw new BinaryFormatException(
                    $"{formatter.CountName} changed while writing '{declaredType}'.");

            return;
        }

        var items = Materialize(formatter.Enumerate(value, declaredType), maximum, formatter.CountName);
        if (formatter.ReverseOnWrite)
            items.Reverse();

        _values.WriteCount(items.Count, formatter.CountKind, formatter.CountName);
        foreach (var element in items)
            WriteValue(element, elementType);
    }

    private void WriteMap(IMapFormatter formatter, object value, Type declaredType)
    {
        var (keyType, valueType) = formatter.EntryTypes(declaredType);
        long maximum = MaximumFor(formatter.CountKind);

        if (formatter.CountOf(value) is { } knownCount)
        {
            var count = _values.WriteCount(knownCount, formatter.CountKind, formatter.CountName);

            int written = 0;
            foreach (var (entryKey, entryValue) in formatter.Enumerate(value, declaredType))
            {
                if (written == count.Value)
                    throw new BinaryFormatException(
                        $"{formatter.CountName} changed while writing '{declaredType}'.");

                WriteValue(entryKey, keyType);
                WriteValue(entryValue, valueType);
                written++;
            }

            if (written != count.Value)
                throw new BinaryFormatException(
                    $"{formatter.CountName} changed while writing '{declaredType}'.");

            return;
        }

        var entries = Materialize(formatter.Enumerate(value, declaredType), maximum, formatter.CountName);
        _values.WriteCount(entries.Count, formatter.CountKind, formatter.CountName);
        foreach (var (entryKey, entryValue) in entries)
        {
            WriteValue(entryKey, keyType);
            WriteValue(entryValue, valueType);
        }
    }

    /// <summary>
    /// Pulls at most <paramref name="maximum"/> items out of a lazy sequence. A sequence that is too
    /// long — or infinite — is rejected as soon as it crosses the limit, never after being fully
    /// enumerated.
    /// </summary>
    private static List<T> Materialize<T>(IEnumerable<T> source, long maximum, string what)
    {
        var items = new List<T>();
        foreach (var item in source)
        {
            if (items.Count >= maximum)
                throw new BinaryLimitException(
                    $"{what} exceeds the configured maximum of {maximum}.");

            items.Add(item);
        }

        return items;
    }

    private long MaximumFor(CountKind kind) => kind switch
    {
        CountKind.Array => _operation.Limits.MaxArrayLength,
        CountKind.Collection => _operation.Limits.MaxCollectionLength,
        CountKind.Dictionary => _operation.Limits.MaxDictionaryEntries,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private void WriteObject(object value, Type declaredType)
    {
        var runtimeType = value.GetType();
        var union = TypeContractCache.GetUnion(declaredType);

        if (union is not null)
        {
            if (!union.TryGetTag(runtimeType, out byte tag))
                throw new BinaryTypeException(
                    $"Runtime type '{runtimeType}' is not allowed for declared type '{declaredType}' — " +
                    $"add [BinaryUnion(tag, typeof({runtimeType.Name}))] to '{declaredType.Name}'.");

            _values.WriteByte(tag);
        }
        else if (runtimeType != declaredType)
        {
            throw new BinaryTypeException(
                $"Declared type '{declaredType}' received a value of runtime type '{runtimeType}', " +
                $"but '{declaredType.Name}' has no [BinaryUnion] map — the derived layout could not " +
                $"be read back. Add [BinaryUnion(tag, typeof({runtimeType.Name}))] to " +
                $"'{declaredType.Name}', or declare the member as '{runtimeType.Name}'.");
        }

        var contract = TypeContractCache.Get(runtimeType);
        if (contract.Layout == MemberLayout.Keyed)
        {
            WriteKeyedMembers(value, contract);
            return;
        }

        foreach (var member in contract.Members)
            WriteValue(member.Get(value), member.MemberType);
    }

    private void WriteKeyedMembers(object value, TypeContract contract)
    {
        if (!_values.CanSeek)
            throw new NotSupportedException(
                $"Type '{contract.Type}' uses [BinaryContract]/[BinaryKey], which requires a " +
                "seekable payload stream: each field's length is written ahead of the field and " +
                "patched once the field's size is known.");

        var members = contract.Members;
        if (members.Length > _operation.Limits.MaxKeyedFields)
            throw new BinaryLimitException(
                $"Keyed field count {members.Length} exceeds the configured maximum of " +
                $"{_operation.Limits.MaxKeyedFields} (MaxKeyedFields).");

        _operation.Budget.ConsumeKeyedFields(members.Length);
        _values.Write7BitEncodedInt(members.Length);

        foreach (var member in members)
        {
            _values.Write7BitEncodedInt(member.Key!.Value);

            long lengthPosition = _values.Position;
            _values.WriteInt32(0);
            long payloadStart = _values.Position;

            WriteKeyedFieldPayload(member, value);

            long payloadEnd = _values.Position;
            long payloadLength = payloadEnd - payloadStart;

            if (payloadLength > int.MaxValue)
                throw new BinaryFormatException(
                    $"Keyed member '{member.Name}' payload length {payloadLength} exceeds Int32 range.");

            _operation.Phases.CheckPayload(payloadLength, $"Keyed member '{member.Name}' payload length");

            _values.Position = lengthPosition;
            _values.WriteInt32((int)payloadLength);
            _values.Position = payloadEnd;
        }
    }

    /// <summary>
    /// Writes one keyed field inside its own reference scope, so an object shared with a sibling
    /// field is written out again instead of becoming a back reference that a reader skipping this
    /// field could never resolve.
    /// </summary>
    private void WriteKeyedFieldPayload(MemberBinding member, object value)
    {
        if (_references is null)
        {
            WriteValue(member.Get(value), member.MemberType);
            return;
        }

        using var scope = _references.Enter();
        WriteValue(member.Get(value), member.MemberType);
    }
}
