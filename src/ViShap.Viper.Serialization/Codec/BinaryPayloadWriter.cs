namespace ViShap.Viper.Codec;

internal sealed class BinaryPayloadWriter
{
    private readonly BinaryWriter _writer;
    private readonly bool _preserveReferences;
    private readonly int _maxDepth;

    private readonly Dictionary<object, int> _seenObjects =
        new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<object> _activeAncestors =
        new(ReferenceEqualityComparer.Instance);

    private int _nextObjectId;
    private int _depth;

    internal BinaryWriter RawWriter => _writer;

    public BinaryPayloadWriter(
        BinaryWriter writer,
        bool preserveReferences = false,
        DeserializationLimits? limits = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _preserveReferences = preserveReferences;

        var actualLimits = limits ?? DeserializationLimits.Default;
        actualLimits.Validate();

        _maxDepth = actualLimits.MaxDepth;
    }

    public void Serialize<T>(T data) => WriteValue(data, typeof(T));

    internal void WriteValue(object? value, Type declaredType)
    {
        Type effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        bool canBeNull = !declaredType.IsValueType || effectiveType != declaredType;

        if (canBeNull)
        {
            _writer.Write(value != null);
            if (value is null) return;
        }
        TypeFormatterRegistry.Resolve(effectiveType).Write(this, value!, effectiveType);
    }

    internal void WriteElement(object? value, Type declaredType) => WriteValue(value, declaredType);
    internal void WriteInt32(int value) => _writer.Write(value);
    internal void WriteBool(bool value) => _writer.Write(value);

    internal void WriteNestedTracked(object value, Type declaredType)
    {
        EnterDepth();

        try
        {
            if (_preserveReferences)
            {
                if (_seenObjects.TryGetValue(
                        value,
                        out int existingId))
                {
                    _writer.Write((byte)1);
                    _writer.Write(existingId);
                    return;
                }

                int id = _nextObjectId++;

                _seenObjects[value] = id;

                _writer.Write((byte)0);
                _writer.Write(id);
            }

            bool tracksForCycles = !value.GetType().IsValueType;

            if (tracksForCycles && !_activeAncestors.Add(value))
                throw new BinaryTypeException(
                    $"Circular reference detected while serializing '{value.GetType()}' — " +
                    "an object of this type refers back to an ancestor already being written. " +
                    "Enable BinarySerializerOptions.PreserveReferences, break the cycle, " +
                    "or exclude one side with [BinaryIgnore].");
            

            try
            {
                WritePolymorphicOrPlainNested(value, declaredType);
            }
            finally
            {
                if (tracksForCycles) _activeAncestors.Remove(value);
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void EnterDepth()
    {
        if (++_depth > _maxDepth)
        {
            _depth--;
            throw new BinaryTypeException(
                $"Serialization nesting depth exceeds the configured limit of {_maxDepth}.");
        }
    }

    private void WritePolymorphicOrPlainNested(object value, Type declaredType)
    {
        var polymorphicMap = PolymorphicTypeCache.GetMap(declaredType);
        if (polymorphicMap is not null)
        {
            var runtimeType = value.GetType();
            if (!polymorphicMap.TryGetDiscriminator(runtimeType, out byte discriminator))
                throw new BinaryTypeException(
                    $"Runtime type '{runtimeType}' is not allowed for declared type '{declaredType}' — " +
                    $"add [BinaryKnownType(typeof({runtimeType.Name}))] on '{declaredType.Name}'.");

            _writer.Write(discriminator);
        }

        var plan = TypeAccessorCache.GetOrBuild(value.GetType());
        foreach (var accessor in plan.Members)
            WriteValue(accessor.Getter(value), accessor.MemberType);
    }
}