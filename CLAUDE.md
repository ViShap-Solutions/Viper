# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Targets .NET 10 (`net10.0`), SDK 10.0.x.

```powershell
dotnet restore Viper.sln
dotnet build Viper.sln --configuration Release
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj

# Single test / class / folder
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --filter "FullyQualifiedName~Serialize_ByteArrayAndStream_ProduceIdenticalBytes"
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --filter "FullyQualifiedName~MalformedPayloadTests"
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --filter "FullyQualifiedName~.Hostile."

# Benchmarks (BenchmarkDotNet; must run Release)
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks --configuration Release
```

CI (`.github/workflows/ci.yml`) runs restore → build → the serialization test project on every PR and push to `main`, `release/**` and `support/**`. CD (`cd.yml`) fires on `v*` tags: a stable `vX.Y.Z` must point exactly at `origin/main` HEAD; a pre-release `vX.Y.Z-alpha.N`, `-beta.N` or `-rc.N` must point at a commit on a `release/*` branch of origin, and is refused once `vX.Y.Z` exists. It packs all three packages with `-p:Version=<tag minus v>` and pushes them to NuGet. Version comes solely from the tag — no version properties in the `.csproj` files.

Branches, tags, the release cycle, SemVer rules, fixture freezing and benchmark baselines are defined in `internal/Development-Workflow.md` (Russian). It binds the owner, every agent and every skill (`viper_tester`, `viper_bencher`, `viper_auditor`, `viper_auditor_next`, `viper_conformance_auditor`, `viper_refactorer`); follow it for anything about how work moves through the repository.

## Where the authoritative information lives

`docs/` is the official, consumer-facing Viper documentation, one page per subject, written from the
contract. The three package READMEs (`src/*/*-README.md`) are drawn from it, and the repository's
`README.md` links to both. Every `csharp` block in `docs/` and in the READMEs is a complete example: it
states the `using` directives it needs and compiles against the current code — a block with top-level
statements also runs; a block that only declares types compiles as a library — and a change to an
API or a behaviour updates the page that describes it in the same change. No page cites `internal/`,
names a retired API or carries an adjective about performance (`Benchmark-Plan.md` §28). Everything
below is engineering material — for the contributor and for Claude Code — and lives under `internal/`.

Every file under `internal/` carries its class at its head, and `internal/README.md` states the
classes: **normative** (the contract), **plan** (a checklist of work to do or repeat), **operational**
(instructions that describe the system and its development as they are now) and **historical** (a
record of work already done, whose rules no longer apply and which is never a reason to change the
system). Where a historical document and the contract disagree, the contract is right.

- `internal/System-Contract.md` — *normative.* **The contract and the source of truth.** Public API
  surface (§3), limits and budgets (§5–6), metering and windowing (§7), exception taxonomy (§8),
  versions and header (§10–11), compression and encryption (§12–13), member layouts (§14),
  polymorphism (§15), references (§16), the byte-level wire format (§22), the supported types with
  their encodings (§23), the release checklist (§24) and the eighteen invariants (§25). Read the
  relevant section before changing behavior; update it in the same commit when behavior changes.
- `internal/QA-Plan.md` — *plan.* The release-gate test plan, realigned with the contract. Checkpoint
  list only, staged M0–M8; §30 records confirmed defects and the resolved contract questions. The
  method for working it lives in the `viper_tester` skill, not in the plan.
- `internal/Benchmark-Plan.md` — *plan.* The post-release performance plan, realigned with the
  contract, measured against the `v1.0.0` tag and re-run per v1.x. It gates no release: correctness
  ships a version, and an open item here blocks only a performance *claim* (§28, and the quality gate
  of §24). Checkpoint list only, staged B0–B9: the competitor roster and why each library is in or
  out, the capability tiers that keep a comparison like-for-like, the data corpus, the workloads, the
  fairness rules and §27 for findings. The method for working it lives in the `viper_bencher` skill,
  not in the plan. Benchmark work is read-only over the library: it touches `benchmarks/`, the plan
  itself and `internal/performance/`, and nothing else. Its §18 component suites measure internals
  directly and are granted an `InternalsVisibleTo`.
- `internal/performance/` — *plan.* Where a measurement becomes a suggestion and stops: one
  `PERF-nn-*.md` per proposed optimization or extension point, cited to the cells that motivate it,
  for the owner to decide on. Nothing here has been applied.
- `internal/Benchmark-Graceful-Stop.md` — *plan.* A decided backlog item for the benchmark harness,
  not started.
- `internal/Development-Workflow.md` — *operational.* How work moves through the repository (Russian):
  branch kinds and where each is cut from and merged to, the alpha/beta/rc/stable cycle, where and how
  tags are set, SemVer 2 rules for API, wire and behaviour, when fixtures are frozen, when and how
  benchmark baselines are taken, hotfixes, and what an agent may and may not do.
  It is the general guide to developing Viper, independent of any current rework or plan, and it is
  kept clean: it holds no notes, rules or history for a particular agent, skill, rework or stage.
  Those belong to the plan or the skill they concern — for the rework, `internal/rework/`. An agent
  changes this file only when the owner asks for a change to the general workflow itself.
- `internal/Architecture-Audit.md`, `Audit-Refactor.md`, `Audit-Closure.md`, `Audit-Future.md` —
  *historical* (Russian). The audit that produced the architecture with the alternatives it rejected,
  the independent audit after the first refactor, the closure of its findings, and the directions it
  named.
- `internal/audit/` — *historical.* The hostile-input audit that led to the rework: the original probes
  (`Problems.cs`, superseded, does not compile), the first remediation design and its review. Kept for
  provenance; `Problems.cs` maps each finding to the QA checkpoint that now pins it.
- `internal/rework/` — *historical.* The second rework, approved by the owner and executed before
  `v1.0.0`: `Rework-Plan.md` (what was built, invariants INV-1…INV-18, the final wire format, stages
  R0–R9), one change file per governing document (`Contract-Changes.md`, `QA-Plan-Changes.md`,
  `Benchmark-Plan-Changes.md`, and `Claude-Changes.md` for this file), `Retired.md` (everything the
  rework removed or renamed, with the strings to search for) and `Conformance-Audit-Brief.md` (what
  the release conformance audit checks). `Decisions.md` (Russian) is the owner's decision record with
  the byte diagrams the plan was written from; where the plan and it disagree, it is right.
  `Owner-Review.md` (Russian) is the review of the earlier draft and the owner's decision log. Its
  rules stop applying when `v1.0.0` is released; until then it is the evidence the last reconciliation
  and the conformance audit check the system against, and the Progress table at the top of
  `Rework-Plan.md` records which stage is open.
- Operational besides the workflow: this file, `internal/README.md`, the skills under
  `.claude/skills/`, the package READMEs and `docs/`.

Current state: `src/` matches the contract. The architecture rework, its reconciliation, the consumer
documentation and the release conformance audit are done; the audit's findings are fixed on
`bugfix/v1-audit-*` branches, and the closure check (R9e) comes before `v1.0.0-rc.1`. The public API is fully
XML-documented and `GenerateDocumentationFile` is on, so the docs ship beside the assemblies. CS1591
stays a warning — `Api/PublicSurfaceTests` is what holds the line, by comparing the exported surface
with the generated XML file. The suite is 2 012 tests, green in Debug and Release.

Public XML documentation is written for the NuGet consumer reading it on hover: what the member does,
what it takes, what it returns, which exception it raises. It never cites `internal/System-Contract.md` and
never records project history.

The test project follows the layout in `internal/QA-Plan.md` §2 — `Algorithms/`, `Api/`, `Concurrency/`,
`Contracts/`, `Diagnostics/`, `Exceptions/`, `Fixtures/`, `Format/`, `Hostile/`, `Limits/`,
`Metadata/`, `References/`, `RoundTrip/`, `Metering/`. Shared helpers live in `Fixtures/` (`AssertEx`,
`Wire`, `Mutate`, `Concurrent`, `Cultures`, stream doubles) and are themselves tested. A test names no
culture and no time zone: those come from the host, so the suite is green with and without
globalization data (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`).

`Fixtures/Wire/*.bin` are the frozen v1.0.0 payloads, read by `Format/CompatibilityTests` against the
frozen shapes in `Fixtures/Compatibility.cs`. They were re-frozen exactly once, from the values in
`Format/CompatibilityTests`, when the rework's format stage changed the wire before any release — the
single recorded exception (`internal/rework/Rework-Plan.md` §0). They are never regenerated again: a
rebuilt fixture agrees with whatever the code became, so a failure there is a compatibility break, not
a fixture to refresh. `Fixtures/Wire.cs` builds and decodes frames with its own encoder, so a byte-level
test never borrows the code it checks.

Every stage M0 through M8 is closed: nothing hand-written survives, and
`Api/PublicSurfaceTests` compares the exported surface against §3 by reflection, so adding a public
type fails the build's test run until the contract lists it.

## Projects

- `ViShap.Viper.Core` — contracts only, no dependencies: attributes, the `CompressionAlgorithm`/`ChecksumAlgorithm`/`EncryptionAlgorithm` enums with their `ICompressionAlgorithm`/`IChecksumAlgorithm`/`IEncryptionAlgorithm` primitives and the `No*` pass-throughs, `SecretKey`/`IKeyProvider`, and the exception hierarchy (all derive from `BinarySerializerException`). Core carries **no policy**: no limits, no orchestration, nothing that enforces a resource ceiling.
- `ViShap.Viper.Serialization` — the entire engine, and the built-in algorithms and key providers (`DeflateCompression`, `BrotliCompression`, `Crc32Checksum`, `XxHash3Checksum`, `XxHash128Checksum`, `Aes256GcmEncryption`, `ChaCha20Poly1305Encryption`, `StaticKeyProvider`, `DelegateKeyProvider`, `HkdfKeyProvider`). Depends on Core + `System.IO.Hashing`.
- `ViShap.Viper` — meta-package, references both, ships no code.

**Namespaces do not follow the folder/assembly layout.** Everything roots at `ViShap.Viper.*` regardless of project (e.g. `src/ViShap.Viper.Serialization/Io/` → `ViShap.Viper.Io`). The *public* API (`BinarySerializer`, `BinarySerializerOptions`, `PooledPayload`, the attributes) sits in the bare `ViShap.Viper` namespace so consumers need one `using`. `GlobalUsings.cs` imports every sub-namespace, so new files in the Serialization project usually need no `using` for in-project types.

Most engine types are `internal`; `AssemblyInfo.QA.cs` grants `InternalsVisibleTo("ViShap.Viper.Serialization.Tests")`.

## Architecture

Documented normatively in `internal/System-Contract.md` §2; the reasoning behind it is in `internal/Architecture-Audit.md`. Layers, top to bottom:

```text
BinarySerializer            buffer-first surface: writes to IBufferWriter<byte>, byte[], PooledPayload,
                            Stream, PipeWriter; reads from ReadOnlySpan<byte>, ReadOnlySequence<byte>,
                            Stream, PipeReader; Populate; async only at the frame edge;
                            creates exactly one OperationState per public call (per frame)
OperationState              a struct passed by ref: Limits snapshot, Budget, PhaseBudget, Keys,
                            policies, and the traversal of the payload (reference tables, ancestors)
FormatPipeline (V0 | V1)    framing, phase order, header of service records (its bytes are the AAD), phase sizes;
                            phases are transforms over pooled buffers, the frame is built whole
Engine                      typed codecs, one per declared type (FormatterCache<T>): null, references,
                            depth, graph nodes, cycles, union tag, keyed framing, every loop over wire data
WireReader / WireWriter     ref structs over memory, the only access to payload bytes
Formatters                  shapes only: IScalarFormatter<T>, sequence / map / array shapes, typed composites
Type contracts              TypeContract<T>: member order, access, construction (ReflectedContract<T>),
                            reached only through MemberWriter / MemberReader
Algorithms                  pure mechanics over spans
```

Dependencies point strictly downwards. **No type below `Pipeline/` may reference `SerializationLimits`.**

**The reflection path states its requirements.** Codecs and type contracts are built by reflection, so
every public entry point that encodes or decodes a caller's type — the generic methods of
`BinarySerializer` and `BinaryFormatDumper` — carries `[RequiresUnreferencedCode]` and
`[RequiresDynamicCode]` with the messages of `Engine/ReflectionPath`, and no other public member does.
The requirement runs down through the pipelines to `Graph.WriteRoot`/`ReadRoot`, the engine's only
entry; inside the engine only the two caches (`FormatterCache<T>`, `TypeContractCache.Get`) suppress
the analysis, on the strength of that. Both packages build with `IsAotCompatible` and report no
trimming or AOT warning: a new method that reaches the engine takes the attributes the analyzer asks
for, never a suppression. `Api/AotAnalysisTests` (EXT-07) holds it by reflection and by building
`tests/ViShap.Viper.AotConsumer` — in the solution, not built with it, compiled by the test and never
run — under native AOT analysis.

### Three structural barriers

These are why the codebase does not carry a security check in every class. Do not work around them:

1. **Byte monopoly.** `WireReader`/`WireWriter` (`Io/`) are the only types that parse or produce payload bytes; the phases and the finished frame carry them as opaque spans. They are `ref struct`s over memory and are passed by `ref`, never stored: `WireReader` reads a span or a `ReadOnlySequence<byte>`, `WireWriter` writes into the serializer's pooled `PayloadBuffer`, and there is no stream under the engine. Fixed-size reads throw `BinaryFormatException` on truncation, strings and blobs are bounded by their limits, and every declared length is compared with `WireReader.Remaining` — exact, because the bytes are in memory — before anything is allocated. A composite formatter gets `ref CompositeReader`/`ref CompositeWriter`, which only the engine's entry creates and which expose no raw integer.
2. **Validated counts.** A loop the engine runs over wire data — container elements and keyed fields — is bounded only by an `ElementCount`, whose sole factory checks the count against its limit and charges the element or keyed-field budget; a formatter reads no count, and the header's service records are bounded by the 4 096-byte header. There is no other way to obtain one, so "read a length, then allocate" is not expressible.
3. **Engine-owned traversal.** The engine's codecs (`Engine/Codecs/`) own all recursion: null — folded into a value's first number, or a flag — and the reference frame, depth scopes, node budget, reference identity and scopes, cycle detection, the union tag, the keyed layout, and the element loop of every container. A shape receives no count and no primitive, and a type contract receives only a `MemberWriter`/`MemberReader`, which expose one member value per call and nothing else; the engine checks every contract call against the contract's description. A formatter never writes a loop over attacker-controlled data.

### Adding a formatter

A declared type resolves once to the engine's codec for it, held in the static field of `FormatterCache<T>`: `FormatterRegistry` (`Engine/`) applies its rules in order — delegates (refused), `Nullable<T>`, the scalars by exact type and enums, arrays, the fixed table of generic definitions, a concrete `ICollection<T>` with a public parameterless constructor, and last the object codec. Pick the shape, implement its interface (`Formatters/Shapes.cs`), and add it to the registry's table:

- `IScalarFormatter<T>` — a self-contained value with no children and no data-driven allocation, through the checked primitives of `WireReader`/`WireWriter`; it states its `MinimumWireSize`. A reference type begins with a length or a count that its formatter writes one higher; the engine writes its null as that number's zero.
- `ISequenceShape<TCollection, TElement, TBuilder, TEnumerator>` — count, enumerate, build and complete; the engine's `SequenceCodec` owns count, loop, depth, nodes and identity. A builder that is not the final instance sets `BuilderIsInstance = false`, a LIFO container sets `ReverseOnWrite`, a collection that keeps its elements contiguously (`List<T>`) answers `TryGetSpan` and is written from the span, and otherwise a struct enumerator keeps writing free of allocation.
- `IMapShape<TMap, TKey, TValue, TBuilder, TEnumerator>` — the same, for key/value entries.
- `IArrayShape<TCollection, TElement>` — a sequence whose elements lie in one array: the engine writes them from the span it exposes and reads them into an array of their final length, which the shape wraps.
- `ICompositeFormatter<T>` — a fixed, type-determined child layout (tuples, pairs, lazies) or an irregular one (array rank), through `CompositeReader`/`CompositeWriter`, which offer child values, a validated array shape and the elements behind it — and no raw integer.

A generic definition's shape is closed once per type; there is no cache of reflective accessors, because a shape calls the collection's own members. No shape fits a plain object: a type no rule claims is member-encoded through its `TypeContract<T>`, and there is no catch-all shape that could shadow a specific one.

The shapes, the codecs and `TypeContract<T>` are deliberately `internal` for v1.0; publishing them would freeze the traversal protocol.

### Versioned envelope

`BinarySerializer` builds one pipeline per supported version. Writing uses `options.WriteVersion` (default `BinaryFormatConstants.LatestVersion` = 1). Reading a V1 payload is *self-describing*: `FormatRouter` decodes magic `0x52455342` + the varint version from the bytes the source has already delivered and dispatches — nothing is read twice and no source rewinds; bytes without the magic are read as V0 only when `AllowV0Fallback` is set, and are otherwise rejected rather than guessed at. Every source is read from memory: a stream or a pipe is buffered by `Pipeline/FrameReader` exactly as far as the frame extends — the identifying bytes, then as much of the header as `BinaryFormatHeaderV1.TryMeasure` says it holds, then the rest of the declared frame — so **any stream is read, seekable or not**, and nothing past the frame is taken. A span or a sequence without a bytes-consumed form must be exactly one frame. Asynchronous methods await only the frame's bytes (reading) or the output (writing); the engine never awaits.

V0 and V1 are peers with different jobs, not a current format and a deprecated one — see `internal/System-Contract.md` §10.

- **V1** (`V1FormatPipeline`) — full envelope: `BinaryFormatHeaderV1` followed by the payload. The header is magic, varint version, payload mode (bit 0: references), a list of service records — `(number << 1) | critical`, length, body; checksum 1, compression 2, encryption 3, all critical, in ascending number, each at most once — and `onDiskLength`; it is at most 4 096 bytes, a format bound. An absent phase is an absent record; an unknown critical record is refused, an unknown skippable one skipped by its length. Write order is serialize → checksum over the raw payload → compress → header, whose `onDiskLength` is the exact ciphertext length → encrypt with the header's exact bytes as associated data. Read reverses it, checks the declared lengths in the order of contract §5.10 — the plaintext of an encrypted frame is not declared and is checked after decryption, before decompression — and requires the payload to be consumed exactly. Only V1 supports reference framing. No header byte can be altered without breaking the tag.
- **V0** (`V0FormatPipeline`) — the compact codec: a bare payload with no header at all, for transports that already supply their own context (private or tightly coordinated channels, IPC, protocols with their own framing). Having no header it has no reference framing and no compression/checksum/encryption phase — configured algorithms are simply not applied on a V0 write. Everything the payload itself expresses is unchanged: the full §23 type set, unions, **keyed contracts**, limits, budgets and metering, and for one value under one layout the payload bytes are identical to V1's. Like V1 it is built in the serializer's own buffer and copied to the destination once, so a keyed write works with any destination and a failed write leaves nothing in it. A V0 payload is unauthenticated by construction, so `Build()` refuses `RequireEncryption`/`RequireChecksum` together with `WithVersion(0)` or `AllowV0Fallback` — both directions, so no operation-time check is needed. Nothing declares where it ends, so reading finds the end by decoding the root: a span or a sequence is exactly one payload unless the caller uses a bytes-consumed form; a seekable stream is read ahead within the operation's budget and put back where the root ends, so a payload may be embedded in a larger stream; a stream that cannot seek is `NotSupportedException`; and every asynchronous read refuses V0 with `NotSupportedException`, because the protocol that frames a V0 message knows where it ends and the caller reads that message's bytes synchronously. Asynchronous V0 writes are allowed. Nothing in a V0 payload identifies it, which is why reading one takes an explicit `AllowV0Fallback`.

Adding a format version means a pipeline registered in `BinarySerializer`; the router picks it up. Formatters, the engine, the algorithms and the limits are untouched.

### Member layouts

`TypeContract<T>` (`Engine/Contracts/`) is the single description of a member-encoded type, used identically by reader and writer. It supplies member order, member access, construction and the response to a known key; v1.0 ships `ReflectedContract<T>`, which compiles one typed getter and setter per member, so a struct owner is assigned in place and nothing is boxed:

- **Positional** (default) — members ordered by `[BinaryOrder]` then ordinal name. Public read/write properties and public non-readonly fields are included; non-public ones need `[BinaryInclude]`; `[BinaryIgnore]` excludes. Compiler-generated fields and indexers are skipped. A delegate-typed member is **rejected** — it carries behaviour, not data — so it must be marked `[BinaryIgnore]`. Field order *is* the wire format.
- **Keyed** (`[BinaryContract]` plus `[BinaryKey(n)]` on every eligible member) — each field is written as `varint key, int32 length, payload`, sorted by key; the length is fixed-width because it is patched after the field is written, and the field count before the fields carries a keyed class's null (count + 1) when it is not reference-framed. Unknown keys are length-skipped, which is what makes schema evolution tolerant. Payload-level, so it works under both format versions; the length is patched in the serializer's buffer after the field is written, so no destination needs to seek.

The two are mutually exclusive, and every contradiction is rejected when the contract is built: `[BinaryKey]` without `[BinaryContract]`, `[BinaryOrder]`/`[BinaryInclude]` on a contract, an unmarked contract member, `[BinaryKey]` together with `[BinaryIgnore]`, `[BinaryInclude]` together with `[BinaryIgnore]`, duplicate keys or orders.

A contract built any other way — the source generator planned after v1.0 — must match the reflected
one: the conformance suite `Contracts/ConformanceTests` (CONF-01…CONF-07) reaches a contract through one
seam, `ContractOf<T>`, and pins the description and the bytes of every object shape over the partial
types of `Fixtures/Conformance`.

Polymorphism: `[BinaryUnion(tag, typeof(Derived))]` on a base class or interface; a one-byte discriminator precedes the members. Tags must fit in a byte, and only tags travel — never type names. Writing a value whose runtime type differs from the declared type **without** a union map is `BinaryTypeException`, because the reader could not reconstruct it.

Null is written once, in the first number a value begins with: `0` is null, any other value is the number plus one — a string's length, a sequence's or map's count, a keyed class's field count, a `BitArray`'s bit count, a multi-dimensional array's rank. A value with no leading number — a positional object, a union, `Tuple<…>`, `Lazy<T>`, `Nullable<T>` — carries a flag byte; `ImmutableArray<T>` folds `default` into its count. Structural numbers are minimal varints; data stays fixed-width.

References: with `PreserveReferences`, one varint frame precedes every structural reference-typed value, containers included — `0` null, `((id << 1) | 0) + 1` a first occurrence, `((id << 1) | 1) + 1` a back reference — and the number after it carries no `+ 1`. Ids are explicit, so skipping an unknown keyed field cannot desynchronise them. Ids are unique but visible only along the ancestor chain, so a back reference never crosses two sibling keyed fields and skipping an unknown field can never dangle. Without the option a cycle throws `BinaryTypeException`; it is found by searching the ancestor stack of the current path (a pooled array no deeper than `MaxDepth`), so an instance repeated along two paths is written again, not refused. The reference tables are pooled per operation and returned cleared. A member-encoded type is created through its contract — a parameterless constructor, or `default` for a struct — and registered before its members are read, so a cycle back to it resolves. The polymorphic slot is the only place the engine boxes.

### Limits and budgets

`SerializationLimits` is the public, immutable policy, validated once when options are built. `OperationState` carries the call's `SerializationBudget` — the per-operation accounting of elements, graph nodes, keyed fields and depth — and its `PhaseBudget`, the per-phase size policy; `WireReader` and `WireWriter` hold a reference to the state, so every charge lands in the one budget of the call. A declared count that the remaining bytes back is materialized at once — an array at its final length, a collection at its full capacity — and one they do not back grows as elements arrive. On read, the pipeline takes source bytes into memory within the wire budget, and the `WireReader` over them classifies running out as a limit breach when the budget cut the bytes and as malformed data otherwise; `WireReader.Slice` is the window over one declared keyed field. On write, `PayloadBuffer` refuses space past the payload budget, and the finished frame (`EncodedFrame`) is checked against the wire budget before it is copied to the destination once — so the budget counts only what the operation produces, a destination never has to seek, and a data or graph error leaves nothing in it. There is no stream decorator: metering and the field window are properties of the reader, the buffer and the frame.

Limit breaches throw `BinaryLimitException`; malformed data throws `BinaryFormatException`; unsupported versions or algorithms throw `BinaryFormatNotSupportedException`; tampering and protection downgrades throw `BinaryIntegrityException`.

### Algorithms

Each family has a public primitive (`I*Algorithm`, policy-free) and an internal service (`CompressionService`, `ChecksumService`, `EncryptionService`) that the pipeline calls **inside** the phase barrier. A primitive has one method per direction and no default members — a member added after v1.0 comes with a default implementation, the only additive path. Compression writes into an `IBufferWriter<byte>` the service supplies (bounded by `MaxCompressedBytes` on the way out, by the declared length on the way in) and must produce exactly `expectedLength` bytes; a checksum states `HashSizeInBytes` (1…255); a cipher states `KeySizeInBytes` and an exact `GetCiphertextLength`, always takes associated data, and returns what it wrote. The services hold every algorithm to what it states, and a breach is `BinaryConfigurationException` (§8.1 of the contract).

The built-ins carry their family as a suffix — `DeflateCompression`, `BrotliCompression`, `Crc32Checksum`, `XxHash3Checksum`, `XxHash128Checksum`, `Aes256GcmEncryption`, `ChaCha20Poly1305Encryption` — so none collides with a BCL type; the enum members (`ChecksumAlgorithm.Crc32`, …) keep the short names. `ChaCha20Poly1305Encryption` where the platform lacks it is `BinaryFormatNotSupportedException` at `Build()` and on read. Because the ciphertext length is exact, an encrypted frame is sized, budget-checked and keyed before the destination is touched, and the cipher then writes straight into the destination (`Pipeline/SealedBody`); a buffer writer is advanced only once the whole frame is in it. Custom algorithms are registered on the options builder and snapshotted into an `AlgorithmCatalog`; there is no process-wide registry, and built-ins cannot be substituted.

Key material is a `SecretKey` (always an owned copy) obtained from an `IKeyProvider`. A fixed key given to `WithEncryption` is checked against `KeySizeInBytes` at `Build()`; a provided key when it is resolved. `HkdfKeyProvider` derives one key per key id (HKDF-SHA-256, id as info) from a root key it never exposes. The serializer never zeroes memory it does not own.

### Diagnostics

`ViShap.Viper.Diagnostics` reads a frame for a person, the way JSON can be read by eye: `BinaryFormatDumper.DumpHeader` renders the header; `Dump` undoes the phases with the options' keys and shows the payload as hex; `Dump<T>` reads it as `T` into a `BinaryDump` — a tree of `BinaryDumpNode` with the offset, length, kind and value of every value, and on failure the tree up to it with the offset and path of the failing member (`Order.Lines[2].Note`); `DumpValue<T>` writes and dumps; `Compare<T>` finds the first differing node. It rides on the engine's trace seam: `OperationState.Trace`, an internal observer that is `null` for every serializer call and set only by the dumper, reported to only by the codecs, handed offsets, names, kinds and generic values and never a reader or a byte. The dumper is the one place a failure becomes output, and it catches only `BinarySerializerException`. When a compatibility test fails, a dump of the fixture is the first thing to look at.

## Conventions

- Tests are xUnit, `[Fact]`-based, named `Method_Scenario_Expectation`, organised by concern into the `internal/QA-Plan.md` §2 folders listed above, with shared types in `Fixtures/`.
- Work happens on short branches (`feature/`, `bugfix/`, `rework/`, `test/`, `benchmark/`, `audit/`,
  `docs/`, `ci/`) cut from the open `release/vX.Y.0` and merged back into it via PR; `release/` merges
  into `main` at release, and `hotfix/` goes from a release tag into `main` — `internal/Development-Workflow.md`.
- Any change to what goes on the wire (formatter encoding, header fields, member ordering, reference framing) is a compatibility break unless it goes behind a new format version or a keyed contract.
- Exception constructors keep the inner exception on the same line as the message, never on its own line.
- Do not add `catch (BinarySerializerException) { throw; }` unless the catch performs real cleanup.
- Comments describe what the code does, for the NuGet consumer reading XML docs on hover or the next
  engineer reading the file. Nothing in `src/` or `tests/` addresses the reader personally or records
  history — no "note:", no "before the fix", no audit or refactoring narrative. Findings and the
  reasoning behind a decision belong in `internal/`.
- The repository owner makes every commit. Never commit, push, or open a PR. Leave finished work in
  the working tree and report the changed paths.
- Whenever the work reaches a natural commit point — a QA-plan section or stage closed, a defect
  fixed, a session wrapped up — end the report with a ready commit message in English. Keep it
  terse, as the history is: one subject line, optionally a short clause after a dash naming the
  consequence. No body, no bullet list, no trailer. The detail belongs in the report and in `internal/`.
