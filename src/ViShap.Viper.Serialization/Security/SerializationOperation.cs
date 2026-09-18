namespace ViShap.Viper.Security;

/// <summary>
/// Everything one public serialize/deserialize call is allowed to consume, created exactly once by
/// <see cref="BinarySerializer"/> and handed down the pipeline. No layer below constructs limits or
/// a budget of its own, which is what makes "every phase of this operation is accounted for" a
/// checkable statement rather than a convention.
/// </summary>
internal sealed class SerializationOperation
{
    public SerializationOperation(
        SerializationLimits limits,
        IKeyProvider? keys,
        bool preserveReferences,
        bool requireEncryption,
        bool requireChecksum,
        bool enableTrace = false)
    {
        Limits = limits;
        Budget = new SerializationBudget(limits);
        Phases = new PhaseBudget(limits);
        Keys = keys;
        PreserveReferences = preserveReferences;
        RequireEncryption = requireEncryption;
        RequireChecksum = requireChecksum;
        EnableTrace = enableTrace;
    }

    private SerializationOperation(SerializationOperation source, bool preserveReferences)
    {
        Limits = source.Limits;
        Budget = source.Budget;
        Phases = source.Phases;
        Keys = source.Keys;
        PreserveReferences = preserveReferences;
        RequireEncryption = source.RequireEncryption;
        RequireChecksum = source.RequireChecksum;
        EnableTrace = source.EnableTrace;
    }

    /// <summary>
    /// The same operation — same budget, same phase policy — reading a payload whose header declares
    /// a different reference mode. Accounting stays cumulative because the budget object is shared.
    /// </summary>
    public SerializationOperation WithPreserveReferences(bool preserveReferences) =>
        preserveReferences == PreserveReferences
            ? this
            : new SerializationOperation(this, preserveReferences);

    public SerializationLimits Limits { get; }
    public SerializationBudget Budget { get; }
    public PhaseBudget Phases { get; }
    public IKeyProvider? Keys { get; }
    public bool PreserveReferences { get; }
    public bool RequireEncryption { get; }
    public bool RequireChecksum { get; }
    public bool EnableTrace { get; }

    public IKeyProvider RequireKeys() =>
        Keys ?? throw new BinaryEncryptionKeyException(
            "The payload is encrypted, but no key material is configured for this serializer.");
}
