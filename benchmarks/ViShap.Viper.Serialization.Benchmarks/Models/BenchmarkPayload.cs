using MemoryPack;
using MessagePack;
using ProtoBuf;
using ZeroFormatter;
using Orleans;

namespace ViShap.Viper.Serialization.Benchmarks.Models;

[MemoryPackable]
[MessagePackObject]
[ProtoContract]
[ZeroFormattable]
[GenerateSerializer]
public partial class BenchmarkPayload
{
    [MemoryPackOrder(0)] [Key(0)] [ProtoMember(1)] [Index(0)] [Id(0)] public virtual int Id { get; set; }
    [MemoryPackOrder(1)] [Key(1)] [ProtoMember(2)] [Index(1)] [Id(1)] public virtual string Name { get; set; } = "";
    [MemoryPackOrder(2)] [Key(2)] [ProtoMember(3)] [Index(2)] [Id(2)] public virtual Guid CorrelationId { get; set; }
    [MemoryPackOrder(3)] [Key(3)] [ProtoMember(4)] [Index(3)] [Id(3)] public virtual DateTime Timestamp { get; set; }
    [MemoryPackOrder(4)] [Key(4)] [ProtoMember(5)] [Index(4)] [Id(4)] public virtual List<BenchmarkItem> Items { get; set; } = [];
    [MemoryPackOrder(5)] [Key(5)] [ProtoMember(6)] [Index(5)] [Id(5)] public virtual Dictionary<string,BenchmarkItem> Map { get; set; } = new();
    [MemoryPackOrder(6)] [Key(6)] [ProtoMember(7)] [Index(6)] [Id(6)] public virtual BenchmarkNode? Root { get; set; }
    [MemoryPackOrder(7)] [Key(7)] [ProtoMember(8)] [Index(7)] [Id(7)] public virtual BenchmarkItem? SharedA { get; set; }
    [MemoryPackOrder(8)] [Key(8)] [ProtoMember(9)] [Index(8)] [Id(8)] public virtual BenchmarkItem? SharedB { get; set; }
    [MemoryPackOrder(9)] [Key(9)] [ProtoMember(10)] [Index(9)] [Id(9)] public virtual List<BenchmarkScalar> Scalars { get; set; } = [];
}

[MemoryPackable]
[MessagePackObject]
[ProtoContract]
[ZeroFormattable]
[GenerateSerializer]
public partial class BenchmarkItem
{
    [MemoryPackOrder(0)] [Key(0)] [ProtoMember(1)] [Index(0)] [Id(0)] public virtual int Id { get; set; }
    [MemoryPackOrder(1)] [Key(1)] [ProtoMember(2)] [Index(1)] [Id(1)] public virtual string Value { get; set; } = "";
    [MemoryPackOrder(2)] [Key(2)] [ProtoMember(3)] [Index(2)] [Id(2)] public virtual double Score { get; set; }
}


[MemoryPackable]
[MessagePackObject]
[ProtoContract]
[ZeroFormattable]
[GenerateSerializer]
public partial class BenchmarkNode
{
    [MemoryPackOrder(0)] [Key(0)] [ProtoMember(1)] [Index(0)] [Id(0)] public virtual int Value { get; set; }
    [MemoryPackOrder(1)] [Key(1)] [ProtoMember(2)] [Index(1)] [Id(1)] public virtual BenchmarkNode? Next { get; set; }
}


[MemoryPackable]
[MessagePackObject]
[ProtoContract]
[ZeroFormattable]
[GenerateSerializer]
public partial class BenchmarkScalar
{
    [MemoryPackOrder(0)] [Key(0)] [ProtoMember(1)] [Index(0)] [Id(0)] public virtual int A { get; set; }
    [MemoryPackOrder(1)] [Key(1)] [ProtoMember(2)] [Index(1)] [Id(1)] public virtual int B { get; set; }
    [MemoryPackOrder(2)] [Key(2)] [ProtoMember(3)] [Index(2)] [Id(2)] public virtual long C { get; set; }
    [MemoryPackOrder(3)] [Key(3)] [ProtoMember(4)] [Index(3)] [Id(3)] public virtual string D { get; set; } = "";
    [MemoryPackOrder(4)] [Key(4)] [ProtoMember(5)] [Index(4)] [Id(4)] public virtual double E { get; set; }
}
