using ViShap.Viper.Formatters;

namespace ViShap.Viper.Codec;

internal sealed class BinaryPayloadReader(BinaryReader reader, bool preserveReferences = false)
{
    private readonly Dictionary<int, object> _seenObjects = new();

    internal BinaryReader RawReader => reader;

    public T? Deserialize<T>() where T : class => (T?)ReadValue(typeof(T));

    public T? Deserialize<T>(T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);

        bool hasValue = reader.ReadBoolean();
        if (!hasValue) return null;

        var declaredType = typeof(T);
        var polymorphicMap = PolymorphicTypeCache.GetMap(declaredType);

        if (preserveReferences)
        {
            byte marker = reader.ReadByte();
            int refId = reader.ReadInt32();
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
            byte discriminator = reader.ReadByte();
            if (!polymorphicMap.TryGetType(discriminator, out var runtimeType) || runtimeType is null)
                throw new BinaryTypeException($"Unknown discriminator '{discriminator}' for declared type '{declaredType}'.");
            if (runtimeType != declaredType)
                throw new BinaryTypeException(
                    $"The root object's actual type is '{runtimeType}', not '{declaredType}' — an " +
                    "existing instance of the declared type can't be reused for a different runtime type.");
            typeToConstruct = runtimeType;
        }

        var plan = TypeAccessorCache.GetOrBuild(typeToConstruct);
        foreach (var accessor in plan.Members)
            accessor.Setter(existingInstance, ReadValue(accessor.MemberType));

        return existingInstance;
    }

    internal object? ReadValue(Type declaredType)
    {
        Type effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        bool canBeNull = !declaredType.IsValueType || effectiveType != declaredType;

        if (canBeNull)
        {
            bool hasValue = reader.ReadBoolean();
            if (!hasValue) return null;
        }

        return TypeFormatterRegistry.Resolve(effectiveType).Read(this, effectiveType);
    }

    internal object? ReadElement(Type declaredType) => ReadValue(declaredType);
    internal int ReadInt32() => reader.ReadInt32();
    internal bool ReadBool() => reader.ReadBoolean();

    internal object ReadNestedTracked(Type declaredType)
    {
        if (preserveReferences)
        {
            byte marker = reader.ReadByte();
            int id = reader.ReadInt32();

            if (marker == 1)
            {
                if (!_seenObjects.TryGetValue(id, out var existing))
                    throw new BinaryTypeException($"Reference to object id {id} was not found — the data may be corrupted or from an incompatible version.");
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
            byte discriminator = reader.ReadByte();
            if (!polymorphicMap.TryGetType(discriminator, out var runtimeType) || runtimeType is null)
                throw new BinaryTypeException($"Unknown discriminator '{discriminator}' for declared type '{declaredType}'.");
            return Activator.CreateInstance(runtimeType)!;
        }

        return Activator.CreateInstance(declaredType)!;
    }

    private void PopulateNested(object instance)
    {
        var plan = TypeAccessorCache.GetOrBuild(instance.GetType());
        foreach (var accessor in plan.Members)
            accessor.Setter(instance, ReadValue(accessor.MemberType));
    }
}