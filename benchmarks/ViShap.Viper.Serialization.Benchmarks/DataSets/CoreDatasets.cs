using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

/// <summary>Builders shared by the generators, so one shape is built one way everywhere.</summary>
internal static class Build
{
    private static readonly string[] Cultures = ["en-US", "de-DE", "ja-JP", "ru-RU", "pt-BR", "fr-FR"];
    private static readonly string[] Metrics = ["latency", "throughput", "error_rate", "queue_depth", "cpu", "rss"];
    private static readonly string[] Countries = ["US", "DE", "JP", "RU", "BR", "FR", "GB", "IN"];

    internal static TinyFlat TinyFlat(DeterministicRandom rng) => new()
    {
        Id = rng.NextInt32(),
        Sequence = rng.NextInt64(),
        Region = (short)rng.Next(short.MaxValue),
        Version = rng.NextByte(),
        Active = rng.NextBool(),
        Score = rng.NextDouble() * 1000,
        Ratio = (float)rng.NextDouble(),
        Amount = Math.Round((decimal)(rng.NextDouble() * 10_000), 4),
        Channel = (Channel)rng.Next(5),
        Label = rng.NextAscii(6, 18),
        Reference = rng.NextGuid(),
        CreatedUtc = rng.NextDateTime(),
    };

    internal static Address Address(DeterministicRandom rng) => new()
    {
        Line1 = rng.NextAscii(10, 28),
        Line2 = rng.NextBool() ? rng.NextAscii(4, 12) : null,
        City = rng.NextAscii(4, 14),
        PostalCode = rng.NextAscii(4, 8),
        CountryCode = rng.Pick(Countries),
        Latitude = rng.NextDouble() * 180 - 90,
        Longitude = rng.NextDouble() * 360 - 180,
    };

    internal static Measurement Measurement(DeterministicRandom rng) => new()
    {
        Metric = rng.Pick(Metrics),
        Value = rng.NextDouble() * 5000,
        Severity = (Severity)rng.Next(5),
        TakenUtc = rng.NextDateTime(),
        Estimated = rng.NextBool(),
    };

    internal static MediumObject MediumObject(DeterministicRandom rng)
    {
        var value = new MediumObject
        {
            Id = rng.NextGuid(),
            Name = rng.NextAscii(8, 24),
            Description = rng.NextAscii(80, 240),
            Note = rng.NextBool() ? rng.NextAscii(20, 60) : null,
            Revision = rng.Next(1, 500),
            ExternalId = rng.NextInt64(),
            Priority = (short)rng.Next(1, 100),
            Flags = rng.NextByte(),
            Published = rng.NextBool(),
            Archived = rng.NextBool(),
            Weight = rng.NextDouble() * 100,
            Confidence = rng.NextDouble(),
            Scale = (float)rng.NextDouble(),
            Price = Math.Round((decimal)(rng.NextDouble() * 1000), 2),
            Discount = Math.Round((decimal)(rng.NextDouble() * 100), 2),
            Channel = (Channel)rng.Next(5),
            Severity = (Severity)rng.Next(5),
            CreatedUtc = rng.NextDateTime(),
            UpdatedUtc = rng.NextDateTime(),
            DeletedUtc = rng.NextBool() ? rng.NextDateTime() : null,
            Duration = TimeSpan.FromMilliseconds(rng.Next(1, 3_600_000)),
            OwnerId = rng.NextGuid(),
            DelegateId = rng.NextBool() ? rng.NextGuid() : null,
            Quota = rng.NextBool() ? rng.Next(1, 10_000) : null,
            Threshold = rng.NextBool() ? rng.NextDouble() : null,
            Culture = rng.Pick(Cultures),
            Slug = rng.NextAscii(10, 30),
            Category = rng.NextAscii(5, 15),
            Primary = Address(rng),
            Secondary = rng.NextBool() ? Address(rng) : null,
            Counters = new int[8],
        };

        for (var i = 0; i < 6; i++)
        {
            value.Measurements.Add(Measurement(rng));
        }

        for (var i = 0; i < 8; i++)
        {
            value.Tags.Add(rng.NextAscii(4, 12));
        }

        for (var i = 0; i < 10; i++)
        {
            value.Attributes[$"key-{i:D2}"] = rng.NextAscii(6, 20);
        }

        for (var i = 0; i < value.Counters.Length; i++)
        {
            value.Counters[i] = rng.NextInt32();
        }

        return value;
    }

    internal static ScalarRecord ScalarRecord(DeterministicRandom rng) => new()
    {
        Id = rng.NextInt32(),
        Ticks = rng.NextInt64(),
        Value = rng.NextDouble() * 1_000_000,
        Valid = rng.NextBool(),
        Code = rng.NextAscii(4, 10),
    };
}

internal sealed class TinyFlatDataset : Dataset<TinyFlat>
{
    internal override string Id => "DATA-01";

    internal override string Name => "TinyFlat";

    internal override string Purpose => "Per-call fixed cost: framing, dispatch, header";

    protected override TinyFlat Build() => DataSets.Build.TinyFlat(new DeterministicRandom(0x0000_0001));
}

internal sealed class MediumObjectDataset : Dataset<MediumObject>
{
    internal override string Id => "DATA-02";

    internal override string Name => "MediumObject";

    internal override string Purpose => "The ordinary business object";

    protected override MediumObject Build() => DataSets.Build.MediumObject(new DeterministicRandom(0x0000_0002));
}

internal sealed class RecordBatchSmallDataset : Dataset<List<TinyFlat>>
{
    internal const int Count = 250;

    internal override string Id => "DATA-03";

    internal override string Name => "RecordBatchSmall";

    internal override string Purpose => "Per-element cost at a realistic batch size";

    protected override List<TinyFlat> Build()
    {
        var rng = new DeterministicRandom(0x0000_0003);
        var batch = new List<TinyFlat>(Count);

        for (var i = 0; i < Count; i++)
        {
            batch.Add(DataSets.Build.TinyFlat(rng));
        }

        return batch;
    }
}

internal sealed class RecordBatchLargeDataset : Dataset<List<TinyFlat>>
{
    internal const int Count = 20_000;

    internal override string Id => "DATA-04";

    internal override string Name => "RecordBatchLarge";

    internal override string Purpose => "Throughput, and where allocation strategy starts to dominate";

    protected override List<TinyFlat> Build()
    {
        var rng = new DeterministicRandom(0x0000_0004);
        var batch = new List<TinyFlat>(Count);

        for (var i = 0; i < Count; i++)
        {
            batch.Add(DataSets.Build.TinyFlat(rng));
        }

        return batch;
    }
}

internal sealed class DictionaryHeavyDataset : Dataset<Dictionary<string, ScalarRecord>>
{
    internal const int Count = 12_000;

    internal override string Id => "DATA-05";

    internal override string Name => "DictionaryHeavy";

    internal override string Purpose => "Dictionary write and rebuild cost";

    protected override Dictionary<string, ScalarRecord> Build()
    {
        var rng = new DeterministicRandom(0x0000_0005);
        var map = new Dictionary<string, ScalarRecord>(Count);

        for (var i = 0; i < Count; i++)
        {
            map[$"entry-{i:D6}"] = DataSets.Build.ScalarRecord(rng);
        }

        return map;
    }
}

internal sealed class DeepGraphDataset(int depth) : Dataset<DeepNode>
{
    internal int Depth { get; } = depth;

    internal override string Id => $"DATA-06/{Depth}";

    internal override string Name => $"DeepGraph({Depth})";

    internal override string Purpose => "Depth accounting and recursion cost";

    protected override DeepNode Build()
    {
        var rng = new DeterministicRandom(0x0000_0600UL + (ulong)Depth);

        DeepNode? child = null;

        for (var level = Depth; level >= 1; level--)
        {
            child = new DeepNode
            {
                Level = level,
                Label = rng.NextAscii(4, 10),
                Payload = rng.NextInt64(),
                Child = child,
            };
        }

        return child!;
    }
}

internal sealed class UnicodeHeavyDataset(bool ascii) : Dataset<List<string>>
{
    internal const int Count = 2_000;

    private static readonly string[] Fragments =
    [
        "日本語のテキスト", "Ελληνικά", "русский текст", "العربية", "한국어",
        "emoji 😀🚀🌍", "combining é́ à", "ñoño", "中文文本", "हिन्दी",
    ];

    internal bool Ascii { get; } = ascii;

    internal override string Id => Ascii ? "DATA-07/ascii" : "DATA-07/unicode";

    internal override string Name => Ascii ? "UnicodeHeavy (ASCII twin)" : "UnicodeHeavy";

    internal override string Purpose => "String encoding cost, and the honest ASCII-versus-UTF-8 difference";

    protected override List<string> Build()
    {
        var rng = new DeterministicRandom(Ascii ? 0x0000_0071UL : 0x0000_0072UL);
        var values = new List<string>(Count);

        for (var i = 0; i < Count; i++)
        {
            if (Ascii)
            {
                values.Add(rng.NextAscii(20, 60));
                continue;
            }

            var text = new StringBuilder();

            for (var part = 0; part < rng.Next(2, 6); part++)
            {
                text.Append(rng.Pick(Fragments)).Append(' ');
            }

            values.Add(text.ToString());
        }

        return values;
    }
}
