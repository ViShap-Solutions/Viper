using ViShap.Viper.Formatters;

namespace ViShap.Viper.Codec;

internal sealed class BinaryPayloadWriter(BinaryWriter writer, bool preserveReferences = false)
{
    private readonly Dictionary<object, int> _seenObjects = new(ReferenceEqualityComparer.Instance);
    private int _nextObjectId;
    private readonly HashSet<object> _activeAncestors = new(ReferenceEqualityComparer.Instance);

    internal BinaryWriter RawWriter => writer;

    public void Serialize<T>(T data) => WriteValue(data, typeof(T));

    internal void WriteValue(object? value, Type declaredType)
    {
        Type effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        bool canBeNull = !declaredType.IsValueType || effectiveType != declaredType;

        if (canBeNull)
        {
            writer.Write(value != null);
            if (value is null) return;
        }
        TypeFormatterRegistry.Resolve(effectiveType).Write(this, value!, effectiveType);
    }

    internal void WriteElement(object? value, Type declaredType) => WriteValue(value, declaredType);
    internal void WriteInt32(int value) => writer.Write(value);
    internal void WriteBool(bool value) => writer.Write(value);

    internal void WriteNestedTracked(object value, Type declaredType)
    {
        if (preserveReferences)
        {
            if (_seenObjects.TryGetValue(value, out int existingId))
            {
                writer.Write((byte)1);
                writer.Write(existingId);
                return;
            }

            int id = _nextObjectId++;
            _seenObjects[value] = id;
            writer.Write((byte)0);
            writer.Write(id);
        }

        bool tracksForCycles = !value.GetType().IsValueType;
        if (tracksForCycles && !_activeAncestors.Add(value))
            throw new BinaryTypeException(
                $"Circular reference detected while serializing '{value.GetType()}' — an object " +
                "of this type refers back to an ancestor already being written. Enable " +
                "BinarySerializerOptions.PreserveReferences, break the cycle, or exclude one side " +
                "with [BinaryIgnore].");

        try
        {
            WritePolymorphicOrPlainNested(value, declaredType);
        }
        finally
        {
            if (tracksForCycles) _activeAncestors.Remove(value);
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

            writer.Write(discriminator);
        }

        var plan = TypeAccessorCache.GetOrBuild(value.GetType());
        foreach (var accessor in plan.Members)
            WriteValue(accessor.Getter(value), accessor.MemberType);
    }
}