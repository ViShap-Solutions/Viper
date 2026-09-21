namespace ViShap.Viper.Engine;

/// <summary>
/// Read-side graph traversal — the mirror of <see cref="GraphWriter"/> and the only recursion over
/// incoming data. Depth, node budget, identity registration, keyed-field skipping and the
/// "consume exactly what was declared" rule all live here, so no formatter can bypass them.
/// </summary>
internal sealed class GraphReader
{
    private readonly ValueReader _values;
    private readonly SerializationOperation _operation;
    private readonly ReadReferenceTable? _references;

    public GraphReader(ValueReader values, SerializationOperation operation)
        : this(values, operation, operation.PreserveReferences ? new ReadReferenceTable() : null)
    {
    }

    private GraphReader(
        ValueReader values,
        SerializationOperation operation,
        ReadReferenceTable? references)
    {
        _values = values;
        _operation = operation;
        _references = references;
    }

    public ValueReader Values => _values;

    public T? ReadRoot<T>() => (T?)ReadValue(typeof(T));

    public object? ReadValue(Type declaredType)
    {
        var effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        bool canBeNull = !declaredType.IsValueType || effectiveType != declaredType;

        if (canBeNull && !_values.ReadBoolean())
            return null;

        var formatter = FormatterRegistry.Resolve(effectiveType);

        if (formatter is IScalarFormatter scalar)
            return scalar.Read(_values, effectiveType);

        return ReadStructural(effectiveType, formatter);
    }

    private object ReadStructural(Type effectiveType, ITypeFormatter? formatter)
    {
        int referenceId = -1;

        if (_references is not null && !effectiveType.IsValueType)
        {
            byte marker = _values.ReadByte();
            int id = _values.ReadInt32();

            if (id < 0)
                throw new BinaryFormatException($"Reference id {id} must be non-negative.");

            if (marker is not 0 and not 1)
                throw new BinaryFormatException($"Unknown reference marker {marker}.");

            if (marker == 1)
                return Resolve(id);

            referenceId = id;
        }

        using var depth = _operation.Budget.EnterDepth();
        _operation.Budget.ConsumeObjectGraphNodes(1);

        return formatter switch
        {
            ISequenceFormatter sequence => ReadSequence(sequence, effectiveType, referenceId),
            IMapFormatter map => ReadMap(map, effectiveType, referenceId),
            ICompositeFormatter composite => ReadComposite(composite, effectiveType, referenceId),
            _ => ReadObject(effectiveType, referenceId)
        };
    }

    private object Resolve(int id)
    {
        if (!_references!.TryResolve(id, out var existing))
            throw new BinaryFormatException(
                $"Reference to object id {id} was not found in the visible object graph. " +
                "References are only valid within the object that defines them and its descendants.");

        if (ReferenceEquals(existing, ReadReferenceTable.Pending))
            throw new BinaryFormatException(
                $"Reference to object id {id} points at an object that is still being constructed — " +
                "a cycle through a container that cannot be created before its elements " +
                "(array, immutable or frozen collection) is not representable.");

        return existing!;
    }

    private object ReadSequence(ISequenceFormatter formatter, Type declaredType, int referenceId)
    {
        var elementType = formatter.ElementType(declaredType);
        var count = _values.ReadCount(formatter.CountKind, formatter.CountName);

        var builder = formatter.CreateBuilder(declaredType, count.CapacityHint);
        Register(referenceId, formatter.BuilderIsInstance ? builder : ReadReferenceTable.Pending);

        for (int i = 0; i < count.Value; i++)
        {
            var element = ReadValue(elementType);
            try
            {
                formatter.Add(builder, element, declaredType);
            }
            catch (ArgumentException ex)
            {
                throw Refused(formatter.CountName, declaredType, ex);
            }
        }

        object completed;
        try
        {
            completed = formatter.Complete(builder, declaredType);
        }
        catch (ArgumentException ex)
        {
            throw Refused(formatter.CountName, declaredType, ex);
        }

        RequireMaterialized(formatter.CountOf(completed), count.Value, formatter.CountName, declaredType);

        if (referenceId >= 0 && !formatter.BuilderIsInstance)
            _references!.Replace(referenceId, completed);

        return completed;
    }

    private object ReadMap(IMapFormatter formatter, Type declaredType, int referenceId)
    {
        var (keyType, valueType) = formatter.EntryTypes(declaredType);
        var count = _values.ReadCount(formatter.CountKind, formatter.CountName);

        var builder = formatter.CreateBuilder(declaredType, count.CapacityHint);
        Register(referenceId, formatter.BuilderIsInstance ? builder : ReadReferenceTable.Pending);

        for (int i = 0; i < count.Value; i++)
        {
            var key = ReadValue(keyType);
            var value = ReadValue(valueType);
            try
            {
                formatter.Add(builder, key, value, declaredType);
            }
            catch (ArgumentException ex)
            {
                throw Refused(formatter.CountName, declaredType, ex);
            }
        }

        object completed;
        try
        {
            completed = formatter.Complete(builder, declaredType);
        }
        catch (ArgumentException ex)
        {
            throw Refused(formatter.CountName, declaredType, ex);
        }

        RequireMaterialized(formatter.CountOf(completed), count.Value, formatter.CountName, declaredType);

        if (referenceId >= 0 && !formatter.BuilderIsInstance)
            _references!.Replace(referenceId, completed);

        return completed;
    }

    /// <summary>
    /// Classifies a container's refusal of a value that came off the wire. The container raises the
    /// framework's own argument exception — for a duplicate key, a duplicate entry or a null key —
    /// and that is a statement about the payload, not about the caller's arguments, so it is a
    /// malformed payload here and never leaves the taxonomy.
    /// </summary>
    private static BinaryFormatException Refused(string what, Type declaredType, ArgumentException cause) =>
        new($"{what}: '{declaredType}' refused a value from the payload — a duplicate key, a " +
            "duplicate entry or a null key is not admitted.", cause);

    /// <summary>
    /// Requires the container to hold exactly what the payload declared. A container that collapses
    /// duplicates silently — a set, a concurrent dictionary, a frozen collection — reports fewer
    /// elements than were read, and that difference is the only evidence that the payload carried a
    /// duplicate at all.
    /// </summary>
    private static void RequireMaterialized(int? actual, int declared, string what, Type declaredType)
    {
        if (actual is { } materialized && materialized != declared)
            throw new BinaryFormatException(
                $"{what} declares {declared}, but '{declaredType}' materialized {materialized} — a " +
                "duplicate key or element is not admitted.");
    }

    private object ReadComposite(ICompositeFormatter formatter, Type declaredType, int referenceId)
    {
        Register(referenceId, ReadReferenceTable.Pending);
        var value = formatter.Read(this, declaredType);
        if (referenceId >= 0)
            _references!.Replace(referenceId, value);

        return value;
    }

    private object ReadObject(Type declaredType, int referenceId)
    {
        var runtimeType = declaredType;
        var union = TypeContractCache.GetUnion(declaredType);

        if (union is not null)
        {
            byte tag = _values.ReadByte();
            if (!union.TryGetType(tag, out var resolved) || resolved is null)
                throw new BinaryTypeException(
                    $"Unknown discriminator '{tag}' for declared type '{declaredType}'.");

            runtimeType = resolved;
        }

        var instance = Construct(runtimeType);
        Register(referenceId, instance);
        PopulateMembers(instance, TypeContractCache.Get(runtimeType));
        return instance;
    }

    /// <summary>Populates an instance the caller supplied, used by the populate-in-place overloads.</summary>
    public object ReadInto(object instance, Type declaredType)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (FormatterRegistry.Resolve(declaredType) is { } formatter)
            throw new BinaryTypeException(
                $"Populate-in-place is only supported for member-encoded types; '{declaredType}' is " +
                $"encoded by {formatter.GetType().Name}. Use the parameterless Deserialize<T>() overload.");

        if (!_values.ReadBoolean())
            throw new BinaryFormatException(
                "The payload holds a null root value, which cannot populate an existing instance.");

        int referenceId = -1;
        if (_references is not null)
        {
            byte marker = _values.ReadByte();
            int id = _values.ReadInt32();

            if (id < 0)
                throw new BinaryFormatException($"Reference id {id} must be non-negative.");

            if (marker == 1)
                throw new BinaryTypeException(
                    "The root object is a back reference, not a first occurrence — an existing " +
                    "instance cannot be populated from reference-only data.");

            if (marker != 0)
                throw new BinaryFormatException($"Unknown reference marker {marker}.");

            referenceId = id;
        }

        using var depth = _operation.Budget.EnterDepth();
        _operation.Budget.ConsumeObjectGraphNodes(1);

        var union = TypeContractCache.GetUnion(declaredType);
        if (union is not null)
        {
            byte tag = _values.ReadByte();
            if (!union.TryGetType(tag, out var runtimeType) || runtimeType is null)
                throw new BinaryTypeException(
                    $"Unknown discriminator '{tag}' for declared type '{declaredType}'.");

            if (runtimeType != instance.GetType())
                throw new BinaryTypeException(
                    $"The payload holds '{runtimeType}', but the supplied instance is " +
                    $"'{instance.GetType()}' — an existing instance cannot be reused for a " +
                    "different runtime type.");
        }
        else if (instance.GetType() != declaredType)
        {
            throw new BinaryTypeException(
                $"The supplied instance is '{instance.GetType()}' but the payload was written for " +
                $"'{declaredType}', which has no [BinaryUnion] map.");
        }

        Register(referenceId, instance);
        PopulateMembers(instance, TypeContractCache.Get(instance.GetType()));
        return instance;
    }

    private void Register(int referenceId, object value)
    {
        if (referenceId >= 0)
            _references!.Register(referenceId, value);
    }

    private static object Construct(Type runtimeType)
    {
        var contract = TypeContractCache.Get(runtimeType);
        if (!contract.CanBeConstructed)
            throw new BinaryTypeException(
                $"'{runtimeType}' cannot be constructed during deserialization — a concrete type " +
                "with a public or non-public parameterless constructor is required. An interface or " +
                "an abstract class needs a [BinaryUnion] map naming the type to build.");

        try
        {
            return ActivatorCache.CreateInstance(runtimeType);
        }
        catch (MissingMethodException ex)
        {
            throw new BinaryTypeException(
                $"'{runtimeType}' cannot be constructed during deserialization.", ex);
        }
    }

    private void PopulateMembers(object instance, TypeContract contract)
    {
        if (contract.Layout == MemberLayout.Keyed)
        {
            ReadKeyedMembers(instance, contract);
            return;
        }

        foreach (var member in contract.Members)
            member.Set(instance, ReadValue(member.MemberType));
    }

    private void ReadKeyedMembers(object instance, TypeContract contract)
    {
        int fieldCount = _values.Read7BitEncodedInt("keyed field count");
        if (fieldCount > _operation.Limits.MaxKeyedFields)
            throw new BinaryLimitException(
                $"Keyed field count {fieldCount} exceeds the configured maximum of " +
                $"{_operation.Limits.MaxKeyedFields} (MaxKeyedFields).");

        _operation.Budget.ConsumeKeyedFields(fieldCount);

        var members = contract.MembersByKey!;
        var seenKeys = new HashSet<int>();

        for (int i = 0; i < fieldCount; i++)
        {
            int key = _values.Read7BitEncodedInt("field key");
            if (!seenKeys.Add(key))
                throw new BinaryFormatException($"Duplicate keyed field key {key}.");

            int payloadLength = _values.ReadInt32();
            _operation.Phases.CheckPayload(payloadLength, $"Key {key} payload length");
            _values.RequireAvailable(payloadLength, $"Key {key} payload");

            if (!members.TryGetValue(key, out var member))
            {
                _values.Skip(payloadLength, $"Key {key} payload");
                continue;
            }

            ReadKeyedFieldPayload(instance, member, key, payloadLength);
        }
    }

    private void ReadKeyedFieldPayload(object instance, MemberBinding member, int key, int payloadLength)
    {
        var window = _values.OpenWindow(payloadLength, $"Key {key} payload");
        var child = new GraphReader(window.Reader, _operation, _references);

        object? value;
        if (_references is null)
        {
            value = child.ReadValue(member.MemberType);
        }
        else
        {
            using var scope = _references.Enter();
            value = child.ReadValue(member.MemberType);
        }

        if (window.Stream.RemainingBytes != 0)
            throw new BinaryFormatException(
                $"Key {key} payload contains {window.Stream.RemainingBytes} trailing byte(s) after " +
                $"decoding '{member.Name}'.");

        member.Set(instance, value);
    }
}
