namespace ViShap.Viper.Security;

/// <summary>
/// Everything one public serialize/deserialize call is allowed to consume, created exactly once by
/// <see cref="BinarySerializer"/> and handed down the pipeline by reference. No layer below
/// constructs limits, a budget or keys of its own, which is what makes "every phase of this
/// operation is accounted for" a checkable statement rather than a convention.
/// <para>
/// The state is a <see langword="struct"/> passed by <see langword="ref"/>: a copy would account
/// for the operation twice. <see cref="WireReader"/> and <see cref="WireWriter"/> hold a reference
/// to it, so every count, depth scope and node charged through them lands in the one budget of the
/// call.
/// </para>
/// </summary>
internal struct OperationState
{
    public OperationState(
        SerializationLimits limits,
        IKeyProvider? keys,
        bool preserveReferences,
        bool requireEncryption,
        bool requireChecksum)
    {
        Limits = limits;
        Budget = new SerializationBudget(limits);
        Phases = new PhaseBudget(limits);
        Keys = keys;
        PreserveReferences = preserveReferences;
        RequireEncryption = requireEncryption;
        RequireChecksum = requireChecksum;
        Graph = default;
    }

    /// <summary>The validated configuration snapshot the operation runs under.</summary>
    public readonly SerializationLimits Limits;

    /// <summary>What the operation has consumed so far.</summary>
    public SerializationBudget Budget;

    /// <summary>The payload, compressed and encrypted size policy.</summary>
    public readonly PhaseBudget Phases;

    public readonly IKeyProvider? Keys;

    /// <summary>Whether this serializer writes reference framing.</summary>
    public readonly bool PreserveReferences;

    public readonly bool RequireEncryption;

    public readonly bool RequireChecksum;

    /// <summary>
    /// The traversal of the payload being written or read: its reference framing, its reference
    /// tables and the ancestor stack. The pipeline opens it for one payload and closes it after.
    /// </summary>
    public GraphState Graph;

    /// <summary>
    /// The observer of a read, which only the diagnostics set; <see langword="null"/> for every
    /// serializer call. The engine's codecs report to it and change nothing a read does.
    /// </summary>
    public IWireTrace? Trace;

    public readonly IKeyProvider RequireKeys() =>
        Keys ?? throw new BinaryEncryptionKeyException(
            "The payload is encrypted, but no key material is configured for this serializer.");
}
