using System.Buffers;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Builds a <see cref="ReadOnlySequence{T}"/> out of several segments, which is the only way a test
/// can hand the serializer a sequence that is not one contiguous block.
/// </summary>
internal static class Sequences
{
    /// <summary>Chains <paramref name="segments"/> into one sequence, in the order given.</summary>
    public static ReadOnlySequence<T> Of<T>(params T[][] segments)
    {
        if (segments.Length == 0)
            return ReadOnlySequence<T>.Empty;

        var first = new Segment<T>(segments[0], runningIndex: 0, previous: null);
        var last = first;
        for (int i = 1; i < segments.Length; i++)
            last = new Segment<T>(segments[i], last.RunningIndex + segments[i - 1].Length, last);

        return new ReadOnlySequence<T>(first, 0, last, segments[^1].Length);
    }

    private sealed class Segment<T> : ReadOnlySequenceSegment<T>
    {
        public Segment(T[] items, long runningIndex, Segment<T>? previous)
        {
            Memory = items;
            RunningIndex = runningIndex;

            if (previous is not null)
                previous.Next = this;
        }
    }
}
