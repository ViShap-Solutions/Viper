namespace ViShap.Viper;

/// <summary>
/// The statement that <typeparamref name="T"/> and every type it reaches are described by a
/// <see cref="BinarySerializerContext"/>: their contracts and their encodings come from it, so a
/// serializer method given this value builds nothing for them by reflection. A generated context
/// exposes one per type it lists — <c>AppContracts.Default.Order</c>.
/// </summary>
/// <typeparam name="T">The declared type a value is written and read as.</typeparam>
/// <remarks>
/// The methods of <see cref="BinarySerializer"/> and <see cref="Diagnostics.BinaryFormatDumper"/> that
/// take one carry no trimming or native AOT requirement. The serializer's options must have been built
/// with the same context, <see cref="BinarySerializerOptionsBuilder.WithContracts"/>.
/// </remarks>
public sealed class BinaryTypeInfo<T>
{
    internal BinaryTypeInfo(BinarySerializerContext context) => Context = context;

    /// <summary>The context that describes <typeparamref name="T"/>.</summary>
    public BinarySerializerContext Context { get; }

    /// <summary>The declared type, <typeparamref name="T"/>.</summary>
    public Type Type => typeof(T);

    /// <summary>
    /// Refuses this value for options built with another context, or with none, before the operation
    /// begins.
    /// </summary>
    internal void Require(BinarySerializerOptions options)
    {
        if (!ReferenceEquals(options.Contracts, Context))
            throw new BinaryConfigurationException(
                $"The type information of '{typeof(T)}' belongs to '{Context.GetType()}', but the serializer's " +
                $"options were built with {(options.Contracts is null ? "no context" : $"'{options.Contracts.GetType()}'")}. " +
                "Build the options with WithContracts and the context instance the type information came from.");
    }
}
