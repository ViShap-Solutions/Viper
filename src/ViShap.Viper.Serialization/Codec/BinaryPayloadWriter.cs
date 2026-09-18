using System.Text;

namespace ViShap.Viper.Codec;

internal sealed class BinaryPayloadWriter
{
    private readonly BinaryWriter _writer;
    private readonly bool _preserveReferences;
    private readonly bool _keyedContracts;
    private readonly SerializationLimits _limits;
    private readonly SerializationBudget _budget;

    private readonly Dictionary<object, int> _seenObjects =
        new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<object> _activeAncestors =
        new(ReferenceEqualityComparer.Instance);

    private int _nextObjectId;

    internal BinaryWriter RawWriter => _writer;
    internal SerializationBudget Budget => _budget;

    public BinaryPayloadWriter(
        BinaryWriter writer,
        bool preserveReferences = false,
        SerializationLimits? limits = null,
        bool keyedContracts = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _preserveReferences = preserveReferences;
        _keyedContracts = keyedContracts;

        var actualLimits = limits ?? SerializationLimits.Default;
        actualLimits.Validate();
        _limits = actualLimits;
        _budget = new SerializationBudget(actualLimits);
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
        using var depthScope = _budget.EnterDepth();

        if (_preserveReferences)
        {
            if (_seenObjects.TryGetValue(value, out int existingId))
            {
                _writer.Write((byte)1);
                _writer.Write(existingId);
                return;
            }

            _budget.ConsumeObjectGraphNodes(1);

            int id = _nextObjectId++;
            _seenObjects[value] = id;
            _writer.Write((byte)0);
            _writer.Write(id);
        }
        else
        {
            _budget.ConsumeObjectGraphNodes(1);
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
            if (tracksForCycles)
                _activeAncestors.Remove(value);
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

        if (plan.UseKeyedEncoding)
        {
            if (!_keyedContracts)
                throw new BinaryFormatNotSupportedException(
                    $"Type '{value.GetType()}' uses [BinaryContract]/[BinaryKey], " +
                    "which requires V1 keyed wire encoding to provide schema-evolution tolerance.");

            WriteKeyedMembers(value, plan);
            return;
        }

        foreach (var accessor in plan.Members)
            WriteValue(accessor.Getter(value), accessor.MemberType);
    }

    private void WriteKeyedMembers(object value, TypeAccessorPlan plan)
    {
        if (!_writer.BaseStream.CanSeek)
            throw new NotSupportedException("Keyed contract encoding requires a seekable payload stream.");

        var membersByKey = plan.MembersByKey!;
        
        if (membersByKey.Count > _limits.MaxKeyedFields)
            throw new BinaryLimitException(
                $"Keyed field count {membersByKey.Count} exceeds the configured maximum of {_limits.MaxKeyedFields}.");

        Write7BitEncodedInt(membersByKey.Count);

        foreach (var entry in membersByKey.OrderBy(static pair => pair.Key))
        {
            Write7BitEncodedInt(entry.Key);

            long lengthPosition = _writer.BaseStream.Position;
            _writer.Write(0);

            long payloadStart = _writer.BaseStream.Position;
            var accessor = entry.Value;
            WriteValue(accessor.Getter(value), accessor.MemberType);
            long payloadEnd = _writer.BaseStream.Position;

            long payloadLength = payloadEnd - payloadStart;
            if (payloadLength < 0)
                throw new BinaryFormatException(
                    $"Keyed member '{accessor.Name}' payload length {payloadLength} is invalid.");

            if (payloadLength > int.MaxValue)
                throw new BinaryFormatException(
                    $"Keyed member '{accessor.Name}' payload length {payloadLength} exceeds Int32 range.");

            if (payloadLength > _limits.MaxPayloadBytes)
                throw new BinaryLimitException(
                    $"Keyed member '{accessor.Name}' payload length {payloadLength} exceeds the configured maximum of {_limits.MaxPayloadBytes}.");

            _writer.BaseStream.Position = lengthPosition;
            _writer.Write((int)payloadLength);
            _writer.BaseStream.Position = payloadEnd;
        }
    }

    internal int ValidateArrayLengthForWrite(int count, string what)
    {
        if (count < 0)
            throw new BinaryFormatException($"{what} {count} must be non-negative.");
        if (count > _limits.MaxArrayLength)
            throw new BinaryLimitException(
                $"{what} {count} exceeds the configured maximum of {_limits.MaxArrayLength}.");
        _budget.ConsumeElements(count);
        return count;
    }

    internal int ValidateCollectionLengthForWrite(int count, string what)
    {
        if (count < 0)
            throw new BinaryFormatException($"{what} {count} must be non-negative.");
        if (count > _limits.MaxCollectionLength)
            throw new BinaryLimitException(
                $"{what} {count} exceeds the configured maximum of {_limits.MaxCollectionLength}.");
        _budget.ConsumeElements(count);
        return count;
    }

    internal int ValidateDictionaryEntryCountForWrite(int count, string what)
    {
        if (count < 0)
            throw new BinaryFormatException($"{what} {count} must be non-negative.");
        if (count > _limits.MaxDictionaryEntries)
            throw new BinaryLimitException(
                $"{what} {count} exceeds the configured maximum of {_limits.MaxDictionaryEntries}.");
        _budget.ConsumeElements(count);
        return count;
    }

    internal long ValidateTotalArrayElementsForWrite(int[] lengths, string what)
    {
        ArgumentNullException.ThrowIfNull(lengths);

        long total = 1;
        foreach (int length in lengths)
        {
            if (length < 0)
                throw new BinaryFormatException(
                    $"{what}: a dimension length {length} must be non-negative.");

            if (length == 0)
            {
                total = 0;
                continue;
            }

            if (total > _limits.MaxArrayLength / (long)length)
                throw new BinaryLimitException(
                    $"{what}: total element count exceeds the configured maximum of {_limits.MaxArrayLength}.");

            total *= length;
        }

        _budget.ConsumeElements(total);
        return total;
    }

    internal int ValidateBitCountForWrite(int bitCount, string what)
    {
        if (bitCount < 0)
            throw new BinaryFormatException($"{what} {bitCount} must be non-negative.");
        if (bitCount > _limits.MaxByteBlobBytes * 8L)
            throw new BinaryLimitException(
                $"{what} {bitCount} exceeds the configured maximum of {_limits.MaxByteBlobBytes * 8L}.");
        return bitCount;
    }

    internal int ValidateByteBlobLengthForWrite(int byteLength, string what)
    {
        if (byteLength < 0)
            throw new BinaryFormatException($"{what} {byteLength} must be non-negative.");
        if (byteLength > _limits.MaxByteBlobBytes)
            throw new BinaryLimitException(
                $"{what} {byteLength} exceeds the configured maximum of {_limits.MaxByteBlobBytes}.");
        return byteLength;
    }

    internal void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > _limits.MaxStringBytes)
            throw new BinaryLimitException(
                $"String byte length {byteCount} exceeds the configured maximum of {_limits.MaxStringBytes}.");
        _writer.Write(value);
    }

    private void Write7BitEncodedInt(int value)
    {
        if (value < 0)
            throw new BinaryFormatException($"7-bit encoded integer {value} must be non-negative.");

        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            _writer.Write((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        _writer.Write((byte)remaining);
    }
}