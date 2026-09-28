namespace ViShap.Viper.Engine;

/// <summary>A scalar value type: no null flag and no frame, only the formatter's encoding.</summary>
internal sealed class ScalarCodec<T>(IScalarFormatter<T> formatter) : Codec<T>
    where T : struct
{
    public override CodecShape Shape => CodecShape.Scalar;

    public override int MinimumWireSize => formatter.MinimumWireSize;

    public override void Write(ref WireWriter writer, T value) => formatter.Write(ref writer, value);

    public override T Read(ref WireReader reader) => formatter.Read(ref reader);
}

/// <summary>
/// A scalar reference type, such as a string: a null flag, then the formatter's encoding. Scalars are
/// never reference-framed, so the same string written twice travels twice.
/// </summary>
internal sealed class NullableScalarCodec<T>(IScalarFormatter<T> formatter) : Codec<T?>
    where T : class
{
    public override CodecShape Shape => CodecShape.Scalar;

    public override void Write(ref WireWriter writer, T? value)
    {
        writer.WriteBoolean(value is not null);
        if (value is not null)
            formatter.Write(ref writer, value);
    }

    public override T? Read(ref WireReader reader) =>
        reader.ReadBoolean() ? formatter.Read(ref reader) : null;
}

/// <summary><see cref="Nullable{T}"/>: a null flag, then the value as <typeparamref name="T"/> encodes it.</summary>
internal sealed class NullableCodec<T> : Codec<T?>
    where T : struct
{
    /// <summary>
    /// Read on use rather than kept: a type that contains itself resolves its own codec while this
    /// one is being built.
    /// </summary>
    private static Codec<T> ValueCodec => FormatterCache<T>.Instance;

    public override CodecShape Shape => ValueCodec.Shape;

    public override void Write(ref WireWriter writer, T? value)
    {
        writer.WriteBoolean(value.HasValue);
        if (value.HasValue)
            ValueCodec.Write(ref writer, value.GetValueOrDefault());
    }

    public override T? Read(ref WireReader reader) =>
        reader.ReadBoolean() ? ValueCodec.Read(ref reader) : null;
}

/// <summary>
/// A type that has no representation on the wire, such as a delegate, which carries behaviour rather
/// than data. A null value is written as null; any other value is refused.
/// </summary>
internal sealed class RejectedCodec<T>(string writeMessage, string readMessage) : Codec<T?>
    where T : class
{
    public override CodecShape Shape => CodecShape.Scalar;

    public override void Write(ref WireWriter writer, T? value)
    {
        writer.WriteBoolean(value is not null);
        if (value is not null)
            throw new BinaryTypeException(writeMessage);
    }

    public override T? Read(ref WireReader reader) =>
        reader.ReadBoolean() ? throw new BinaryTypeException(readMessage) : null;
}

/// <summary>
/// A type the engine could not build a codec for. Every use raises the same
/// <see cref="BinaryTypeException"/>, so a failure is reported where the type is used rather than
/// where it was first resolved.
/// </summary>
internal sealed class UnsupportedCodec<T>(string message) : Codec<T>
{
    public override CodecShape Shape => CodecShape.Object;

    public override void Write(ref WireWriter writer, T value) => throw new BinaryTypeException(message);

    public override T Read(ref WireReader reader) => throw new BinaryTypeException(message);
}
