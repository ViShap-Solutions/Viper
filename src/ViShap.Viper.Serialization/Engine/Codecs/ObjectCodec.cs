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
/// <para>
/// The contract of a type is the one the operation's configuration supplies, when it supplies one,
/// and otherwise the one built by reflection, which the codec keeps.
/// </para>
/// </summary>
internal sealed class ObjectCodec<T> : StructuralCodec<T>
{
    private static readonly bool IsReference = !typeof(T).IsValueType;

    private TypeContract<T>? _reflected;
    private ContractChoice? _chosen;
    private UnionLookup? _union;

    /// <summary>A codec that reads the declared type's union map from its attributes on first use.</summary>
    public ObjectCodec()
    {
    }

    /// <summary>A codec given its union map — <see langword="null"/> for none — instead of reading it.</summary>
    public ObjectCodec(UnionMap? union) => _union = new UnionLookup(union);

    public override CodecShape Shape => CodecShape.Object;

    protected override bool FoldsNull(ref OperationState state) =>
        Union is null && Contract(ref state).Layout == MemberLayout.Keyed;

    /// <summary>
    /// The contract of the declared type: the configuration's, or the one built by reflection.
    /// The contract chosen for the last configuration met is kept with that configuration's
    /// contract set, so a run of operations under one configuration looks the type up once.
    /// Building one can fail — a delegate member, a duplicate key, a type the configuration requires
    /// and lacks — and a failure is not kept, so every use reports it the same way.
    /// </summary>
    private TypeContract<T> Contract(ref OperationState state)
    {
        if (state.Contracts is not { } contracts)
            return Reflected();

        if (_chosen is { } chosen && ReferenceEquals(chosen.Contracts, contracts))
            return chosen.Contract;

        var contract = contracts.Find<T>()
            ?? (contracts.RequireAll ? throw contracts.Missing(typeof(T)) : Reflected());

        _chosen = new ContractChoice(contracts, contract);
        return contract;
    }

    /// <summary>
    /// The contract built by reflection, kept once built; with the reflection path off, the type has
    /// no contract.
    /// </summary>
    private TypeContract<T> Reflected() =>
        _reflected ??= ReflectionPath.IsEnabled ? TypeContractCache.Get<T>() : throw ReflectionPath.NoContract(typeof(T));

    /// <summary>The contract of a runtime type met in the polymorphic slot, found the same way.</summary>
    private static ITypeContract RuntimeContract(Type runtimeType, ref OperationState state)
    {
        if (state.Contracts is { } contracts)
        {
            if (contracts.Find(runtimeType) is { } supplied)
                return supplied;

            if (contracts.RequireAll)
                throw contracts.Missing(runtimeType);
        }

        return ReflectionPath.IsEnabled ? TypeContractCache.Get(runtimeType) : throw ReflectionPath.NoContract(runtimeType);
    }

    /// <summary>Writes the members of a value held on the ancestor path while they are written.</summary>
    private static void WriteOnPath(ref WireWriter writer, TypeContract<T> contract, in T value, bool nullFolded)
    {
        writer.State.Graph.PushAncestor(value!);
        try
        {
            ObjectMembers.Write(ref writer, contract, in value, nullFolded);
        }
        finally
        {
            writer.State.Graph.PopAncestor();
        }
    }

    /// <summary>Writes the members of a value of another runtime type, held on the ancestor path while they are written.</summary>
    private static void WriteBoxedOnPath(ref WireWriter writer, ITypeContract contract, object value)
    {
        writer.State.Graph.PushAncestor(value);
        try
        {
            var boxed = MemberWriter.Begin(ref writer, contract, nullFolded: false);
            contract.WriteBoxed(ref boxed, value);
            boxed.End(ref writer);
        }
        finally
        {
            writer.State.Graph.PopAncestor();
        }
    }

    /// <summary>
    /// Whether the value is kept on the path while its members are written: a reference-framed value of a
    /// type read through a constructor, which a back reference from its own members could never reach.
    /// </summary>
    private static bool Opens(ref OperationState state, ITypeContract contract) =>
        IsReference && state.Graph.Written is not null && contract.Constructs;

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
            var contract = Contract(ref writer.State);
            if (Opens(ref writer.State, contract))
                WriteOnPath(ref writer, contract, in value, nullFolded);
            else
                ObjectMembers.Write(ref writer, contract, in value, nullFolded);

            return;
        }

        var runtimeContract = RuntimeContract(runtimeType, ref writer.State);
        if (Opens(ref writer.State, runtimeContract))
        {
            WriteBoxedOnPath(ref writer, runtimeContract, value!);
            return;
        }

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
                return (T)RuntimeContract(runtimeType, ref reader.State).ReadBoxed(ref reader, referenceId, target: null);
        }

        return ObjectMembers.ReadInstance(ref reader, Contract(ref reader.State), referenceId, target: null, nullFolded);
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
        else if (FoldsNull(ref reader.State))
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
            ObjectMembers.ReadInstance(ref reader, Contract(ref reader.State), referenceId, target, nullFolded);
        else
            RuntimeContract(instanceType, ref reader.State).ReadBoxed(ref reader, referenceId, target);
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

    /// <summary>
    /// A contract set and the contract it gives the declared type, published together by one
    /// reference write, so a thread reads either the whole pair or the one it replaced.
    /// </summary>
    private sealed class ContractChoice(ContractSet contracts, TypeContract<T> contract)
    {
        public ContractSet Contracts { get; } = contracts;

        public TypeContract<T> Contract { get; } = contract;
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
        if (contract.Accessors is { } accessors)
        {
            WriteTable(ref writer, contract, accessors, in value, nullFolded);
            return;
        }

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
        if (contract.Construction is { } construction)
        {
            if (target is not null)
                throw new BinaryTypeException(
                    $"'{typeof(T)}' is read through a constructor with parameters, which cannot fill an existing " +
                    "instance. Use a Deserialize overload instead.");

            if (referenceId >= 0)
                reader.State.Graph.Read!.Register(referenceId, ReadReferenceTable.Pending);

            var constructed = construction.Read(ref reader, contract, nullFolded);

            if (referenceId >= 0)
                reader.State.Graph.Read!.Replace(referenceId, constructed!);

            return constructed;
        }

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
        if (!typeof(T).IsValueType || contract.Construction is not null)
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

    /// <summary>
    /// The new instance the contract creates. An exception from the contract's own code propagates
    /// unchanged; an instance that is <see langword="null"/> is refused.
    /// </summary>
    private static T Create<T>(TypeContract<T> contract)
    {
        if (!contract.CanBeConstructed)
            throw new BinaryTypeException(
                $"'{typeof(T)}' cannot be constructed during deserialization — a concrete type " +
                "needs a parameterless constructor of any visibility, a single public constructor whose " +
                "parameters name its members, or one marked [BinaryConstructor]. An interface or an " +
                "abstract class needs a [BinaryUnion] map naming the type to build.");

        var instance = contract.Create();
        if (!typeof(T).IsValueType && instance is null)
            throw new BinaryTypeException(
                $"The contract of '{typeof(T)}' created null instead of an instance to read the members into.");

        return instance;
    }

    private static void ReadMembers<T>(ref WireReader reader, TypeContract<T> contract, ref T instance, bool nullFolded)
    {
        if (contract.Layout == MemberLayout.Positional)
            ReadPositional(ref reader, contract, ref instance);
        else
            ReadKeyed(ref reader, contract, ref instance, nullFolded);
    }

    /// <summary>
    /// The members of a positional value. Kept apart from the keyed loop so that a nested positional
    /// graph, which recurses through here, carries none of that loop's state on every level of the stack.
    /// </summary>
    /// <summary>
    /// Writes the members through the contract's accessor table: in order for a positional layout, and
    /// for a keyed one each as a field — key, length, payload — under the same accounting and reference
    /// scope as <c>MemberWriter</c>.
    /// </summary>
    private static void WriteTable<T>(ref WireWriter writer, TypeContract<T> contract, Accessor<T>[] accessors, in T value, bool nullFolded)
    {
        if (contract.Layout == MemberLayout.Positional)
        {
            foreach (var accessor in accessors)
                accessor.Write(ref writer, in value);

            return;
        }

        ITypeContract description = contract;
        MemberWriter.WriteFieldCount(ref writer, description, nullFolded);

        var members = description.Members;
        for (int index = 0; index < accessors.Length; index++)
        {
            long lengthPosition = MemberWriter.BeginField(ref writer, members[index].Key!.Value);

            if (writer.State.Graph.Written is { } references)
            {
                using var scope = references.Enter();
                accessors[index].Write(ref writer, in value);
            }
            else
            {
                accessors[index].Write(ref writer, in value);
            }

            MemberWriter.EndField(ref writer, members[index], lengthPosition);
        }
    }

    private static void ReadPositional<T>(ref WireReader reader, TypeContract<T> contract, ref T instance)
    {
        if (contract.Accessors is { } accessors && reader.State.Trace is null)
        {
            foreach (var accessor in accessors)
                accessor.Read(ref reader, ref instance);

            return;
        }

        reader.State.Trace?.Shape(TraceShape.Object, contract.Members.Count);
        var members = MemberReader.Positional(ref reader, contract);
        contract.ReadPositional(ref members, ref instance);
        members.End(ref reader);
    }

    private static void ReadKeyed<T>(ref WireReader reader, TypeContract<T> contract, ref T instance, bool nullFolded)
    {
        var trace = reader.State.Trace;
        var accessors = trace is null ? contract.Accessors : null;
        ITypeContract description = contract;

        var fieldCount = reader.ReadCount(CountKind.KeyedFields, "Keyed field count", nullFolded);
        trace?.Shape(TraceShape.KeyedObject, fieldCount);

        int previousKey = -1;
        for (int i = 0; i < fieldCount; i++)
        {
            int key = ReadFieldHeader(ref reader, description, ref previousKey, out int payloadLength);

            if (accessors is not null)
            {
                ReadField(reader.SliceField(key, payloadLength), description, accessors, key, ref instance, traced: false);
                continue;
            }

            var field = MemberReader.Field(reader.SliceField(key, payloadLength), contract, key);
            bool known = contract.ReadKeyed(ref field, key, ref instance);
            field.EndField();

            if (known && !field.ValueRead)
                throw new BinaryTypeException(
                    $"The contract of '{typeof(T)}' accepted key {key} without reading its value.");

            if (!known && field.ValueRead)
                throw new BinaryTypeException(
                    $"The contract of '{typeof(T)}' read key {key} and then reported it unknown.");

            if (known && field.Remaining != 0)
                throw new BinaryFormatException(
                    $"Key {key} payload contains {field.Remaining} trailing byte(s) after " +
                    $"decoding '{description.Members[description.IndexOfKey(key)].Name}'.");

            trace?.EndField(known, reader.Position);
        }
    }

    /// <summary>
    /// Reads one keyed field through the accessor of its key, in the field's own window and reference
    /// scope, and requires the value to fill the field; a key the description does not hold is skipped.
    /// </summary>
    private static bool ReadField<T>(WireReader field, ITypeContract description, Accessor<T>[] accessors, int key, ref T instance, bool traced)
    {
        int index = description.IndexOfKey(key);
        if (index < 0)
            return false;

        if (field.State.Graph.Read is { } references)
        {
            using var scope = references.Enter();
            ReadEntry(ref field, accessors[index], ref instance, traced);
        }
        else
        {
            ReadEntry(ref field, accessors[index], ref instance, traced);
        }

        if (field.Remaining != 0)
            throw new BinaryFormatException(
                $"Key {key} payload contains {field.Remaining} trailing byte(s) after " +
                $"decoding '{description.Members[index].Name}'.");

        return true;
    }

    private static void ReadEntry<T>(ref WireReader reader, Accessor<T> accessor, ref T target, bool traced)
    {
        if (traced)
            accessor.ReadTraced(ref reader, ref target);
        else
            accessor.Read(ref reader, ref target);
    }

    /// <summary>
    /// Reads a keyed field's key and length: keys strictly ascending, the length within the payload
    /// limit, and the field announced to a trace.
    /// </summary>
    private static int ReadFieldHeader(ref WireReader reader, ITypeContract description, ref int previousKey, out int payloadLength)
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

        ref var state = ref reader.State;
        payloadLength = reader.ReadInt32();
        if (!state.Phases.AdmitsPayload(payloadLength))
            state.Phases.CheckPayload(payloadLength, $"Key {key} payload length");

        if (state.Trace is { } trace)
        {
            int index = description.IndexOfKey(key);
            if (index >= 0)
                trace.Label(description.Members[index].Name);

            trace.Field(key, payloadLength, fieldStart);
        }

        return key;
    }

    /// <summary>
    /// Reads the members of a value built through a constructor into <paramref name="target"/>, the
    /// values the constructor is then called with, through the values' accessor table: in order under a
    /// positional layout, field by field under a keyed one, a field the description does not hold skipped.
    /// </summary>
    internal static void ReadValues<TValues>(
        ref WireReader reader,
        ITypeContract description,
        Accessor<TValues>[] values,
        ref TValues target,
        bool nullFolded)
    {
        var trace = reader.State.Trace;

        if (description.Layout == MemberLayout.Positional)
        {
            trace?.Shape(TraceShape.Object, values.Length);
            for (int index = 0; index < values.Length; index++)
            {
                trace?.Label(description.Members[index].Name);
                ReadEntry(ref reader, values[index], ref target, traced: trace is not null);
            }

            return;
        }

        var fieldCount = reader.ReadCount(CountKind.KeyedFields, "Keyed field count", nullFolded);
        trace?.Shape(TraceShape.KeyedObject, fieldCount);

        int previousKey = -1;
        for (int i = 0; i < fieldCount; i++)
        {
            int key = ReadFieldHeader(ref reader, description, ref previousKey, out int payloadLength);
            bool known = ReadField(reader.SliceField(key, payloadLength), description, values, key, ref target, traced: trace is not null);
            trace?.EndField(known, reader.Position);
        }
    }
}
