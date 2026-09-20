namespace ViShap.Viper.Serialization.Benchmarks.Models.Viper;

internal enum Channel
{
    Unknown = 0,
    Web = 1,
    Mobile = 2,
    Partner = 3,
    Internal = 4,
}

internal enum Severity : byte
{
    Trace = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4,
}

/// <summary>DATA-01 — ten scalars, a short string, a <see cref="Guid"/> and a timestamp.</summary>
internal sealed class TinyFlat
{
    public int Id { get; set; }

    public long Sequence { get; set; }

    public short Region { get; set; }

    public byte Version { get; set; }

    public bool Active { get; set; }

    public double Score { get; set; }

    public float Ratio { get; set; }

    public decimal Amount { get; set; }

    public Channel Channel { get; set; }

    public string Label { get; set; } = string.Empty;

    public Guid Reference { get; set; }

    public DateTime CreatedUtc { get; set; }
}

/// <summary>A nested component of <see cref="MediumObject"/>.</summary>
internal sealed class Address
{
    public string Line1 { get; set; } = string.Empty;

    public string? Line2 { get; set; }

    public string City { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}

/// <summary>A nested component of <see cref="MediumObject"/>.</summary>
internal sealed class Measurement
{
    public string Metric { get; set; } = string.Empty;

    public double Value { get; set; }

    public Severity Severity { get; set; }

    public DateTime TakenUtc { get; set; }

    public bool Estimated { get; set; }
}

/// <summary>DATA-02 — an ordinary business object: scalars, nested objects, enums, nullables, small collections.</summary>
internal sealed class MediumObject
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Note { get; set; }

    public int Revision { get; set; }

    public long ExternalId { get; set; }

    public short Priority { get; set; }

    public byte Flags { get; set; }

    public bool Published { get; set; }

    public bool Archived { get; set; }

    public double Weight { get; set; }

    public double Confidence { get; set; }

    public float Scale { get; set; }

    public decimal Price { get; set; }

    public decimal Discount { get; set; }

    public Channel Channel { get; set; }

    public Severity Severity { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public DateTime? DeletedUtc { get; set; }

    public TimeSpan Duration { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? DelegateId { get; set; }

    public int? Quota { get; set; }

    public double? Threshold { get; set; }

    public string Culture { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public Address Primary { get; set; } = new();

    public Address? Secondary { get; set; }

    public List<Measurement> Measurements { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public Dictionary<string, string> Attributes { get; set; } = [];

    public int[] Counters { get; set; } = [];
}

/// <summary>DATA-05 — the value side of a dictionary-heavy payload.</summary>
internal sealed class ScalarRecord
{
    public int Id { get; set; }

    public long Ticks { get; set; }

    public double Value { get; set; }

    public bool Valid { get; set; }

    public string Code { get; set; } = string.Empty;
}

/// <summary>DATA-06 — a chain whose depth is the point.</summary>
internal sealed class DeepNode
{
    public int Level { get; set; }

    public string Label { get; set; } = string.Empty;

    public long Payload { get; set; }

    public DeepNode? Child { get; set; }
}
