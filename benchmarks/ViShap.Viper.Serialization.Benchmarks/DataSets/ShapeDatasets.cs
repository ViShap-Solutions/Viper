using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

/// <summary>DATA-08 — high entropy, where compression buys nothing.</summary>
internal sealed class IncompressibleDataset : Dataset<BlobEnvelope>
{
    /// <summary>Below <c>MaxArrayLength</c>, which is what bounds a <c>byte[]</c> (§5.2).</summary>
    internal const int Bytes = 900_000;

    internal override string Id => "DATA-08";

    internal override string Name => "Incompressible";

    internal override string Purpose => "Compression's worst case, and what the phase costs when it saves nothing";

    protected override BlobEnvelope Build()
    {
        var rng = new DeterministicRandom(0x0000_0008);

        return new BlobEnvelope
        {
            Id = rng.NextGuid(),
            Name = rng.NextAscii(8, 16),
            Content = rng.NextBytes(Bytes),
        };
    }
}

/// <summary>DATA-09 — heavily repeated structure, where compression is at its best.</summary>
internal sealed class HighlyCompressibleDataset : Dataset<List<string>>
{
    internal const int Count = 20_000;

    internal override string Id => "DATA-09";

    internal override string Name => "HighlyCompressible";

    internal override string Purpose => "Compression's best case";

    protected override List<string> Build()
    {
        var values = new List<string>(Count);

        for (var i = 0; i < Count; i++)
        {
            values.Add("the quick brown fox jumps over the lazy dog, again and again and again");
        }

        return values;
    }
}

/// <summary>DATA-10 — one instance reachable by many paths.</summary>
internal sealed class SharedReferenceDagDataset : Dataset<DagNode>
{
    internal override string Id => "DATA-10";

    internal override string Name => "SharedReferenceDAG";

    internal override string Purpose => "Identity preservation, in time and in size";

    internal override bool IdentitySensitive => true;

    internal override string? VerifyIdentity(object restored)
    {
        var root = (DagNode)restored;

        var first = root.Children[0].Children[0];
        var second = root.Children[1].Children[0];

        return ReferenceEquals(first, second)
            ? null
            : "the shared node was restored as two instances";
    }

    protected override DagNode Build()
    {
        var rng = new DeterministicRandom(0x0000_0010);

        var shared = new List<DagNode>();

        for (var i = 0; i < 40; i++)
        {
            shared.Add(new DagNode
            {
                Id = i,
                Name = rng.NextAscii(8, 20),
                Weight = rng.NextDouble(),
            });
        }

        var root = new DagNode { Id = -1, Name = "root", Weight = 1 };

        for (var branch = 0; branch < 20; branch++)
        {
            var node = new DagNode
            {
                Id = 1000 + branch,
                Name = rng.NextAscii(6, 14),
                Weight = rng.NextDouble(),
            };

            // Every branch points at the same shared instances, in the same order.
            node.Children.Add(shared[0]);

            for (var i = 1; i < shared.Count; i++)
            {
                node.Children.Add(shared[i]);
            }

            root.Children.Add(node);
        }

        return root;
    }
}

/// <summary>DATA-11 — parent/child cycles: impossible without reference support.</summary>
internal sealed class CyclicGraphDataset : Dataset<CyclicNode>
{
    internal override string Id => "DATA-11";

    internal override string Name => "CyclicGraph";

    internal override string Purpose => "The scenario that is impossible without reference support";

    internal override bool RequiresReferences => true;

    internal override bool IdentitySensitive => true;

    internal override string? VerifyIdentity(object restored)
    {
        var root = (CyclicNode)restored;

        return ReferenceEquals(root.Children[0].Parent, root)
            ? null
            : "the cycle back to the parent was not restored";
    }

    protected override CyclicNode Build()
    {
        var rng = new DeterministicRandom(0x0000_0011);
        var root = new CyclicNode { Id = 0, Name = "root" };

        for (var i = 1; i <= 200; i++)
        {
            var child = new CyclicNode
            {
                Id = i,
                Name = rng.NextAscii(6, 16),
                Parent = root,
            };

            root.Children.Add(child);
        }

        return root;
    }
}

/// <summary>DATA-12 — a batch of derived shapes behind one union map.</summary>
internal sealed class PolymorphicBatchDataset : Dataset<List<EventBase>>
{
    internal const int Count = 2_000;

    internal override string Id => "DATA-12";

    internal override string Name => "PolymorphicBatch";

    internal override string Purpose => "Discriminator cost and polymorphic dispatch";

    protected override List<EventBase> Build()
    {
        var rng = new DeterministicRandom(0x0000_0012);
        var events = new List<EventBase>(Count);

        for (var i = 0; i < Count; i++)
        {
            var sequence = (long)i;
            var at = rng.NextDateTime();

            EventBase item = rng.Next(6) switch
            {
                0 => new TextEvent { Text = rng.NextAscii(20, 60), Source = rng.NextAscii(4, 12) },
                1 => new NumericEvent { Value = rng.NextDouble() * 1000, Unit = rng.NextAscii(2, 6) },
                2 => new StateEvent
                {
                    From = (Severity)rng.Next(5),
                    To = (Severity)rng.Next(5),
                    Automatic = rng.NextBool(),
                },
                3 => new LocationEvent
                {
                    Latitude = rng.NextDouble() * 180 - 90,
                    Longitude = rng.NextDouble() * 360 - 180,
                    Accuracy = (float)rng.NextDouble(),
                },
                4 => BuildBatch(rng),
                _ => new ErrorEvent
                {
                    Code = rng.NextAscii(4, 8),
                    Message = rng.NextAscii(20, 80),
                    Attempt = rng.Next(1, 5),
                },
            };

            item.Sequence = sequence;
            item.AtUtc = at;
            events.Add(item);
        }

        return events;
    }

    private static BatchEvent BuildBatch(DeterministicRandom rng)
    {
        var batch = new BatchEvent { Total = rng.Next(1, 100) };

        for (var i = 0; i < 8; i++)
        {
            batch.Ids.Add(rng.NextInt64());
        }

        return batch;
    }
}

/// <summary>DATA-13 — a keyed contract: key, length, payload, sorted by key.</summary>
internal sealed class KeyedEvolutionDataset : Dataset<List<KeyedOrder>>
{
    internal const int Count = 50;

    internal override string Id => "DATA-13";

    internal override string Name => "KeyedEvolution";

    internal override string Purpose => "Evolution cost on the writing side and on the skipping side";

    protected override List<KeyedOrder> Build()
    {
        var rng = new DeterministicRandom(0x0000_0013);
        var orders = new List<KeyedOrder>(Count);

        for (var i = 0; i < Count; i++)
        {
            var order = new KeyedOrder
            {
                Id = rng.NextGuid(),
                Customer = rng.NextAscii(8, 24),
                Total = Math.Round((decimal)(rng.NextDouble() * 5000), 2),
                PlacedUtc = rng.NextDateTime(),
                Channel = (Channel)rng.Next(5),
                Note = rng.NextBool() ? rng.NextAscii(10, 40) : null,
                Revision = rng.Next(1, 20),
            };

            for (var line = 0; line < rng.Next(2, 8); line++)
            {
                order.Lines.Add(new KeyedLine
                {
                    Sku = rng.NextAscii(6, 12),
                    Quantity = rng.Next(1, 50),
                    UnitPrice = Math.Round((decimal)(rng.NextDouble() * 200), 2),
                    Comment = rng.NextBool() ? rng.NextAscii(5, 20) : null,
                });
            }

            orders.Add(order);
        }

        return orders;
    }
}

/// <summary>
/// DATA-14 — one blob inside a small object. A <c>byte[]</c> is an array, so it is bounded by
/// <c>MaxArrayLength</c> (1,000,000 by default) rather than by <c>MaxByteBlobBytes</c>, which bounds
/// the blob-encoded values of §22.4. The size stays under that ceiling.
/// </summary>
internal sealed class ByteBlobDataset : Dataset<BlobEnvelope>
{
    internal const int Bytes = 900_000;

    internal override string Id => "DATA-14/blob";

    internal override string Name => "ByteBlob(0.9 MB)";

    internal override string Purpose => "Pure carrying cost, with traversal taken out of the picture";

    protected override BlobEnvelope Build() => Blob(0x0000_1400, Bytes);

    /// <summary>A repeating pattern rather than noise: the blob measures carrying, not compression.</summary>
    internal static BlobEnvelope Blob(ulong seed, int bytes)
    {
        var rng = new DeterministicRandom(seed);
        var content = new byte[bytes];

        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % 251);
        }

        return new BlobEnvelope
        {
            Id = rng.NextGuid(),
            Name = rng.NextAscii(8, 16),
            Content = content,
        };
    }
}

/// <summary>
/// DATA-14 — several blobs in one payload, which is how a multi-megabyte carrying case is expressed
/// while each array stays under <c>MaxArrayLength</c>.
/// </summary>
internal sealed class BlobBatchDataset : Dataset<List<BlobEnvelope>>
{
    internal const int Count = 4;

    internal override string Id => "DATA-14/batch";

    internal override string Name => $"ByteBlob({Count} × 0.9 MB)";

    internal override string Purpose => "Carrying cost at several megabytes, and the element budget it spends";

    protected override List<BlobEnvelope> Build()
    {
        var blobs = new List<BlobEnvelope>(Count);

        for (var i = 0; i < Count; i++)
        {
            blobs.Add(ByteBlobDataset.Blob(0x0000_1410UL + (ulong)i, ByteBlobDataset.Bytes));
        }

        return blobs;
    }
}

/// <summary>DATA-15 — bulk numeric arrays.</summary>
internal sealed class NumericArraysDataset : Dataset<NumericArrays>
{
    internal const int Count = 100_000;

    internal override string Id => "DATA-15";

    internal override string Name => "NumericArrays";

    internal override string Purpose => "Where a layout-fixed serializer legitimately wins, shown rather than avoided";

    protected override NumericArrays Build()
    {
        var rng = new DeterministicRandom(0x0000_0015);

        var value = new NumericArrays
        {
            Integers = new int[Count],
            Longs = new long[Count],
            Doubles = new double[Count],
        };

        for (var i = 0; i < Count; i++)
        {
            value.Integers[i] = rng.NextInt32();
            value.Longs[i] = rng.NextInt64();
            value.Doubles[i] = rng.NextDouble() * 1_000_000;
        }

        return value;
    }
}

/// <summary>DATA-16 — many short strings.</summary>
internal sealed class StringTableDataset : Dataset<List<string>>
{
    internal const int Count = 50_000;

    internal override string Id => "DATA-16";

    internal override string Name => "StringTable";

    internal override string Purpose => "String-dominated payloads and per-string overhead";

    protected override List<string> Build()
    {
        var rng = new DeterministicRandom(0x0000_0016);
        var values = new List<string>(Count);

        for (var i = 0; i < Count; i++)
        {
            values.Add(rng.NextAscii(6, 18));
        }

        return values;
    }
}

/// <summary>DATA-17 — mostly-null members.</summary>
internal sealed class NullSparseDataset : Dataset<List<NullSparse>>
{
    internal const int Count = 500;

    internal override string Id => "DATA-17";

    internal override string Name => "NullSparse";

    internal override string Purpose => "Null framing cost against formats that omit absent fields";

    protected override List<NullSparse> Build()
    {
        var rng = new DeterministicRandom(0x0000_0017);
        var values = new List<NullSparse>(Count);

        for (var i = 0; i < Count; i++)
        {
            values.Add(new NullSparse
            {
                Id = rng.NextInt32(),
                A = rng.NextAscii(6, 14),
                F = rng.Next(1, 1000),
            });
        }

        return values;
    }
}

/// <summary>DATA-18 — two hundred flat members.</summary>
internal sealed class WideObjectDataset : Dataset<WideObject>
{
    internal override string Id => "DATA-18";

    internal override string Name => "WideObject";

    internal override string Purpose => "Member-plan cost, positional against keyed";

    protected override WideObject Build()
    {
        var rng = new DeterministicRandom(0x0000_0018);
        var value = new WideObject();

        foreach (var property in typeof(WideObject).GetProperties())
        {
            object boxed = Type.GetTypeCode(property.PropertyType) switch
            {
                TypeCode.Int32 => rng.NextInt32(),
                TypeCode.Int64 => rng.NextInt64(),
                TypeCode.Double => rng.NextDouble() * 1000,
                TypeCode.Boolean => rng.NextBool(),
                TypeCode.Decimal => Math.Round((decimal)(rng.NextDouble() * 1000), 3),
                TypeCode.String => rng.NextAscii(6, 20),
                TypeCode.DateTime => rng.NextDateTime(),
                _ => rng.NextGuid(),
            };

            property.SetValue(value, boxed);
        }

        return value;
    }
}

/// <summary>DATA-19 — one instance of each container family.</summary>
internal sealed class CollectionZooDataset : Dataset<CollectionZoo>
{
    internal const int Count = 200;

    internal override string Id => "DATA-19";

    internal override string Name => "CollectionZoo";

    internal override string Purpose => "Breadth over the supported-type table in a single measurement";

    protected override CollectionZoo Build()
    {
        var rng = new DeterministicRandom(0x0000_0019);

        var value = new CollectionZoo
        {
            Array = new int[Count],
            Rectangular = new int[16, 16],
            Pair = new KeyValuePair<string, int>(rng.NextAscii(4, 10), rng.NextInt32()),
            Tuple = (rng.NextInt32(), rng.NextAscii(4, 10)),
        };

        for (var i = 0; i < Count; i++)
        {
            value.Array[i] = rng.NextInt32();
            value.List.Add(rng.NextAscii(4, 12));
            value.Set.Add(rng.NextInt32());
            value.SortedSet.Add(rng.NextInt32());
            value.Linked.AddLast(rng.NextAscii(4, 12));
            value.Stack.Push(rng.NextInt32());
            value.Queue.Enqueue(rng.NextInt32());
            value.Dictionary[$"d{i:D3}"] = rng.NextInt32();
            value.SortedDictionary[$"s{i:D3}"] = rng.NextInt32();
        }

        for (var row = 0; row < 16; row++)
        {
            for (var column = 0; column < 16; column++)
            {
                value.Rectangular[row, column] = rng.NextInt32();
            }
        }

        value.ImmutableArray = [.. value.Array];
        value.ImmutableList = [.. value.List];
        value.ImmutableDictionary = value.Dictionary.ToImmutableDictionary();

        return value;
    }
}

/// <summary>DATA-20 — time, wide numerics, vectors and matrices.</summary>
internal sealed class TimeAndNumericsDataset : Dataset<List<TimeAndNumerics>>
{
    internal const int Count = 100;

    internal override string Id => "DATA-20";

    internal override string Name => "TimeAndNumerics";

    internal override string Purpose => "The families other serializers most often lack natively";

    protected override List<TimeAndNumerics> Build()
    {
        var rng = new DeterministicRandom(0x0000_0020);
        var values = new List<TimeAndNumerics>(Count);

        for (var i = 0; i < Count; i++)
        {
            var utc = rng.NextDateTime();

            values.Add(new TimeAndNumerics
            {
                Utc = utc,
                Local = DateTime.SpecifyKind(utc, DateTimeKind.Local),
                Offset = new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(rng.Next(-11, 12))),
                Elapsed = TimeSpan.FromTicks(rng.NextInt64() / 1000),
                Date = DateOnly.FromDateTime(utc),
                Time = TimeOnly.FromDateTime(utc),
                Money = Math.Round((decimal)(rng.NextDouble() * 100_000), 6),
                Wide = new Int128((ulong)rng.NextInt64(), (ulong)rng.NextInt64()),
                WideUnsigned = new UInt128((ulong)rng.NextInt64(), (ulong)rng.NextInt64()),
                Big = new BigInteger(rng.NextInt64()) * new BigInteger(rng.NextInt64()),
                Small = (Half)rng.NextDouble(),
                Complex = new Complex(rng.NextDouble(), rng.NextDouble()),
                Vector2 = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                Vector3 = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()),
                Vector4 = new Vector4((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()),
                Quaternion = new Quaternion((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()),
                Matrix3X2 = new Matrix3x2(
                    (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(),
                    (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()),
                Matrix4X4 = Matrix4x4.CreateRotationZ((float)rng.NextDouble()),
                Guid = rng.NextGuid(),
                Version = new Version(rng.Next(1, 9), rng.Next(0, 99), rng.Next(0, 999)),
                Uri = new Uri($"https://example.invalid/{rng.NextAscii(4, 12)}"),
            });
        }

        return values;
    }
}
