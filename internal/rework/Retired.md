# Retired — what the rework removed or renamed

**Purpose.** A name, rule or term that the rework removes survives silently wherever nobody looks for
it. This ledger is where each stage records what it removed, so that R9 can search the whole
repository for every entry and prove that nothing of the previous system is left
(`Rework-Plan.md` §0.12, R9) [D9.28].

**Rule.** A stage appends a row for everything it removes or renames — a type, a member, an option, a
command-line argument, a term, a documented rule, a checkpoint — in the same change as the removal.
A row names what replaced it, or says that nothing did.

**The R9 sweep.** Every `Searched as` value is searched in `src/`, `tests/`, `benchmarks/`, the XML
documentation, `CLAUDE.md`, `.claude/skills/` and every living document of `internal/`. The only hits
allowed are in historical documents, in explicit `retired in Rn` markers, and in the frozen
artifacts under `benchmarks/.../Baselines/`, which BASE-08 forbids rewriting. Each row's last column
records the result.

| Stage | Removed or renamed | Kind | Replaced by | Searched as | R9 sweep |
|---|---|---|---|---|---|
| R0 | `RunKind.Partial` | harness type member | `RunKind.Measurement` | `RunKind.Partial`, `Partial run`, `partial run`, `Partial-прогон` | |
| R0 | "Pre-release (`beta`, `rc`) — Partial по умолчанию" | workflow rule | a full `--track A` on a pre-release tag is a Baseline, taken only by the owner's decision (`Development-Workflow.md` §6.2) | `Partial по умолчанию` | |
| R0 | `RunKind.Baseline` described as the record of a released version | harness documentation | the record of a tagged revision: a release, a pre-release on request, a local tag before a rework | `record of a released version`, `one per release` | |
| R0 | DATA-09 at 20 000 strings, target ~1 MB | benchmark dataset | 5 000 strings, target ~0.35 MB (Benchmark-Plan R-02) | `20_000` in `ShapeDatasets.cs`, `HighlyCompressible` with `~1 MB` | |
| R1 | `ValueReader` | internal type | `WireReader`, a `ref struct` over a span or a sequence | `ValueReader`, `ValueReader.cs` | |
| R1 | `ValueWriter` | internal type | `WireWriter`, a `ref struct` over `PayloadBuffer` | `ValueWriter`, `ValueWriter.cs` | |
| R1 | `PayloadWindow` and `ValueReader.OpenWindow` | internal type and member | `WireReader.Slice` | `PayloadWindow`, `OpenWindow` | |
| R1 | `ValueReader.RemainingBytes`, `ValueReader.Position`, `ValueWriter.CanSeek`, `ValueWriter.Position` setter | internal members | `WireReader.Remaining`, `WireReader.Consumed`, `WireWriter.Position` and `WireWriter.PatchInt32` | `RemainingBytes`, `CanSeek` in `Engine/` | |
| R1 | A keyed V0 write to a non-seekable destination is `NotSupportedException` | documented rule (contract §8.10, §10.2, §14.2; `CLAUDE.md`) and checkpoint V0-25 | the write succeeds and matches the seekable write; V0-25 inverted | `seekable payload stream`, `writes straight through`, `writes straight to the`, `ThrowsNotSupported` with keyed V0 | |
| R1 | `MeteredReadStream` and `WindowReadStream` as the read-side mechanisms of contract §7.1 and §7.3 | documented rule | §7.1 "Metering on read" and §7.3 "The field window" over `WireReader`; the two types have no caller left and are deleted in R2 | `Read paths compose it`, `7.3` headed `WindowReadStream` | |
| R1 | `CompositeReader`/`CompositeWriter` as readonly structs built by the engine with `new` | internal type shape | `ref struct`s created only by their static `Decode`/`Encode` entry; formatters take them by `ref` | `new CompositeReader(`, `new CompositeWriter(` | |
| R2 | `MeteredReadStream` | internal type | the read-ahead within the budget and `WireReader` over memory (contract §7.1) | `MeteredReadStream`, `MeteredReadStream.cs` | |
| R2 | `MeteredWriteStream` | internal type | the `PayloadBuffer` budget and the frame check of `EncodedFrame` before the one copy out (contract §7.2) | `MeteredWriteStream`, `MeteredWriteStream.cs` | |
| R2 | `WindowReadStream` and `WindowReadStream.SkipRemaining` | internal type and member | `WireReader.Slice` and `WireReader.Skip` (contract §7.3) | `WindowReadStream`, `SkipRemaining` | |
| R2 | `IRemainingBytes` | internal interface | `WireReader.Remaining` and `WireBudget.Exceeded` | `IRemainingBytes`, `RemainingBytes` in `src/` | |
| R2 | The per-operation `HashSet<object>` of active ancestors in `GraphWriter` | internal mechanism | the pooled ancestor stack searched by reference (contract §16) | `_activeAncestors`, `HashSet<object>(ReferenceEqualityComparer` in `Engine/` | |
| R2 | A reference table built with `new` per operation, one `Dictionary` per scope | internal mechanism | `WriteReferenceTable.Rent`/`Return` and `ReadReferenceTable.Rent`/`Return`, one map and a scope log | `new WriteReferenceTable()`, `new ReadReferenceTable()`, `List<Dictionary<` in `Engine/` | |
| R2 | `IFormatPipeline.Write(Stream, T, …)` and `Read<T>(Stream, …)` | internal members | `EncodedFrame Write<T>(T, …)` and `object? Read(Stream or ReadOnlySpan<byte>, Type, object?, …)` | `Write(Stream destination, T data, SerializationOperation` | |
| R2 | `byte[]` results of `CompressionService`, `EncryptionService` and `PayloadBufferWriter.DetachPayload` | internal members | `RentedBytes`, cleared when released | `rented.AsSpan(0, written).ToArray()`, `byte[] Compress(byte[]`, `byte[] Encrypt(byte[]` | |
| R2 | `BinaryFormatHeaderV1.BuildAssociatedData` through `MemoryStream` and `BinaryWriter` | internal implementation | `WriteAssociatedData(Span<byte>)` and `AssociatedDataLength`; `BuildAssociatedData` kept for tests and MICRO-08 | `new BinaryWriter(` in `Metadata/` | |
| R2 | `MemoryStream` on the payload path: `Serialize<T>(T)`, `Deserialize<T>(byte[])` and the pipelines | implementation | `EncodedFrame.ToArray`, the array decoded where it lies | `MemoryStream` in `BinarySerializer.cs`, `Pipeline/`, `Io/`, `Engine/`, `Security/`, `Algorithms/` | |
| R2 | Contract §7 "Security stream mechanisms" and §7.2 "`MeteredWriteStream`"; §5.10 "`MaxWireBytes` bounds physical stream bytes consumed/produced" | documented rule | §7 "Metering and windowing over buffers", §7.2 "Metering on write", §2.6 "Atomic writes" | `Security stream mechanisms`, `physical stream bytes`, `stream mechanisms (§7)` | |
| R2 | QA §21 "Security streams — `Streams/`", the `Streams/` test folder and its three test classes | checkpoint section, test layout | §21 "Metering and windowing over buffers — `Metering/`", `ReadMeteringTests`, `WriteMeteringTests`, `FieldWindowTests`, `AtomicWriteTests` | `Streams/MeteredReadStreamTests`, `Streams/MeteredWriteStreamTests`, `Streams/WindowReadStreamTests`, `Tests.Streams` | |
| R2 | `StreamMechanismBenchmarks` (MICRO-09) and the chart `components-streams.svg` | benchmark suite and artifact | `MeteringBenchmarks` and `components-metering.svg` | `StreamMechanismBenchmarks`, `components-streams`, `MICRO-09 stream mechanisms` | |
| R2 | MICRO-07 cell "table construction" | benchmark cell | "table rent and return" | `MICRO-07 table construction`, `TableConstruction` | |
| R2 | WL-03 as "a supported refusal rather than a timing"; ALLOC-05 "V0's straight-through write" | benchmark checkpoints | WL-03 timed in `ProfileStreamBenchmarks`; ALLOC-05 measures what the header and phases add | `supported refusal`, `straight-through write` | |
| R3 | `StreamExtensions` (13 methods: `stream.Serialize` and the 12 `Deserialize` extensions) | public type | `BinarySerializer` reads every source itself; keys for reading are `WithKeys` | `StreamExtensions`, `stream.Serialize(`, `source.Deserialize<`, `Extensions/StreamExtensions.cs` | |
| R3 | `BinarySerializerOptions.FromHeader` ×3 and `FromStream` ×3 | public members | `WithKeys` ×3; V1 reading already takes its algorithms from the header; `BinaryFormatInspector.Peek` to read a header alone | `FromHeader`, `FromStream` | |
| R3 | `Deserialize<T>(byte[])` | public member | `Deserialize<T>(ReadOnlySpan<byte>)` — an array converts to a span | `Deserialize<T>(byte[]`, `Deserialize<T>(byte[] bytes)` | |
| R3 | `Deserialize<T>(Stream / byte[], T existingInstance)` and `Deserialize<T>(Stream / byte[], ref T existingInstance)` | public members | `Populate` ×5 and `PopulateAsync` ×2, classes only; a struct is read with `Deserialize<T>` | `existingInstance`, `ref T existing`, `Deserialize(payload, target)`, `ExistingInstanceTests` | |
| R3 | A `null` `byte[]` read is `ArgumentNullException` | documented rule (contract §3.1, QA API-13, EXC-14 tests) | a `null` array is an empty span and `BinaryFormatException` | `Deserialize_NullByteArray_ThrowsArgumentNull`, `null bytes` | |
| R3 | "Reading needs a seekable stream": a non-seekable source is `NotSupportedException` | documented rule (contract §3.1, §8.10, §10.3; `CLAUDE.md`; XML docs) and checkpoints API-17, V0-14, OPT-18, STR-24 | a V1 frame is read to its declared length from any stream; only a V0 payload from a non-seekable stream, and any asynchronous V0 read, are `NotSupportedException` | `seekable stream so the format version`, `Reads therefore require a seekable stream`, `required seekability`, `needs a seekable stream` outside `Peek` | |
| R3 | `BinaryHeaderPeek` (`TryPeekMagicAndVersion`, `TryReadMagicAndVersion`) and the peek-and-rewind probe | internal type and mechanism | `FormatRouter.TryReadVersion` over the bytes already delivered; `FormatRouter.PrefixLength` | `BinaryHeaderPeek`, `TryPeekMagicAndVersion`, `version-detection probe`, `detecting the binary format` | |
| R3 | `BinarySerializer.SerializeTo(IBufferWriter<byte>, T)` | internal member | public `Serialize<T>(IBufferWriter<byte>, T)` | `SerializeTo(` in `tests/` and `src/` | |
| R3 | `IFormatPipeline.Read(Stream, …)` and `Read(ReadOnlySpan<byte>, …)` without `consumed`; `StreamSource.ReadAhead` and `StreamSource.Return` | internal members | `IFormatPipeline.Measure`, `Read(span / sequence, …, out long consumed)`; `Pipeline/FrameReader` and `FrameBuffer` | `ReadAhead(`, `StreamSource.Return`, `object? Read(Stream source` | |
| R3 | A span read ignoring bytes after a V1 frame or a V0 root | behaviour (contract §22.8 "does not require the source to end there" for any source) | without a bytes-consumed form a span or a sequence is exactly one frame; trailing bytes are `BinaryFormatException` | `neither required nor rejected` for spans | |
| R3 | A `Populate` root that is a back reference is `BinaryTypeException` | behaviour | `BinaryFormatException`, per the owner's decision (`Decisions.md` 9.4) | `back reference, not a first occurrence` with `BinaryTypeException` | |
| R3 | Contract §3.2 "Stream extensions", §4.3 "`FromHeader` and `FromStream`" | documented sections | §3.2 `PooledPayload`, §3.3 populate, §3.4 bytes consumed, §3.5 asynchrony; §4.3 reading a header without the payload | `## 3.2 Stream extensions`, `## 4.3 \`FromHeader\`` | |
| R3 | "an async API" in the deferred list of contract §21.3 | documented rule | the asynchronous frame-edge API of §3.5; an asynchronous engine stays rejected (R9a rewrites §21.3) | `an async API` | |
| R3 | QA §8 SX-01…SX-11; API-06…API-12; OPT-15…OPT-18; XEP-03 | checkpoints | API-21, API-24, XEP-04, XEP-05, OPT-22, SRC-05 | `SX-0`, `SX-1`, `StreamExtensionsTests`, `StreamExtensionsLimitsTests` | |
| R3 | Benchmark PROF-07 "the per-call `StreamExtensions` path"; PROF-06 "the existing-instance and `ref` entry points"; FAIR-07 "Viper's lack of an `IBufferWriter` entry point"; ALLOC-04 "the streaming family separately from the `byte[]` family" | benchmark checkpoints | PROF-06 `Populate` against `Deserialize`; PROF-07 reuse against construction; FAIR-07 per family with the buffer family; ALLOC-04 per family | `per-call \`StreamExtensions\``, `lack of an \`IBufferWriter\``, `existing-instance and \`ref\` entry points` | |
