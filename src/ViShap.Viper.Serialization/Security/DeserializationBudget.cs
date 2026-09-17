namespace ViShap.Viper.Security;

internal sealed class DeserializationBudget
{
    private readonly DeserializationLimits _limits;

    private long _totalElements;
    private int _depth;

    public DeserializationBudget(DeserializationLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    internal DeserializationLimits Limits => _limits;

    internal int Depth => _depth;

    public void ConsumeElements(long count)
    {
        if (count < 0)
            throw new BinaryFormatException(
                $"Element count {count} must be non-negative.");

        if (count > _limits.MaxTotalElements - _totalElements)
            throw new BinaryLimitException(
                $"Cumulative element count across the payload exceeds the configured limit of {_limits.MaxTotalElements}.");

        _totalElements += count;
    }

    public void ConsumeBytes(long count)
    {
        if (count < 0)
        {
            throw new BinaryFormatException(
                $"Byte count {count} must be non-negative.");
        }

        if (count > _limits.MaxMessageBytes)
        {
            throw new BinaryLimitException(
                $"Byte count {count} exceeds the configured limit of {_limits.MaxMessageBytes}.");
        }
    }

    public IDisposable EnterDepth()
    {
        if (++_depth > _limits.MaxDepth)
        {
            _depth--;
            throw new BinaryLimitException(
                $"Nesting depth exceeds the configured limit of {_limits.MaxDepth}.");
        }

        return new DepthScope(this);
    }

    private void ExitDepth()
    {
        if (_depth > 0)
            _depth--;
    }

    private sealed class DepthScope(DeserializationBudget owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.ExitDepth();
        }
    }
}