using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// The state of one operation, held on the heap: a test hands <see cref="State"/> to a reader or a
/// writer by reference, captures the box in a lambda, and inspects the budget afterwards.
/// </summary>
internal sealed class OperationBox(SerializationLimits? limits = null)
{
    public OperationState State = new(
        limits ?? SerializationLimits.Default,
        keys: null,
        preserveReferences: false,
        requireEncryption: false,
        requireChecksum: false);
}
