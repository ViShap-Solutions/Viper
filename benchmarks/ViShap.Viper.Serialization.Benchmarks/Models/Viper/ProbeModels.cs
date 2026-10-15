namespace ViShap.Viper.Serialization.Benchmarks.Models.Viper;

// The models the capability probes of the plan's §7.5 write and read.

internal sealed class ProbeLeaf
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class ProbePair
{
    public ProbeLeaf? Left { get; set; }

    public ProbeLeaf? Right { get; set; }
}

internal sealed class ProbeCycle
{
    public string Name { get; set; } = string.Empty;

    public ProbeCycle? Next { get; set; }
}

internal sealed class ProbeDeep
{
    public int Level { get; set; }

    public ProbeDeep? Next { get; set; }
}

[BinaryContract]
internal sealed class ProbeRecord
{
    [BinaryKey(1)]
    public int A { get; set; }

    [BinaryKey(2)]
    public string B { get; set; } = string.Empty;

    [BinaryKey(3)]
    public double C { get; set; }
}

[BinaryContract]
internal sealed class ProbeRecordAdded
{
    [BinaryKey(1)]
    public int A { get; set; }

    [BinaryKey(2)]
    public string B { get; set; } = string.Empty;

    [BinaryKey(3)]
    public double C { get; set; }

    [BinaryKey(4)]
    public long D { get; set; }
}

[BinaryContract]
internal sealed class ProbeRecordRemoved
{
    [BinaryKey(1)]
    public int A { get; set; }

    [BinaryKey(3)]
    public double C { get; set; }
}

[BinaryContract]
internal sealed class ProbeRecordReordered
{
    [BinaryKey(3)]
    public double C { get; set; }

    [BinaryKey(1)]
    public int A { get; set; }

    [BinaryKey(2)]
    public string B { get; set; } = string.Empty;
}

[BinaryUnion(1, typeof(ProbeCircle))]
[BinaryUnion(2, typeof(ProbeSquare))]
internal abstract class ProbeShape
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class ProbeCircle : ProbeShape
{
    public double Radius { get; set; }
}

internal sealed class ProbeSquare : ProbeShape
{
    public double Side { get; set; }
}
