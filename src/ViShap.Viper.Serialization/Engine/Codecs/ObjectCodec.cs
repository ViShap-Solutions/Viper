using System.Runtime.CompilerServices;

namespace ViShap.Viper.Engine;

/// <summary>
/// The engine's codec for a member-encoded type. After the frame, a declared type with a
/// <see cref="BinaryUnionAttribute"/> map writes the tag of the value's runtime type; a value whose
/// runtime type differs from the declared one goes through that type's contract in the polymorphic
/// slot, the one place the engine boxes. Every other value goes through the declared type's contract,
/// typed from end to end.
/// <para>
/// A keyed type without a union map begins with its keyed field count, which carries its null when
/// the value is not reference-framed. A positional type, and any type with a union map, carries its
/// null in a flag byte.
/// </para>
/// </summary>
internal sealed class ObjectCodec<T> : StructuralCodec<T>
{
    private static readonly bool IsReference = !typeof(T).IsValueType;

    private TypeContract<T>? _contract;
    private UnionLookup? _union;

    public override CodecShape Shape => CodecShape.Object;

    protected override bool FoldsNull => Union is null && Contract.Layout == MemberLayout.Keyed;

    /// <summary>
    /// The contract of the declared type. Building one can fail — a delegate member, a duplicate
    /// key — and a failure is not kept, so every use reports it the same way.
    /// </summary>
    private TypeContract<T> Contract => _contract ??= TypeContractCache.Get<T>();

    private UnionMap? Union => (_union ??= new UnionLookup(TypeContractCache.GetUnion(typeof(T)))).Map;

    protected override void WriteBody(ref WireWriter writer, T value, bool nullFolded)
    {
        var runtimeType = IsReference ? value!.GetType() : typeof(T);
        var union = Union;

        if (union is not null)
        {
            if (!union.TryGetTag(runtimeType, out byte tag))
                throw new BinaryTypeException(
                    $"Runtime type '{runtimeType}' is not allowed for declared type '{typeof(T)}' — " +
                    $"add [BinaryUnion(tag, typeof({runtimeType.Name}))] to '{typeof(T).Name}'.");

            writer.WriteByte(tag);
        }
        else if (runtimeType != typeof(T))
        {
            throw new BinaryTypeException(
                $"Declared type '{typeof(T)}' received a value of runtime type '{runtimeType}', " +
                $"but '{typeof(T).Name}' has no [BinaryUnion] map — the derived layout could not " +
                $"be read back. Add [BinaryUnion(tag, typeof({runtimeType.Name}))] to " +
                $"'{typeof(T).Name}', or declare the member as '{runtimeType.Name}'.");
        }

        if (runtimeType == typeof(T))
        {
            ObjectMembers.Write(ref writer, Contract, in value, nullFolded);
            return;
        }

        var runtimeContract = TypeContractCache.Get(runtimeType);
        var boxed = MemberWriter.Begin(ref writer, runtimeContract, nullFolded: false);
        runtimeContract.WriteBoxed(ref boxed, value!);
        boxed.End(ref writer);
    }

    protected override T ReadBody(ref WireReader reader, int referenceId, bool nullFolded)
    {
        if (Union is { } union)
        {
            var runtimeType = ReadTag(ref reader, union);
            if (runtimeType != typeof(T))
                return (T)TypeContractCache.Get(runtimeType).ReadBoxed(ref reader, referenceId, target: null);
        }

        return ObjectMembers.ReadInstance(ref reader, Contract, referenceId, target: null, nullFolded);
    }

    /// <summary>
    /// Reads the root value into <paramref name="target"/> instead of creating one. The root must be
    /// present and a first occurrence, and its runtime type must be the target's own.
    /// </summary>
    public void Populate(ref WireReader reader, T target)
    {
        int referenceId = -1;
        bool nullFolded = false;

        if (reader.State.Graph.Read is not null)
        {
            var frame = ReferenceFrame.Read(ref reader);
            if (frame.IsNull)
                throw NullRoot();

            if (frame.IsBackReference)
                throw new BinaryFormatException(
                    "The root object is a back reference, not a first occurrence — an existing " +
                    "instance cannot be populated from reference-only data.");

            referenceId = frame.Id;
        }
        else if (FoldsNull)
        {
            if (reader.TryReadNull())
                throw NullRoot();

            nullFolded = true;
        }
        else if (!reader.ReadBoolean())
        {
            throw NullRoot();
        }

        ref var state = ref reader.State;
        using var depth = state.Budget.EnterDepth();
        state.Budget.ConsumeObjectGraphNodes(1);

        var instanceType = target!.GetType();
        if (Union is { } union)
        {
            var runtimeType = ReadTag(ref reader, union);
            if (runtimeType != instanceType)
                throw new BinaryTypeException(
                    $"The payload holds '{runtimeType}', but the supplied instance is " +
                    $"'{instanceType}' — an existing instance cannot be reused for a " +
                    "different runtime type.");
        }
        else if (instanceType != typeof(T))
        {
            throw new BinaryTypeException(
                $"The supplied instance is '{instanceType}' but the payload was written for " +
                $"'{typeof(T)}', which has no [BinaryUnion] map.");
        }

        if (instanceType == typeof(T))
            ObjectMembers.ReadInstance(ref reader, Contract, referenceId, target, nullFolded);
        else
            TypeContractCache.Get(instanceType).ReadBoxed(ref reader, referenceId, target);
    }

    private static BinaryFormatException NullRoot() =>
        new("The payload holds a null root value, which cannot populate an existing instance.");

    private static Type ReadTag(ref WireReader reader, UnionMap union)
    {
        byte tag = reader.ReadByte();
        if (!union.TryGetType(tag, out var runtimeType) || runtimeType is null)
            throw new BinaryTypeException(
                $"Unknown discriminator '{tag}' for declared type '{typeof(T)}'.");

        reader.State.Trace?.Union(tag, runtimeType);
        return runtimeType;
    }

    /// <summary>A resolved union lookup, so "no map" is remembered as well as a map.</summary>
    private sealed class UnionLookup(UnionMap? map)
    {
        public UnionMap? Map { get; } = map;
    }
}

/// <summary>
/// The engine's side of an object's members: the keyed field count and the call checks around a
/// contract's write, and construction, identity and the keyed loop around its read.
/// </summary>
internal static class ObjectMembers
{
    /// <summary>
    /// Writes the members of <paramref name="value"/> through <paramref name="contract"/>, checking
    /// every call. When <paramref name="nullFolded"/>, the keyed field count carries the value's null
    /// and is written one higher.
    /// </summary>
    public static void Write<T>(ref WireWriter writer, TypeContract<T> contract, in T value, bool nullFolded)
    {
        var members = MemberWriter.Begin(ref writer, contract, nullFolded);
        contract.Write(ref members, in value);
        members.End(ref writer);
    }

    /// <summary>
    /// Creates the instance — or takes <paramref name="target"/> — and registers it under
    /// <paramref name="referenceId"/> <em>before</em> its members are read, so a cycle back to it
    /// resolves; then reads the members into it. When <paramref name="nullFolded"/>, the keyed field
    /// count was written one higher.
    /// </summary>
    public static T ReadInstance<T>(
        ref WireReader reader,
        TypeContract<T> contract,
        int referenceId,
        object? target,
        bool nullFolded)
    {
        var instance = target is not null ? (T)target : Create(contract);

        if (referenceId >= 0)
            reader.State.Graph.Read!.Register(referenceId, instance!);

        ReadMembers(ref reader, contract, ref instance, nullFolded);
        return instance;
    }

    /// <summary>
    /// <see cref="ReadInstance{T}"/> for a value in a polymorphic slot. A value type is boxed first and
    /// read in place inside its box, so the object registered for a reference is the one returned.
    /// </summary>
    public static object ReadBoxed<T>(ref WireReader reader, TypeContract<T> contract, int referenceId, object? target)
    {
        if (!typeof(T).IsValueType)
            return ReadInstance(ref reader, contract, referenceId, target, nullFolded: false)!;

        object box = target ?? Create(contract)!;

        if (referenceId >= 0)
            reader.State.Graph.Read!.Register(referenceId, box);

        ReadMembers(ref reader, contract, ref Unsafe.As<byte, T>(ref Unsafe.As<BoxedData>(box).Data), nullFolded: false);
        return box;
    }

    /// <summary>The layout of a boxed value: the value begins at the first field of the box.</summary>
    private sealed class BoxedData
    {
        public byte Data;
    }

    private static T Create<T>(TypeContract<T> contract) =>
        contract.CanBeConstructed
            ? contract.Create()
            : throw new BinaryTypeException(
                $"'{typeof(T)}' cannot be constructed during deserialization — a concrete type " +
                "with a public or non-public parameterless constructor is required. An interface or " +
                "an abstract class needs a [BinaryUnion] map naming the type to build.");

    private static void ReadMembers<T>(ref WireReader reader, TypeContract<T> contract, ref T instance, bool nullFolded)
    {
        var trace = reader.State.Trace;

        if (contract.Layout == MemberLayout.Positional)
        {
            trace?.Shape(TraceShape.Object, contract.Members.Length);
            var members = MemberReader.Positional(ref reader, contract);
            contract.Read(ref members, ref instance);
            members.End(ref reader);
            return;
        }

        ref var state = ref reader.State;

        int fieldCount = reader.ReadFolded(nullFolded, "keyed field count");
        if (fieldCount > state.Limits.MaxKeyedFields)
            throw new BinaryLimitException(
                $"Keyed field count {fieldCount} exceeds the configured maximum of " +
                $"{state.Limits.MaxKeyedFields} (MaxKeyedFields).");

        state.Budget.ConsumeKeyedFields(fieldCount);
        trace?.Shape(TraceShape.KeyedObject, fieldCount);

        int previousKey = -1;
        for (int i = 0; i < fieldCount; i++)
        {
            long fieldStart = reader.Position;
            int key = reader.Read7BitEncodedInt("field key");
            if (key == previousKey)
                throw new BinaryFormatException($"Duplicate keyed field key {key}.");

            if (key < previousKey)
                throw new BinaryFormatException(
                    $"Keyed field key {key} follows key {previousKey}; fields must appear in ascending " +
                    "key order.");

            previousKey = key;

            int payloadLength = reader.ReadInt32();
            if (!state.Phases.AdmitsPayload(payloadLength))
                state.Phases.CheckPayload(payloadLength, $"Key {key} payload length");

            if (trace is not null)
            {
                int index = contract.IndexOfKey(key);
                if (index >= 0)
                    trace.Label(contract.Members[index].Name);

                trace.Field(key, payloadLength, fieldStart);
            }

            var field = MemberReader.Field(reader.SliceField(key, payloadLength), contract, key);
            bool known = contract.ReadField(ref field, key, ref instance);

            if (known && !field.ValueRead)
                throw new BinaryTypeException(
                    $"The contract of '{typeof(T)}' accepted key {key} without reading its value.");

            if (!known && field.ValueRead)
                throw new BinaryTypeException(
                    $"The contract of '{typeof(T)}' read key {key} and then reported it unknown.");

            if (known && field.Remaining != 0)
                throw new BinaryFormatException(
                    $"Key {key} payload contains {field.Remaining} trailing byte(s) after " +
                    $"decoding '{contract.Members[contract.IndexOfKey(key)].Name}'.");

            trace?.EndField(known, reader.Position);
        }
    }
}
