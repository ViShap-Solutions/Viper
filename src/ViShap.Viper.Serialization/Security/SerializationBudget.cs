namespace ViShap.Viper.Security;

internal sealed class SerializationBudget
{
    private readonly SerializationLimits _limits;
    private long _totalElements;
    private long _objectGraphNodes;
    private int _depth;

    public SerializationBudget(SerializationLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _limits.Validate();
    }

    internal SerializationLimits Limits => _limits;
    internal int Depth => _depth;

    public void ConsumeElements(long count)
    {
        if (count < 0)
            throw new BinaryFormatException($"Element count {count} must be non-negative.");

        if (count > _limits.MaxTotalElements - _totalElements)
            throw new BinaryLimitException(
                $"Cumulative element count across the payload exceeds the configured limit of {_limits.MaxTotalElements}.");

        _totalElements += count;
    }

    public void ConsumeObjectGraphNodes(long count)
    {
        if (count < 0)
            throw new BinaryFormatException($"Object graph node count {count} must be non-negative.");

        if (count > _limits.MaxObjectGraphNodes - _objectGraphNodes)
            throw new BinaryLimitException(
                $"Object graph node count exceeds the configured limit of {_limits.MaxObjectGraphNodes}.");

        _objectGraphNodes += count;
    }

    public IDisposable EnterDepth()
    {
        if (_depth >= _limits.MaxDepth)
            throw new BinaryLimitException(
                $"Nesting depth exceeds the configured limit of {_limits.MaxDepth}.");

        _depth++;
        return new DepthScope(this);
    }

    private void ExitDepth()
    {
        if (_depth > 0)
            _depth--;
    }

    private sealed class DepthScope(SerializationBudget owner) : IDisposable
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
