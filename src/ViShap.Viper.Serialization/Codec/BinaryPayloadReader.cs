using System.Text;

namespace ViShap.Viper.Codec;

internal sealed class BinaryPayloadReader
{
    private readonly BinaryReader _reader;
    private readonly bool _preserveReferences;
    private readonly bool _keyedContracts;
    private readonly Dictionary<int, object> _seenObjects = new();
    private readonly List<TraceEntry>? _trace;

    internal DeserializationBudget Budget { get; }

    internal BinaryReader RawReader => _reader;

    internal IReadOnlyList<TraceEntry> Trace =>
        _trace is null
            ? Array.Empty<TraceEntry>()
            : _trace;

    public BinaryPayloadReader(
        BinaryReader reader,
        bool preserveReferences = false,
        DeserializationLimits? limits = null,
        bool enableTrace = false,
        bool keyedContracts = false)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _preserveReferences = preserveReferences;
        _keyedContracts = keyedContracts;

        var actualLimits = limits ?? DeserializationLimits.Default;
        actualLimits.Validate();

        Budget = new DeserializationBudget(actualLimits);

        _trace = enableTrace
            ? new List<TraceEntry>(capacity: 64)
            : null;
    }

    private BinaryPayloadReader(
        BinaryReader reader,
        bool preserveReferences,
        bool keyedContracts,
        DeserializationBudget budget,
        Dictionary<int, object> seenObjects,
        List<TraceEntry>? trace)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _preserveReferences = preserveReferences;
        _keyedContracts = keyedContracts;
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        _seenObjects = seenObjects ?? throw new ArgumentNullException(nameof(seenObjects));
        _trace = trace;
    }

    public T? Deserialize<T>() => (T?)ReadValue(typeof(T));

    public T? Deserialize<T>(T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);

        bool hasValue = _reader.ReadBoolean();
        if (!hasValue)
            return null;

        var declaredType = typeof(T);
        var polymorphicMap = PolymorphicTypeCache.GetMap(declaredType);

        if (_preserveReferences)
        {
            byte marker = _reader.ReadByte();
            int refId = _reader.ReadInt32();
            
            if (refId < 0)
                throw new BinaryFormatException(
                    $"Reference id {refId} must be non-negative.");

            if (marker is not 0 and not 1)
            {
                throw new BinaryFormatException(
                    $"Unknown reference marker {marker}.");
            }
            
            if (marker == 1)
                throw new BinaryTypeException(
                    "The root object is a reference to an earlier object, not a first occurrence — " +
                    "Deserialize(T existingInstance) can't populate an instance for reference-only " +
                    "data. Use the parameterless Deserialize<T>() instead.");

            _seenObjects[refId] = existingInstance;
        }

        Type typeToConstruct = declaredType;

        if (polymorphicMap is not null)
        {
            byte discriminator = _reader.ReadByte();

            if (!polymorphicMap.TryGetType(discriminator, out var runtimeType) || runtimeType is null)
                throw new BinaryTypeException(
                    $"Unknown discriminator '{discriminator}' for declared type '{declaredType}'.");

            if (runtimeType != declaredType)
                throw new BinaryTypeException(
                    $"The root object's actual type is '{runtimeType}', not '{declaredType}' — " +
                    "an existing instance of the declared type can't be reused for a different runtime type.");

            typeToConstruct = runtimeType;
        }

        PopulateMembers(existingInstance, typeToConstruct);

        return existingInstance;
    }

    public void Deserialize<T>(ref T existingInstance) where T : struct
    {
        object boxed = existingInstance;
        PopulateMembers(boxed, typeof(T));

        existingInstance = (T)boxed;
    }

    internal object? ReadValue(Type declaredType)
    {
        long offsetBefore =
            _reader.BaseStream.CanSeek
                ? _reader.BaseStream.Position
                : -1;

        int depthBefore = Budget.Depth;

        Type effectiveType =
            Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        bool canBeNull =
            !declaredType.IsValueType ||
            effectiveType != declaredType;

        if (canBeNull)
        {
            bool hasValue = _reader.ReadBoolean();

            if (!hasValue)
            {
                AddTrace(offsetBefore, depthBefore, effectiveType, null);
                return null;
            }
        }

        var result = TypeFormatterRegistry.Resolve(effectiveType).Read(this, effectiveType);

        AddTrace(offsetBefore, depthBefore, effectiveType, result);

        return result;
    }

    private void AddTrace(
        long offset,
        int depth,
        Type type,
        object? value)
    {
        if (_trace is null)
            return;

        string? valueText = value switch
        {
            null => "null",

            string or Guid or DateTime or TimeSpan or bool or
            byte or sbyte or short or ushort or
            int or uint or long or ulong or
            float or double or decimal or char
                => value.ToString(),

            _ => null
        };

        _trace.Add(
            new TraceEntry(offset, depth, type.Name, valueText));

        if (_trace.Count > 500)
            _trace.RemoveAt(0);
    }

    internal object? ReadElement(Type declaredType) =>
        ReadValue(declaredType);

    internal int ReadInt32() => _reader.ReadInt32();

    internal bool ReadBool() => _reader.ReadBoolean();

    internal object ReadNestedTracked(Type declaredType)
    {
        using var depthScope = Budget.EnterDepth();

        if (_preserveReferences)
        {
            byte marker = _reader.ReadByte();
            int id = _reader.ReadInt32();

            if (id < 0)
                throw new BinaryFormatException(
                    $"Reference id {id} must be non-negative.");

            if (marker is not 0 and not 1)
            {
                throw new BinaryFormatException(
                    $"Unknown reference marker {marker}.");
            }
            
            if (marker == 1)
            {
                if (!_seenObjects.TryGetValue(id, out var existing))
                    throw new BinaryTypeException(
                        $"Reference to object id {id} was not found — " +
                        "the data may be corrupted or from an incompatible version.");

                return existing;
            }

            var instance = ConstructNested(declaredType);
            _seenObjects[id] = instance;
            PopulateNested(instance);
            return instance;
        }

        var freshInstance = ConstructNested(declaredType);
        PopulateNested(freshInstance);
        return freshInstance;
    }

    private object ConstructNested(Type declaredType)
    {
        var polymorphicMap = PolymorphicTypeCache.GetMap(declaredType);

        if (polymorphicMap is not null)
        {
            byte discriminator = _reader.ReadByte();

            if (!polymorphicMap.TryGetType(discriminator, out var runtimeType) || runtimeType is null)
                throw new BinaryTypeException(
                    $"Unknown discriminator '{discriminator}' for declared type '{declaredType}'.");
            
            return Activator.CreateInstance(runtimeType)!;
        }

        return Activator.CreateInstance(declaredType)!;
    }

    private void PopulateNested(object instance) =>
        PopulateMembers(instance, instance.GetType());

    private void PopulateMembers(object instance, Type type)
    {
        var plan = TypeAccessorCache.GetOrBuild(type);

        if (plan.UseKeyedEncoding)
        {
            if (!_keyedContracts)
            {
                throw new BinaryFormatNotSupportedException(
                    $"Type '{type}' uses [BinaryContract]/[BinaryKey], " +
                    "which requires V1 keyed wire encoding to provide schema-evolution tolerance.");
            }

            ReadKeyedMembers(instance, plan);
            return;
        }

        foreach (var accessor in plan.Members)
            accessor.Setter(instance, ReadValue(accessor.MemberType));
    }

    private void ReadKeyedMembers(object instance, TypeAccessorPlan plan)
    {
        int fieldCount = 
            DeserializationGuard.ReadBounded7BitEncodedInt(
                _reader,
                "keyed field count");
        
        if (fieldCount > Budget.Limits.MaxCollectionLength)
            throw new BinaryFormatException(
                $"Keyed field count {fieldCount} exceeds the configured maximum of {Budget.Limits.MaxCollectionLength}.");

        var seenKeys = new HashSet<int>();
        var membersByKey = plan.MembersByKey!;

        for (int i = 0; i < fieldCount; i++)
        {
            int key =
                DeserializationGuard.ReadBounded7BitEncodedInt(
                    _reader,
                    "field key");
                    
            if (!seenKeys.Add(key))
                throw new BinaryFormatException($"Duplicate keyed field key {key}.");

            int payloadLength;
            try
            {
                payloadLength = _reader.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                throw new BinaryFormatException(
                    $"Key {key} payload length is truncated.");
            }

            DeserializationGuard.ValidateLength(
                payloadLength,
                Budget.Limits.MaxMessageBytes,
                $"Key {key} payload length");

            byte[] payload = _reader.ReadBytes(payloadLength);
            if (payload.Length != payloadLength)
                throw new BinaryFormatException(
                    $"Key {key} payload ended early. Expected {payloadLength} bytes, got {payload.Length}.");

            if (!membersByKey.TryGetValue(key, out var accessor))
                continue;

            using var fieldStream = new MemoryStream(payload, writable: false);
            using var fieldReader = new BinaryReader(fieldStream, Encoding.UTF8, leaveOpen: true);
            var child = new BinaryPayloadReader(
                fieldReader,
                _preserveReferences,
                _keyedContracts,
                Budget,
                _seenObjects,
                _trace);

            object? value = child.ReadValue(accessor.MemberType);
            if (fieldStream.Position != fieldStream.Length)
                throw new BinaryFormatException(
                    $"Key {key} payload contains trailing bytes after decoding '{accessor.Name}'.");

            accessor.Setter(instance, value);
        }
    }
}