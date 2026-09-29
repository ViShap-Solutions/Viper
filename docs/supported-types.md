# Supported types

A type is supported when a built-in encoding claims it, or when it is written member by member as
described in [Contracts](contracts.md). Any other type is a `BinaryTypeException` the first time it is
used.

| Family | Types |
|---|---|
| Primitives | `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`, `string`, enums, `Half`, `Int128`, `UInt128`, `IntPtr`, `UIntPtr`, `Rune`, `BigInteger` |
| Time | `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `TimeZoneInfo` |
| Numerics | `Complex`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Plane`, `Matrix3x2`, `Matrix4x4` |
| System | `Guid`, `Uri`, `Version`, `StringBuilder`, `CultureInfo`, `BitArray` |
| Composite | `KeyValuePair<,>`, `Tuple<…>`, `ValueTuple<…>`, `Lazy<>`, `ImmutableArray<>`, arrays of rank greater than one |
| Arrays and memory | `T[]`, `Memory<>`, `ReadOnlyMemory<>`, `ArraySegment<>`, `ReadOnlySequence<>` |
| Collections | `List<>`, `IList<>`, `ICollection<>`, `IEnumerable<>`, `IReadOnlyList<>`, `IReadOnlyCollection<>`, `HashSet<>`, `ISet<>`, `SortedSet<>`, `LinkedList<>`, `ObservableCollection<>`, `Stack<>`, `Queue<>`, `ReadOnlyCollection<>`, `ReadOnlyObservableCollection<>` |
| Concurrent | `ConcurrentBag<>`, `ConcurrentQueue<>`, `ConcurrentStack<>`, `ConcurrentDictionary<,>` |
| Dictionaries and queues | `Dictionary<,>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `ReadOnlyDictionary<,>`, `SortedDictionary<,>`, `SortedList<,>`, `PriorityQueue<,>` |
| Immutable | `ImmutableList<>`, `IImmutableList<>`, `ImmutableHashSet<>`, `IImmutableSet<>`, `ImmutableSortedSet<>`, `ImmutableQueue<>`, `IImmutableQueue<>`, `ImmutableStack<>`, `IImmutableStack<>`, `ImmutableDictionary<,>`, `IImmutableDictionary<,>`, `ImmutableSortedDictionary<,>` |
| Frozen | `FrozenSet<>`, `FrozenDictionary<,>` |
| Custom collections | any concrete type implementing `ICollection<T>` with a public parameterless constructor and an `Add` method |
| Objects | any other type with a parameterless constructor, written member by member |

`Nullable<T>` of any supported value type is supported as well.

Every value has exactly one spelling on the wire, and a reader refuses any other with
`BinaryFormatException`: a string's bytes are strict UTF-8, a `BigInteger` is its shortest two's
complement, a `BitArray`'s unused bits are zero, and a `Version` or a `CultureInfo` is the text its
writer produces (`1.2`, `en-US`). A `string` that UTF-8 cannot encode — one holding a lone surrogate
such as `"\uD800"` — is refused when it is written, with `BinaryFormatException`, rather than
stored as a different value; nothing reaches the destination.

```csharp
using System.Collections.Immutable;
using ViShap.Viper;

var serializer = new BinarySerializer();

var value = new Sample
{
    Id = Guid.NewGuid(),
    At = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(2)),
    Elapsed = TimeSpan.FromMinutes(90),
    Amounts = new Dictionary<string, decimal> { ["net"] = 10.5m, ["tax"] = 2.1m },
    Tags = ImmutableArray.Create("a", "b"),
    Point = (3, 4),
    Grid = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } },
    Level = Level.High,
    Optional = null
};

Sample? copy = serializer.Deserialize<Sample>(serializer.Serialize(value));

Console.WriteLine(copy!.Id == value.Id);
Console.WriteLine(copy.At == value.At);
Console.WriteLine(copy.Amounts["tax"]);
Console.WriteLine(copy.Tags.Length + " " + copy.Point + " " + copy.Grid[1, 2] + " " + copy.Level);
Console.WriteLine(copy.Optional.HasValue);

public enum Level { Low, High }

public sealed class Sample
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public TimeSpan Elapsed { get; set; }
    public Dictionary<string, decimal> Amounts { get; set; } = [];
    public ImmutableArray<string> Tags { get; set; }
    public (int X, int Y) Point { get; set; }
    public int[,] Grid { get; set; } = new int[0, 0];
    public Level Level { get; set; }
    public int? Optional { get; set; }
}
```

## How a value comes back

- **Interfaces** resolve to a concrete type on read. `IList<T>`, `ICollection<T>`, `IEnumerable<T>`,
  `IReadOnlyList<T>` and `IReadOnlyCollection<T>` produce `List<T>`; `ISet<T>` produces `HashSet<T>`;
  `IDictionary<K,V>` produces `Dictionary<K,V>`; `IReadOnlyDictionary<K,V>` produces
  `ReadOnlyDictionary<K,V>`; each immutable interface produces the corresponding immutable type. Reference
  identity across such a member is preserved, the concrete type is not.
- **Order.** `Stack<>`, `ConcurrentStack<>` and `ImmutableStack<>` round-trip so that iteration order is
  preserved. Unordered containers — `HashSet<>`, `ConcurrentBag<>`, `FrozenSet<>` and every dictionary
  that is not sorted — round-trip their contents, not their iteration order.
- **`PriorityQueue<TElement,TPriority>`** round-trips its element and priority pairs, so dequeue order is
  rebuilt from the priorities rather than copied.
- **`Lazy<T>`** travels as its value, so writing one materializes it: an unevaluated instance has its
  factory run, and an exception from that factory is yours and propagates unchanged. Reading produces a
  `Lazy<T>` that already holds the value and never runs a factory, although `IsValueCreated` is `false`
  until the value is first asked for.
- **Memory-like values** — `Memory<T>`, `ReadOnlyMemory<T>`, `ArraySegment<T>` and
  `ReadOnlySequence<T>` — travel as their elements alone, so their backing storage is not part of the
  value. Reading builds a fresh array and wraps all of it: an `ArraySegment<T>` comes back at offset zero
  over an array exactly as long as the segment, and a multi-segment `ReadOnlySequence<T>` comes back as a
  single segment. A default `ArraySegment<T>` is written as an empty segment.
- **`byte[]` is an array**, like every other array. It is bounded by `MaxArrayLength` and each byte is
  one element of `MaxTotalElements`; see [Options and limits](options-and-limits.md#carrying-binary-data).
- **`ImmutableArray<T>`** distinguishes `default` from empty; every other container does not.
- **`DateTime`** travels with its `Kind` but not its zone. A `Local` value is written as the instant it
  names and comes back in the reader's local zone, so the instant survives a machine in another zone
  and the wall-clock reading does not. Use `DateTimeOffset`, or `DateTimeKind.Utc`, when the value must
  compare equal at both ends.
- **Strings** are UTF-8, and a payload whose string bytes are not valid UTF-8 is a
  `BinaryFormatException` rather than being repaired.

### Duplicates

A key or an element that a payload declares twice is malformed input, whichever container would receive
it. Left to themselves the containers disagree — a dictionary raises, a `ConcurrentDictionary` drops the
repeat, a set collapses it — so the same bytes would fail, silently lose data or succeed depending only
on the declared type of a member. Viper therefore rejects the duplicate with a `BinaryFormatException`. A
sequence that admits repeats, such as a list, an array or a queue, is unaffected: the same value twice is
data there. A null dictionary key is refused the same way.

### Custom collections

A concrete type that implements `ICollection<T>` and has a public parameterless constructor and an `Add`
method is written as a sequence of its elements, so a collection class of your own needs no annotation.

## What is not supported

- **Delegates** are rejected with `BinaryTypeException` as a root value, as a member or as an element.
  Mark a member that holds one with `[BinaryIgnore]`.
- **A type that cannot be constructed** — an interface or an abstract class with no
  [`[BinaryUnion]`](polymorphism.md) declaration, or a class with no parameterless constructor — is a
  `BinaryTypeException` when it is read.
- **A value whose runtime type differs from its declared type** and has no union declaration is a
  `BinaryTypeException` when it is written.

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;

var serializer = new BinarySerializer();

try
{
    serializer.Serialize<Action>(() => { });
}
catch (BinaryTypeException)
{
    Console.WriteLine("a delegate carries behaviour, not data");
}
```
