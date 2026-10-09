<p align="center">
  <img src="icon.png" alt="Viper Logo" width="150px" />
</p>

# Viper

> **Ultimate binary engineering solutions powered by the Viper ecosystem.**

[![NuGet](https://img.shields.io/nuget/v/ViShap.Viper.svg)](https://www.nuget.org/packages/ViShap.Viper)
[![GitHub Actions](https://github.com/ViShap-Solutions/Viper/actions/workflows/ci.yml/badge.svg)](https://github.com/ViShap-Solutions/Viper/actions/workflows/ci.yml)
[![Releases](https://img.shields.io/github/v/release/ViShap-Solutions/Viper.svg)](https://github.com/ViShap-Solutions/Viper/releases)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-2ea44f)](LICENSE)

Viper is a binary serializer for .NET 10. It writes objects as self-describing frames, reads them from
memory, streams and pipes, and can compress, checksum and encrypt a frame. It is built to read bytes it
does not trust: every declared length, count and nesting level is bounded before anything is allocated for
it, and every failure is an exception from one hierarchy.

---

## 🛠️ Features

- **Buffers, streams and pipes** — `byte[]`, `IBufferWriter<byte>`, pooled payloads, spans, sequences,
  `Stream` and `System.IO.Pipelines`; asynchronous methods, `Populate` into an existing object, streams of
  frames.
- **Explicit layouts** — positional by default; keyed contracts (`[BinaryContract]`, `[BinaryKey(n)]`) so a
  type can evolve without breaking stored data.
- **Declared polymorphism** — `[BinaryUnion(tag, typeof(Derived))]`; only tags travel, never type names.
- **References** — shared and cyclic references with `PreserveReferences()`.
- **Data protection** — Deflate and Brotli compression, CRC-32 and XXH3 checksums, AES-256-GCM and
  ChaCha20-Poly1305 encryption with an authenticated header, key providers with key ids, and policies that
  reject downgraded frames.
- **Two formats** — V1, the self-describing frame, and V0, the headerless payload for protocols that already
  frame their messages.
- **Limits** — bounds on depth, sizes, cumulative budgets and the size of every phase, checked before the
  memory they protect is allocated.
- **Diagnostics** — a header inspector and a frame dumper that renders a tree and points at the value that
  failed.

---

## 🧩 Supported types

| Family | Types |
|---|---|
| Primitives | `bool`, integers up to `Int128`/`UInt128`, `float`, `double`, `decimal`, `char`, `string`, enums, `Half`, `Rune`, `BigInteger` |
| Time | `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `TimeZoneInfo` |
| Numerics and system | `Complex`, `Vector2`–`Vector4`, `Quaternion`, `Plane`, `Matrix3x2`, `Matrix4x4`, `Guid`, `Uri`, `Version`, `BitArray`, `CultureInfo` |
| Arrays and memory | `T[]`, multi-dimensional arrays, `Memory<T>`, `ReadOnlyMemory<T>`, `ArraySegment<T>`, `ReadOnlySequence<T>` |
| Collections | `List<>`, `HashSet<>`, `SortedSet<>`, `LinkedList<>`, `Stack<>`, `Queue<>`, `ObservableCollection<>`, read-only wrappers, your own `ICollection<T>` |
| Dictionaries | `Dictionary<,>`, `SortedDictionary<,>`, `SortedList<,>`, `ReadOnlyDictionary<,>`, `PriorityQueue<,>` |
| Concurrent, immutable, frozen | `Concurrent*`, `Immutable*` (including `ImmutableArray<>`), `FrozenSet<>`, `FrozenDictionary<,>` |
| Composite | `Nullable<T>`, `KeyValuePair<,>`, `Tuple<…>`, `ValueTuple<…>`, `Lazy<T>` |
| Objects | any class or struct with a parameterless constructor, member by member |

Every type and how it comes back: [Supported types](docs/supported-types.md).

---

## 🔐 Compression, checksums and encryption

| Family | Built in |
|---|---|
| Compression | `DeflateCompression`, `BrotliCompression` |
| Checksums | `Crc32Checksum`, `XxHash3Checksum`, `XxHash128Checksum` |
| Encryption | `Aes256GcmEncryption`, `ChaCha20Poly1305Encryption` — authenticated, and the header is authenticated too |
| Keys | `StaticKeyProvider`, `DelegateKeyProvider`, `HkdfKeyProvider` (one derived key per key id), key ids for rotation |
| Policies | `RequireEncryption`, `RequireChecksum` reject a frame that was downgraded |
| Your own | implement `ICompressionAlgorithm`, `IChecksumAlgorithm`, `IEncryptionAlgorithm` or `IKeyProvider` and register it by name |

Details: [Algorithms and keys](docs/algorithms-and-keys.md).

---

## 🧬 Schemas that evolve, types that vary

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

Animal[] zoo = [new Dog { Name = "Rex" }, new Cat { Name = "Tom" }];
Animal[]? copy = serializer.Deserialize<Animal[]>(serializer.Serialize(zoo));

Console.WriteLine(copy![1].GetType().Name);

[BinaryUnion(1, typeof(Dog))]
[BinaryUnion(2, typeof(Cat))]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : Animal;

public sealed class Cat : Animal;
```

`[BinaryContract]` with `[BinaryKey(n)]` lets a reader skip keys it does not know, so a stored type can
gain and lose members; `[BinaryUnion]` declares the derived types a base may hold, and only tags travel —
never type names. See [Contracts](docs/contracts.md) and [Polymorphism](docs/polymorphism.md).

---

## 📦 Packages

| Package | Contains |
|---|---|
| [`ViShap.Viper`](src/ViShap.Viper/METAPACK-README.md) | the meta-package: references the three packages below |
| [`ViShap.Viper.Core`](src/ViShap.Viper.Core/CORE-README.md) | attributes, algorithm and key contracts, exceptions — for libraries that annotate types or implement algorithms |
| [`ViShap.Viper.Serialization`](src/ViShap.Viper.Serialization/SERIALIZATION-README.md) | the serializer, built-in algorithms and key providers, diagnostics |
| [`ViShap.Viper.Generator`](src/ViShap.Viper.Generator/GENERATOR-README.md) | the build-time source generator of type contracts, with compiler diagnostics for contract mistakes |

```bash
dotnet add package ViShap.Viper
```

---

## 📂 Project Structure

```
📂 Viper
├── 📁 src
│   │
│   ├── 📁 ViShap.Viper
│   │   └── 📄 METAPACK-README.md
│   │
│   ├── 📁 ViShap.Viper.Core
│   │   └── 📄 CORE-README.md
│   │
│   └── 📁 ViShap.Viper.Serialization
│       └── 📄 SERIALIZATION-README.md
│
├── 📁 tests
├── 📁 benchmarks
├── 📁 docs
│
├── ⚖️ LICENSE
└── 📝 README.md
```

---

## 🩸 Usage

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

byte[] bytes = serializer.Serialize(new Order { Id = 1001, Customer = "Ada Lovelace" });
Order? copy = serializer.Deserialize<Order>(bytes);

Console.WriteLine(copy!.Customer);

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
}
```

Configuration goes through a builder, and is validated once when it is built:

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;

byte[] key = RandomNumberGenerator.GetBytes(32);

var options = BinarySerializerOptions.Configure()
    .WithCompression(new BrotliCompression())
    .WithChecksum(new XxHash3Checksum())
    .WithEncryption(new Aes256GcmEncryption(), key, keyId: "2026-q3")
    .RequireEncryption()
    .Build();

var serializer = new BinarySerializer(options);
byte[] frame = serializer.Serialize("protected");

Console.WriteLine(serializer.Deserialize<string>(frame));
```

### Documentation

The documentation is in [`docs/`](docs/README.md):

| Page | What it covers |
|---|---|
| [Getting started](docs/getting-started.md) | installing the packages, a first round trip |
| [Entry points](docs/entry-points.md) | buffers, streams, pipes, asynchronous use, `Populate`, streams of frames |
| [Options and limits](docs/options-and-limits.md) | the options builder and every limit |
| [Formats: V1 and V0](docs/formats.md) | the self-describing frame and the headerless codec |
| [Contracts and schema evolution](docs/contracts.md) | positional and keyed layouts |
| [The source generator](docs/generator.md) | generated type contracts and their diagnostics |
| [References](docs/references.md) | shared and cyclic references |
| [Polymorphism](docs/polymorphism.md) | `[BinaryUnion]` |
| [Algorithms and keys](docs/algorithms-and-keys.md) | compression, checksums, encryption, key providers, your own algorithms |
| [Supported types](docs/supported-types.md) | every type and how it comes back |
| [Exceptions](docs/exceptions.md) | the hierarchy and when each is raised |
| [Diagnostics](docs/diagnostics.md) | inspecting and dumping frames |
| [Trimming and native AOT](docs/aot.md) | what the reflection path asks of your application |

---

## License

MIT License (see LICENSE file for details)
