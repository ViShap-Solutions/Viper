namespace ViShap.Viper.Serialization.Tests.Hostile.Fuzz;

/// <summary>
/// Seeded edits of a valid frame: a flipped bit, a byte set to a boundary value, bytes inserted,
/// deleted or repeated, a varint written at a boundary, a splice with another frame, a truncation.
/// The same seed gives the same sequence of inputs on every machine.
/// </summary>
internal sealed class FrameMutator(int seed)
{
    private static readonly byte[] Boundaries = [0x00, 0x01, 0x02, 0x7F, 0x80, 0x81, 0xFE, 0xFF];

    private static readonly int[] VarintBoundaries =
        [0, 1, 2, 127, 128, 255, 256, 16_383, 16_384, 1_000_000, 2_097_151, 2_097_152, int.MaxValue - 1, int.MaxValue];

    private readonly Random _random = new(seed);

    /// <summary>One to four edits of <paramref name="frame"/>; <paramref name="other"/> is what a splice takes its tail from.</summary>
    public byte[] Mutate(byte[] frame, byte[] other)
    {
        var bytes = new List<byte>(frame);
        int edits = 1 + _random.Next(4);

        for (int edit = 0; edit < edits; edit++)
            Apply(bytes, other);

        return [.. bytes];
    }

    private void Apply(List<byte> bytes, byte[] other)
    {
        if (bytes.Count == 0)
        {
            bytes.Add((byte)_random.Next(256));
            return;
        }

        int at = _random.Next(bytes.Count);
        switch (_random.Next(10))
        {
            case 0:
                bytes[at] ^= (byte)(1 << _random.Next(8));
                break;

            case 1:
                bytes[at] = Boundaries[_random.Next(Boundaries.Length)];
                break;

            case 2:
                bytes[at] = (byte)(bytes[at] + (_random.Next(2) == 0 ? 1 : -1));
                break;

            case 3:
                for (int count = 1 + _random.Next(8); count > 0; count--)
                    bytes.Insert(at, (byte)_random.Next(256));
                break;

            case 4:
                bytes.RemoveRange(at, Math.Min(1 + _random.Next(8), bytes.Count - at));
                break;

            case 5:
            {
                int length = Math.Min(1 + _random.Next(16), bytes.Count - at);
                bytes.InsertRange(at, bytes.GetRange(at, length));
                break;
            }

            case 6:
            {
                byte[] varint = Varint(VarintBoundaries[_random.Next(VarintBoundaries.Length)]);
                for (int i = 0; i < varint.Length && at + i < bytes.Count; i++)
                    bytes[at + i] = varint[i];
                break;
            }

            case 7:
                if (other.Length > 0)
                {
                    int from = _random.Next(other.Length);
                    bytes.RemoveRange(at, bytes.Count - at);
                    bytes.AddRange(other.AsSpan(from).ToArray());
                }

                break;

            case 8:
                bytes.RemoveRange(at, bytes.Count - at);
                break;

            default:
            {
                int to = _random.Next(bytes.Count);
                (bytes[at], bytes[to]) = (bytes[to], bytes[at]);
                break;
            }
        }
    }

    private static byte[] Varint(int value)
    {
        var bytes = new List<byte>(5);
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            bytes.Add((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        bytes.Add((byte)remaining);
        return [.. bytes];
    }
}
