using System.Text;

namespace ViShap.Viper.Codec;

internal sealed class V0FormatCodec : IFormatCodec
{
    private const bool SupportsKeyedContracts = false;
    private readonly SerializationLimits _limits;

    public int Version => 0;

    public V0FormatCodec(SerializationLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _limits.Validate();
    }

    public void Serialize<T>(Stream destination, T data)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var wire = new BudgetedWriteStream(destination, _limits.MaxWireBytes, "wire", leaveOpen: true);
        using var payload = new BudgetedWriteStream(wire, _limits.MaxPayloadBytes, "payload", leaveOpen: true);
        using var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);

        new BinaryPayloadWriter(
                writer,
                preserveReferences: false,
                limits: _limits,
                keyedContracts: SupportsKeyedContracts)
            .Serialize(data);

        writer.Flush();
        payload.Flush();
        wire.Flush();
    }

    public T? Deserialize<T>(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var wire = new BudgetedReadStream(source, _limits.MaxWireBytes, "wire", leaveOpen: true);
        using var reader = new BinaryReader(wire, Encoding.UTF8, leaveOpen: true);

        return new BinaryPayloadReader(
                reader,
                preserveReferences: false,
                limits: _limits,
                keyedContracts: SupportsKeyedContracts)
            .Deserialize<T>();
    }

    public T? Deserialize<T>(Stream source, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);

        using var wire = new BudgetedReadStream(source, _limits.MaxWireBytes, "wire", leaveOpen: true);
        using var reader = new BinaryReader(wire, Encoding.UTF8, leaveOpen: true);

        return new BinaryPayloadReader(
                reader,
                preserveReferences: false,
                limits: _limits,
                keyedContracts: SupportsKeyedContracts)
            .Deserialize(existingInstance);
    }

    public void Deserialize<T>(Stream source, ref T existingInstance) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);

        using var wire = new BudgetedReadStream(source, _limits.MaxWireBytes, "wire", leaveOpen: true);
        using var reader = new BinaryReader(wire, Encoding.UTF8, leaveOpen: true);

        new BinaryPayloadReader(
                reader,
                preserveReferences: false,
                limits: _limits,
                keyedContracts: SupportsKeyedContracts)
            .Deserialize(ref existingInstance);
    }
}