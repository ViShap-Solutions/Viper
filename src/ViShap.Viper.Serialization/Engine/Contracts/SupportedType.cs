namespace ViShap.Viper.Contracts;

/// <summary>
/// A supported type closed over the types of an application — <c>List&lt;Order&gt;</c>,
/// <c>Dictionary&lt;string, Line&gt;</c>, an enum, a member-encoded class — as
/// <see cref="SupportedTypes"/> creates it. A <see cref="BinarySerializerContext"/> adds it, so the
/// serializer encodes the type without building anything for it by reflection.
/// </summary>
/// <remarks>
/// The value is opaque: it names its type and carries how the serializer encodes it, which only the
/// serializer reads.
/// </remarks>
public sealed class SupportedType
{
    private readonly Action _offer;

    internal SupportedType(Type type, object codec, Action offer)
    {
        Type = type;
        Codec = codec;
        _offer = offer;
    }

    /// <summary>The declared type this value makes supported.</summary>
    public Type Type { get; }

    /// <summary>The engine's codec of the type.</summary>
    internal object Codec { get; }

    /// <summary>Makes the type's encoding the one the serializer uses for the type in this process.</summary>
    internal void Offer() => _offer();
}
