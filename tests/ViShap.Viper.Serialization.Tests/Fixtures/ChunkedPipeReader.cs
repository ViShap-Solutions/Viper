using System.Buffers;
using System.IO.Pipelines;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A <see cref="PipeReader"/> over fixed content that arrives <paramref name="chunkSize"/> bytes at a
/// time, deterministically: a read returns what has arrived and not been consumed, as one segment per
/// chunk, and a new chunk arrives only once the reader has examined everything it was shown — the way
/// a real pipe makes <see cref="PipeReader.ReadAsync"/> wait for new data. When the content has all
/// arrived, the pipe completes, or with <paramref name="completeAtEnd"/> off it waits until the read
/// is cancelled.
/// </summary>
internal sealed class ChunkedPipeReader(byte[] content, int chunkSize, bool completeAtEnd = true) : PipeReader
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private int _consumed;
    private int _examined;
    private int _arrived;
    private bool _outstanding;
    private bool _cancelPending;
    private ReadOnlySequence<byte> _shown;

    /// <summary>How many bytes the reader has consumed.</summary>
    public int Consumed => _consumed;

    /// <summary>How many bytes have arrived so far.</summary>
    public int Arrived => _arrived;

    /// <summary>Whether the reader completed the pipe.</summary>
    public bool Completed { get; private set; }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_outstanding)
            throw new InvalidOperationException("ReadAsync was called again before AdvanceTo.");

        cancellationToken.ThrowIfCancellationRequested();

        if (_cancelPending)
        {
            _cancelPending = false;
            return ValueTask.FromResult(Show(isCanceled: true));
        }

        if (_examined >= _arrived)
        {
            if (_arrived == content.Length)
                return completeAtEnd
                    ? ValueTask.FromResult(Show(isCanceled: false))
                    : WaitUntilCancelled(cancellationToken);

            _arrived = Math.Min(content.Length, _arrived + chunkSize);
        }

        return ValueTask.FromResult(Show(isCanceled: false));
    }

    public override bool TryRead(out ReadResult result)
    {
        result = default;
        return false;
    }

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (!_outstanding)
            throw new InvalidOperationException("AdvanceTo was called without a read to advance.");

        int consumedLength = (int)_shown.Slice(_shown.Start, consumed).Length;
        int examinedLength = (int)_shown.Slice(_shown.Start, examined).Length;
        if (examinedLength < consumedLength)
            throw new InvalidOperationException("The examined position precedes the consumed one.");

        _examined = _consumed + examinedLength;
        _consumed += consumedLength;
        _outstanding = false;
    }

    public override void CancelPendingRead() => _cancelPending = true;

    public override void Complete(Exception? exception = null) => Completed = true;

    private ReadResult Show(bool isCanceled)
    {
        var segments = new List<byte[]>();
        for (int start = _consumed; start < _arrived;)
        {
            int end = Math.Min(_arrived, (start / chunkSize + 1) * chunkSize);
            segments.Add(content[start..end]);
            start = end;
        }

        _shown = Sequences.Of([.. segments]);
        _outstanding = true;
        return new ReadResult(_shown, isCanceled, isCompleted: completeAtEnd && _arrived == content.Length);
    }

    private static async ValueTask<ReadResult> WaitUntilCancelled(CancellationToken cancellationToken)
    {
        await Task.Delay(Patience, cancellationToken);
        throw new TimeoutException("The read waited for bytes that were never going to arrive.");
    }
}
