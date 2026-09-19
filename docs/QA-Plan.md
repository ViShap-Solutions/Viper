# ViShap.Viper — QA Plan

**Target release:** v1.0.0
**Status:** Release-gate test plan, realigned with the reworked architecture
**Framework:** xUnit 2.9.3 · `net10.0`
**Scope:** production `src/` behavior
**Normative source:** `System-Contract.md` — every item below cites the section it proves

---

# 1. How to read this plan

This is a **checkpoint list**, worked through incrementally. It contains items and gates, nothing else: assertion discipline, failure triage, escalation and style live in the tester skill.

Every item has the form:

```text
- [ ] ID — the behavior that must be proven  (§contract-section)
```

Rules that govern the boxes:

- A box is ticked only when a test exists, runs, and asserts exactly the cited clause.
- An item whose contract clause is missing or ambiguous is marked **`BLOCKED (Qn)`** and recorded in §30.
- An item is never deleted to make a gate pass. It is rewritten, split, or blocked.
- Benchmarks are out of scope; `Benchmark-Plan.md` owns them and nothing here may assert performance.

Test-layer vocabulary used by the suites:

| Layer | Meaning |
|---|---|
| **L1** | Public contract. Consumes the library as an external application would. |
| **L2** | Internal invariants through `InternalsVisibleTo` — budgets, streams, contracts, caches. |
| **L3** | Property, metamorphic and malformed-corpus tests. |
| **L4** | Real parallel execution. |

---

# 2. Target test project layout

```text
tests/ViShap.Viper.Serialization.Tests/
  Api/              Api, Options, StreamExtensions, CrossEntryPoint
  Exceptions/       taxonomy, configuration validation, inner-exception preservation
  Format/           V1 header, V1 envelope, V0 envelope, routing, wire format (§22)
  Contracts/        member plans, attributes, polymorphism, keyed evolution
  References/       identity, scopes, cycles
  RoundTrip/        the §23 type corpus, by family
  Limits/           limits, budgets, depth, nodes, phases
  Streams/          MeteredReadStream, MeteredWriteStream, WindowReadStream
  Algorithms/       compression, checksum, encryption, algorithm catalog
  Metadata/         inspector, header info, FromHeader / FromStream
  Diagnostics/      BinaryFormatDumper
  Concurrency/      caches, shared serializer, parallel operations
  Hostile/          malformed corpus, truncation, amplification, property tests
  Fixtures/         shared types, frame builders, mutation helpers, stream doubles
  Fixtures/Wire/    committed *.bin compatibility fixtures (already wired in the .csproj)
```

The `Api/`, `Correctness/` and `Security/` folders that exist today are replaced by this layout during **M0**.

---

# 3. Stages and gates

Work proceeds stage by stage. A stage closes when every one of its items is `[x]` or `BLOCKED (Qn)`, and `dotnet test` is green for the whole project.

| Stage | Content | Suites | Gate |
|---|---|---|---|
| **M0** | Foundation | §5 baseline reconciliation, §29 utilities | Layout exists, helpers tested, no test file outside it; the two API smoke files survive until M1 replaces them |
| **M1** | Public surface | §6 API, §7 options, §8 stream extensions | Every public overload of §3 exercised; §3 surface confirmed complete |
| **M2** | Failure taxonomy | §9 exceptions, §10 configuration validation | Every branch of the §8 hierarchy pinned to a cause |
| **M3** | Format | §11 header, §12 envelope/canonicity, §13 V0 and routing, §14 wire format | A conforming reader could be written from the tests alone |
| **M4** | Type system | §15 contracts, §16 keyed evolution, §17 polymorphism, §18 references and cycles | Every §14–§16 invariant pinned |
| **M5** | Corpus | §19 round trip across §23 | Every supported type family round-trips in V1, and in V0 where V0 supports the shape |
| **M6** | Resources | §20 limits and budgets, §21 streams, §22 hostile input | Every limit has below / exact / above / invalid |
| **M7** | Algorithms | §23 compression, §24 checksum, §25 encryption, §26 catalog | Every phase boundary and every key-ownership rule pinned |
| **M8** | Periphery | §27 inspection and diagnostics, §28 concurrency and caches, §29 utilities, §31 cross-entry-point and property corpus | Full suite green; §32 release gate evaluated |

---

# 4. Configuration profiles

The profiles the suites reference. Built with the real builder surface (§4.1 of the contract).

## P0 — default

```csharp
new BinarySerializer()                 // BinarySerializerOptions.Default
```

- [ ] P0-01 — V1 write, V1 read, no compression, no checksum, no encryption *(§4.2)*
- [ ] P0-02 — `Configure().Build()` is semantically equivalent to `Default` *(§4.2)*

## P1 — preserved references

```csharp
BinarySerializerOptions.Configure().PreserveReferences().Build()
```

- [ ] P1-01 — reference framing is active and the header records it *(§16, §22.2)*

## P2 — tight limits

```csharp
SerializationLimits.Default with
{
    MaxDepth = 4, MaxArrayLength = 3, MaxCollectionLength = 3, MaxDictionaryEntries = 2,
    MaxStringBytes = 8, MaxByteBlobBytes = 8, MaxTotalElements = 5, MaxObjectGraphNodes = 5,
    MaxKeyedFields = 3, MaxTotalKeyedFields = 6,
    MaxPayloadBytes = 64, MaxCompressedBytes = 64, MaxEncryptedBytes = 128, MaxWireBytes = 256
}
```

- [ ] P2-01 — the profile builds and every limit in it is reachable by a crafted payload *(§5)*

## P3 — compression

- [ ] P3-01 — `Deflate` *(§12)*
- [ ] P3-02 — `Brotli` *(§12)*

## P4 — checksum

- [ ] P4-01 — `Crc32` *(§11, §22.6)*

## P5 — encryption

- [ ] P5-01 — `Aes256Gcm` with a fixed 32-byte key *(§13)*
- [ ] P5-02 — `Aes256Gcm` with a key resolver and a `KeyId` *(§13.2)*
- [ ] P5-03 — `RequireEncryption` + `RequireChecksum` *(§21.1)*

## P6 — full V1

- [x] P6-01 — Brotli + Crc32 + Aes256Gcm *(§22.6)* — `RoundTrip/ProtectedCorpusTests`, over the whole §19 corpus
- [ ] P6-02 — Deflate + Crc32 + Aes256Gcm *(§22.6)*

## P7 — V0

The compact profile: no envelope, no algorithm phases, positional members only. It is measured as a
peer of P1–P6, not as a degraded mode.

```csharp
BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build()
```

- [x] P7-01 — V0 write and V0 read *(§10.2, §22.8)* — `RoundTrip/HeaderlessCorpusTests`, over the whole §19 corpus
- [ ] P7-02 — V0 write and V0 read of a `[BinaryContract]` type, against the same type under P1 *(§14.2)*

---

# 5. Baseline reconciliation — M0

The 94 tests present today were written by hand as smoke checks, before this plan existed. Each file is re-homed, rewritten, or deleted. Nothing is counted as coverage until it has been compared with the clause it claims to prove.

- [x] BASE-01 — `API/BinarySerializerApiTests.cs` (8 tests): entry-point smoke checks with no boundary or failure assertions → superseded by §6 and §31; delete after §6 is green — `deleted; replaced by Api/SerializerApiTests and Api/ExistingInstanceTests`
- [x] BASE-02 — `API/StreamExtensionsApiTests.cs` (13 tests): same → superseded by §8; delete after §8 is green — `deleted; replaced by Api/StreamExtensionsTests`
- [x] BASE-03 — `Correctness/RoundTripCorpusTests.cs` (17 tests): family-level round trips → split into the per-family suites of §19; keep the assertions that already check runtime type and ordering — `RoundTrip/Corpus` (the two M0 holding files it first became were absorbed by M5)
- [x] BASE-04 — `Correctness/TypeContractTests.cs` (17 tests): contract, polymorphism, reference and populate-in-place behavior → split into §15, §17, §18 — `Contracts/, References/, Api/ExistingInstanceTests`
- [x] BASE-05 — `Correctness/WireFormatTests.cs` (9 tests): byte-level pins → move to §14, extend to every row of §22 — `Format/WireFormatTests`
- [x] BASE-06 — `Security/CryptoContractTests.cs` (14 tests): crypto and compression contract → split into §23 and §25 — `Algorithms/CompressionTests, Algorithms/EncryptionTests`
- [x] BASE-07 — `Security/HostileInputTests.cs` (13 tests): hostile corpus → split into §20, §21, §22 — `Limits/, Streams/, Hostile/`
- [x] BASE-08 — `Fixtures/Basic.cs`, `Fixtures/Wire.cs` → become the shared fixture base of §29; `Wire.Frame` gains the parameters the §11 header suite needs — `Fixtures/AssertEx, Wire, Mutate, Streams`
- [x] BASE-09 — no test file remains outside the §2 layout (`Streams/` and `Hostile/` already started by the D1 fix) — `Correctness/ and Security/ removed`
- [x] BASE-10 — every surviving test names the checkpoint it proves — `every suite names its checkpoints`

---

# 6. Public API — `Api/`

Surface under test: the `BinarySerializer` overloads of §3.1 as compiled.

- [x] API-01 — `new BinarySerializer()` uses `BinarySerializerOptions.Default` *(§3.1, §4.2)* — `Api/SerializerApiTests`
- [x] API-02 — `new BinarySerializer(null)` is equivalent to the parameterless form *(§3.1)* — `Api/SerializerApiTests`
- [x] API-03 — `new BinarySerializer(options)` with invalid limits throws `BinaryConfigurationException` at construction *(§5)* — `Api/SerializerApiTests`
- [x] API-04 — `byte[] Serialize<T>(T)` and `void Serialize<T>(Stream, T)` produce identical bytes *(§3.1)* — `Api/SerializerApiTests`
- [x] API-05 — `T? Deserialize<T>(byte[])` and `T? Deserialize<T>(Stream)` produce equal results *(§3.1)* — `Api/SerializerApiTests`
- [x] API-06 — `Deserialize<T>(byte[])` on an empty array throws `BinaryFormatException` *(§3)* — `Api/EmptyPayloadTests`
- [x] API-07 — `Deserialize<T>(byte[], T existing)` on an empty array throws and leaves the instance untouched *(§3)* — `Api/EmptyPayloadTests`
- [x] API-08 — `Deserialize<T>(byte[], ref T existing)` on an empty array throws and leaves the value untouched *(§3)* — `Api/EmptyPayloadTests`
- [x] API-09 — `Deserialize<T>(Stream, T existing)` populates and returns the same instance *(§3)* — `Api/ExistingInstanceTests`
- [x] API-10 — `Deserialize<T>(…, T existing)` on a formatter-claimed type throws `BinaryTypeException` *(§3)* — `Api/ExistingInstanceTests`
- [x] API-11 — `Deserialize<T>(…, ref T existing)` restores every member of a struct, including a primitive root *(§3, C01)* — `Api/ExistingInstanceTests`
- [x] API-12 — `Deserialize<T>(…, ref T existing)` is correct under `PreserveReferences` framing *(§3, A02)* — `Api/ExistingInstanceTests`
- [x] API-13 — null `destination`, `source`, `bytes` or `existingInstance` throw `ArgumentNullException` *(§8.10)* — `Api/SerializerApiTests`
- [x] API-14 — a caller stream is never disposed by serialize or deserialize *(§20)* — `Api/SerializerApiTests`
- [x] API-15 — a caller stream is never rewound; serialization appends at the current position *(§3.1, §20)* — `Api/SerializerApiTests`
- [x] API-16 — deserialization reads only as far as the payload extends *(§3.1)* — `Api/SerializerApiTests`
- [x] API-17 — reading from a non-seekable stream throws `NotSupportedException` *(§8.10, §10.3)* — `Api/SerializerApiTests`
- [x] API-18 — writing to a non-seekable stream succeeds for a positional payload *(§7.2)* — `Api/SerializerApiTests`
- [x] API-19 — a serializer instance is reusable across calls with no state carried over *(§2.2)* — `Api/SerializerApiTests`
- [x] API-20 — a failed operation leaves the stream position where the failure occurred, and the next independent call still succeeds *(§20, §2.2)* — `Api/SerializerApiTests`

---

# 7. Options and configuration — `Api/`

- [x] OPT-01 — `BinarySerializerOptions.Default` exposes the documented defaults *(§4.2)* — `Api/OptionsTests`
- [x] OPT-02 — options properties have no public setters; `Configure()…Build()` is the only construction path *(§2.1)* — `Api/OptionsTests`
- [x] OPT-03 — `WithCompression` / `WithChecksum` are reflected in the built options and in the header *(§4.1, §11)* — `Api/OptionsTests`
- [x] OPT-04 — `WithEncryption(alg, ReadOnlySpan<byte>, keyId)` copies the key immediately *(§13.2)* — `Api/OptionsTests`
- [x] OPT-05 — `WithEncryption(alg, Func<string?, byte[]?>, keyId)` builds a resolver-backed provider *(§4.1)* — `Api/OptionsTests`
- [x] OPT-06 — `WithEncryption(alg, IKeyProvider, keyId)` uses the supplied provider *(§4.1)* — `Api/OptionsTests`
- [x] OPT-07 — `WithLimits(null)` throws `ArgumentNullException` *(§8.10)* — `Api/OptionsTests`
- [x] OPT-08 — `WithLimits` stores the instance it was given, and `SerializationLimits` is an immutable record, so no later change to it is expressible *(§2.1, §5)* — `Api/OptionsTests`
- [x] OPT-09 — `PreserveReferences`, `AllowV0Fallback`, `RequireEncryption`, `RequireChecksum` default to `false` and flip with the no-argument overload; the fallback is exercised on its own, since it cannot be combined with a protection policy *(§4.1, §4.2)* — `Api/OptionsTests`
- [x] OPT-10 — `WithVersion(n)` for an unsupported `n` → `BinaryConfigurationException` at `Build()` *(§4.1)* — `Api/WriteVersionTests`
- [x] OPT-11 — `RegisterCustomCompression` / `Checksum` / `Encryption` are snapshotted into the options' catalog *(§4.1)* — `Api/OptionsTests`
- [x] OPT-12 — registration is per-configuration: a second options instance built without it cannot resolve the custom name *(§4.1)* — `Api/OptionsTests`
- [x] OPT-13 — a custom registration cannot substitute a built-in algorithm *(§4.1)* — `Api/OptionsTests`
- [x] OPT-14 — a null name or null factory in a registration throws `ArgumentNullException` *(§8.10)* — `Api/OptionsTests`
- [x] OPT-15 — `FromHeader(...)` builds options matching the header metadata *(§4.3)* — `Api/OptionsTests`
- [x] OPT-16 — `FromHeader` with invalid limits throws `BinaryConfigurationException` *(§4.3)* — `Api/OptionsTests`
- [x] OPT-17 — `FromStream(...)` peeks through `BinaryFormatInspector` and restores the position *(§4.3, §19)* — `Api/OptionsTests`
- [x] OPT-18 — `FromStream` on a non-seekable stream throws `NotSupportedException` *(§4.3)* — `Api/OptionsTests`
- [x] OPT-19 — a key resolver receives the header's `KeyId` *(§4.3, §13.2)* — `Api/OptionsTests`
- [x] OPT-20 — `KeyId` is never treated as key material *(§4.3, §8.7)* — `Api/OptionsTests`
- [x] OPT-21 — `SerializationLimits` is a record: `Default with { … }` derives a policy and `Validate()` is public *(§5)* — `Api/OptionsTests`

---

# 8. Stream extensions — `Api/`

The 13 public overloads of `StreamExtensions`, enumerated in contract §3.2.

- [x] SX-01 — `Serialize<T>(this Stream, T, BinarySerializerOptions?)` matches `BinarySerializer.Serialize` byte for byte *(§3)* — `Api/StreamExtensionsTests`
- [x] SX-02 — `Deserialize<T>(this Stream, BinarySerializerOptions)` matches the serializer overload *(§3)* — `Api/StreamExtensionsTests`
- [x] SX-03 — `Deserialize<T>(this Stream)` configures itself from the header *(§4.3)* — `Api/StreamExtensionsTests`
- [x] SX-04 — `Deserialize<T>(this Stream, byte[]? key)` decrypts with the supplied key *(§4.3, §13)* — `Api/StreamExtensionsTests`
- [x] SX-05 — `Deserialize<T>(this Stream, Func<string?, byte[]?>)` resolves by header `KeyId` *(§4.3, §13.2)* — `Api/StreamExtensionsTests`
- [x] SX-06 — the three existing-reference-instance overloads populate in place *(§3)* — `Api/StreamExtensionsTests`
- [x] SX-07 — the three `ref struct` overloads assign the value read *(§3)* — `Api/StreamExtensionsTests`
- [x] SX-08 — every overload leaves the caller's stream open and undisposed *(§20)* — `Api/StreamExtensionsTests`
- [x] SX-09 — every overload rejects a null stream / resolver with `ArgumentNullException` *(§8.10)* — `Api/StreamExtensionsTests`
- [x] SX-10 — header-derived overloads apply the caller's `limits`, or the defaults when omitted *(§3.2)* — `Api/StreamExtensionsLimitsTests`
- [x] SX-11 — a header-derived overload cannot be used to bypass a caller's configured limits *(§3.2)* — `Api/StreamExtensionsLimitsTests`

---

# 9. Exception taxonomy — `Exceptions/`

## 9.1 Hierarchy

- [x] EXC-01 — the hierarchy matches §8 exactly, branch for branch *(§8)* — `Exceptions/ExceptionHierarchyTests`
- [x] EXC-02 — `BinaryLimitException` derives from `BinaryFormatException` *(§8.3)* — `Exceptions/ExceptionHierarchyTests`
- [x] EXC-03 — `BinaryEncryptionKeyException` derives from `BinaryEncryptionException` *(§8.7)* — `Exceptions/ExceptionHierarchyTests`
- [x] EXC-04 — every Viper exception derives from `BinarySerializerException` and none from another framework base *(§8)* — `Exceptions/ExceptionHierarchyTests`, over every type in both assemblies, not only the exported ones
- [x] EXC-05 — every exception type is public and catchable from an external assembly *(§3)* — `Exceptions/ExceptionHierarchyTests`

## 9.2 Cause → type mapping

- [x] EXC-06 — malformed structure → `BinaryFormatException` *(§8.2)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-07 — parseable but over a configured ceiling → `BinaryLimitException` *(§8.3)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-08 — recognized but unsupported version or algorithm → `BinaryFormatNotSupportedException` *(§8.4)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-09 — checksum or AEAD tag failure → `BinaryIntegrityException` *(§8.5)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-10 — key missing, unresolvable or mismatched → `BinaryEncryptionKeyException` *(§8.7)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-11 — caller-stream I/O failure → `BinaryStreamException` *(§8.8)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-12 — invalid CLR type, contract or graph semantics → `BinaryTypeException` *(§8.9)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-13 — invalid configuration → `BinaryConfigurationException` *(§8.1)* — `Exceptions/ExceptionMappingTests`, `Exceptions/ConfigurationValidationTests`
- [x] EXC-14 — a null public argument → `ArgumentNullException`, never a Viper type *(§8.10)* — `Exceptions/ExceptionMappingTests`
- [x] EXC-15 — a required capability such as seekability → `NotSupportedException` *(§8.10)* — `Exceptions/ExceptionMappingTests`

## 9.3 Leakage and preservation

- [x] EXC-16 — `EndOfStreamException` never escapes a truncated read *(§8.2)* — `Exceptions/ExceptionLeakageTests`, every proper prefix of a V1 and a V0 payload
- [x] EXC-17 — `ArgumentException` from a parser never escapes *(§24)* — `Exceptions/ExceptionLeakageTests`
- [x] EXC-18 — `IOException` is preserved as `BinaryStreamException.InnerException` *(§9)* — `Exceptions/ExceptionLeakageTests`
- [x] EXC-19 — `CryptographicException` is preserved as `BinaryIntegrityException.InnerException` *(§9)* — `Exceptions/ExceptionLeakageTests`
- [x] EXC-20 — `InvalidDataException` is preserved as `BinaryFormatException.InnerException` *(§9)* — `Exceptions/ExceptionLeakageTests`
- [x] EXC-21 — no production path wraps the whole codec operation in a blanket `IOException` catch *(§8.8, §10.3)* — `Exceptions/SourceInvariantTests`
- [x] EXC-22 — no production path contains a bare `catch (BinarySerializerException) { throw; }` *(§9)* — `Exceptions/SourceInvariantTests`

---

# 10. Configuration validation — `Exceptions/`

- [x] CFG-01 — every limit at `0` → `BinaryConfigurationException`, one test per limit *(§5)* — `Exceptions/ConfigurationValidationTests`, discovered by reflection over every numeric limit
- [x] CFG-02 — every limit negative → `BinaryConfigurationException`, one test per limit *(§5)* — `Exceptions/ConfigurationValidationTests`
- [x] CFG-03 — the message names the offending limit *(§5)* — `Exceptions/ConfigurationValidationTests`
- [x] CFG-04 — a valid positive policy builds *(§5)* — `Exceptions/ConfigurationValidationTests`
- [x] CFG-05 — `RequireEncryption` without an encryption algorithm → `BinaryConfigurationException` *(§4.1)* — `Algorithms/EncryptionTests`
- [x] CFG-06 — `RequireEncryption` with an algorithm reporting `AuthenticatesAssociatedData == false` → `BinaryConfigurationException` *(§4.1, §13.1)* — `Exceptions/ConfigurationValidationTests`
- [x] CFG-07 — no builder overload can leave an encryption algorithm without key material *(§4.1, §8.7)* — `Exceptions/ConfigurationValidationTests`; the reader-side form is `BinaryEncryptionKeyException`, see EXC-10. Rewritten under Q10
- [x] CFG-08 — `RequireChecksum` without a checksum algorithm → `BinaryConfigurationException` *(§4.1)* — `Exceptions/ConfigurationValidationTests`
- [x] CFG-09 — validation happens exactly once, when options are built *(§2.1)* — `Exceptions/SourceInvariantTests` pins `Validate()` to the configuration boundaries; `Api/OptionsTests` proves the builder validates
- [x] CFG-10 — the default policy matches the §5 table value for value *(§5)* — `Exceptions/ConfigurationValidationTests`
- [x] CFG-11 — a protection policy with `WithVersion(0)` → `BinaryConfigurationException` *(§4.1, §10.2)* — `Api/OptionsTests`, see D2-01 and D2-03
- [x] CFG-12 — a protection policy with `AllowV0Fallback` → `BinaryConfigurationException` *(§4.1, §10.2)* — `Api/OptionsTests`, see D2-02 and D2-03
- [x] CFG-13 — an algorithm configured under version 0 without a policy builds *(§4.1, §21.1)* — `Api/OptionsTests`, see D2-04

---

# 11. V1 header — `Format/`

Field order, types and invariants per §22.6.

- [x] HDR-01 — a written header has the documented field order and byte layout *(§22.6)* — `Format/WireFormatTests`
- [x] HDR-02 — magic mismatch → `BinaryFormatException` *(§22.6)* — `Format/HeaderTests`
- [x] HDR-03 — an unknown version → `BinaryFormatNotSupportedException` *(§22.6)* — `Format/HeaderTests`
- [x] HDR-04 — truncation at every prefix length of the fixed header → `BinaryFormatException` *(§11)* — `Format/HeaderTests`
- [x] HDR-05 — an undefined compression identifier → `BinaryFormatNotSupportedException` *(§22.6)* — `Format/HeaderTests`
- [x] HDR-06 — an undefined checksum identifier → `BinaryFormatNotSupportedException` *(§22.6)* — `Format/HeaderTests`
- [x] HDR-07 — an undefined encryption identifier → `BinaryFormatNotSupportedException` *(§22.6)* — `Format/HeaderTests`
- [x] HDR-08 — optional strings: absent, empty, and populated all round-trip *(§22.1)* — `Format/HeaderTests`
- [x] HDR-09 — an optional string declaring a length beyond the stream → `BinaryFormatException` before allocation *(§2.3, §17)* — D1-02
- [x] HDR-10 — a header string over the 256-byte format ceiling → `BinaryFormatException`, and an unwritable configured value → `BinaryConfigurationException` *(§11, §22.6)* — `Format/HeaderStringTests`
- [x] HDR-11 — a negative `UncompressedLength` / `CompressedLength` / `OnDiskLength` → `BinaryFormatException` *(§22.6)* — `Format/HeaderTests`
- [x] HDR-12 — each length above its phase limit → `BinaryLimitException`, before allocation *(§22.6, §17)* — `Format/HeaderTests`
- [x] HDR-13 — `Compression == None` with `CompressedLength != UncompressedLength` → `BinaryFormatException` *(§11)* — `Format/HeaderTests`
- [x] HDR-14 — `Encryption == None` with `OnDiskLength != CompressedLength` → `BinaryFormatException` *(§11)* — `Format/HeaderTests`
- [x] HDR-15 — `OnDiskLength` beyond the bytes physically present → `BinaryFormatException` before allocation *(§17)* — D1-01
- [x] HDR-16 — `checksumLength` round-trips, including `0` and `255` *(§22.6)* — `Format/HeaderTests`
- [x] HDR-17 — a checksum truncated below its declared length → `BinaryFormatException` before allocation *(§11)* — D1-03
- [x] HDR-18 — a checksum longer than the byte representation allows → `BinaryConfigurationException` on write *(§11)* — `Format/HeaderTests`
- [x] HDR-19 — `PreserveReferences` in the header, not the local configuration, decides payload interpretation *(§2.2, §16)* — `Format/HeaderTests`

---

# 12. V1 envelope and canonicity — `Format/`

- [x] ENV-01 — write order is serialize → checksum raw → compress → AAD → encrypt → header *(§22.6)* — `Format/EnvelopeTests`
- [x] ENV-02 — read reverses that order *(§22.6)* — `Format/EnvelopeTests`
- [x] ENV-03 — trailing bytes after the root value → `BinaryFormatException` *(§10.1)* — `Hostile/MalformedPayloadTests`
- [x] ENV-04 — a payload shorter than the root value demands → `BinaryFormatException` *(§10.1)* — `Format/EnvelopeTests`
- [x] ENV-05 — extra bytes **after** the declared `OnDiskLength` in the source stream are not consumed and not an error *(§3.1, §20)* — `Format/EnvelopeTests`
- [x] ENV-06 — a decompressed payload shorter than declared → rejected *(§12)* — `Format/EnvelopeTests`
- [x] ENV-07 — a decompressed payload longer than declared → rejected *(§12)* — `Format/EnvelopeTests`
- [x] ENV-08 — the declared plaintext length never exceeds the ciphertext delivered *(§13)* — `Format/EnvelopeTests`
- [x] ENV-09 — `MaxWireBytes` is charged relative to the operation's start position on write *(§7.2)* — `Streams/MeteredWriteStreamTests`
- [x] ENV-10 — `MaxWireBytes` is charged from zero on read regardless of the source's absolute position *(§7.1)* — `Format/EnvelopeTests`

---

# 13. V0 and routing — `Format/`

V0 is a compact codec in its own right, not a compatibility shim (§10.2). These checkpoints prove two
things about it: that it encodes the same graphs V1 does minus what only a header could have carried
— reference framing and the algorithm phases — and that it is never selected by inference, an
unidentified stream being V0 only because the caller said so.

- [x] V0-01 — V0 writes a headerless payload with no magic *(§22.8)* — `Format/WireFormatTests`
- [x] V0-02 — V0 round-trips positional data *(§10.2)* — `Format/V0FormatTests`
- [x] V0-03 — a `[BinaryContract]` type round-trips on V0 *(§10.2, §14.2)* — `Format/V0FormatTests`
- [x] V0-04 — a keyed payload written on V0 is read by a V0 reader whose schema has moved on; the unknown key is length-skipped *(§10.2, §14.2)* — `Format/V0FormatTests`
- [x] V0-05 — V0 ignores `PreserveReferences`; a cycle is a `BinaryTypeException`, not a reference frame *(§10.2, §16)* — `Format/V0FormatTests`
- [x] V0-06 — V0 may be embedded: bytes after the payload are neither required nor rejected *(§22.8)* — `Format/V0FormatTests`
- [x] V0-07 — `MaxPayloadBytes` applies to V0 on read *(§7.1, S05)* — `Limits/BudgetTests`
- [x] V0-08 — `MaxPayloadBytes` applies to V0 on write *(§7.2)* — `Format/V0FormatTests`
- [x] V0-09 — V0 truncation → `BinaryFormatException` *(§8.2)* — `Format/V0FormatTests`
- [x] V0-10 — routing selects V1 when the magic and version are recognized *(§10.3)* — `Format/V0FormatTests`
- [x] V0-11 — routing selects V0 only when the read fallback is enabled; writing V0 does not enable it *(§10.2, §10.3)* — `Api/WriteVersionTests`
- [x] V0-12 — no magic with the fallback disabled → `BinaryFormatException` *(§10.3)* — `Format/V0FormatTests`
- [x] V0-13 — a recognized but unregistered version → `BinaryFormatNotSupportedException` *(§10.3)* — `Format/RoutingTests`
- [x] V0-14 — routing on a non-seekable stream → `NotSupportedException` *(§10.3)* — `Format/RoutingTests`
- [x] V0-15 — the version probe restores the stream position before dispatch *(§10.3)* — `Format/RoutingTests`
- [x] V0-16 — an `IOException` from the probe → `BinaryStreamException` *(§10.3)* — `Format/RoutingTests`
- [x] V0-17 — the probe matches the eight header bytes exactly, so a V0 payload that is shorter
  than the probe window or differs from the magic in any byte is not misrouted; one that literally
  opens with the magic and version 1 *is* read as V1, which is the documented consequence of a V0
  payload carrying no identity of its own *(§10.2, §10.3)* — `Format/RoutingTests`
- [x] V0-18 — committed fixed-byte V0 and V1 fixtures decode correctly; the fixture is never regenerated by the writer under test *(§10.2)* — `Format/RoutingTests`, `Fixtures/Wire/person-v0.bin`, `Fixtures/Wire/person-v1.bin`
- [x] V0-19 — a V0 payload is byte-identical to the payload a V1 frame carries for the same value under the same positional or keyed layout *(§22.8)* — `Format/RoutingTests`
- [x] V0-20 — `[BinaryUnion]` polymorphism round-trips on V0 *(§10.2, §15)* — `Format/V0CorpusTests`
- [x] V0-21 — every §23 family V0 supports round-trips through it, and each one produces the same payload bytes as V1; RT-C08 extends this to the whole corpus *(§10.2)* — `Format/V0CorpusTests`
- [x] V0-22 — `RequireEncryption` or `RequireChecksum` together with V0 is rejected when the options are built, on the write side and on the read side alike *(§4.1, §10.2, §21.1, D2)* — `Api/OptionsTests`
- [x] V0-23 — a keyed contract nested inside a keyed contract round-trips on V0, so field windowing works over the metered V0 payload and not only over V1's buffered one *(§7.3, §10.2)* — `Format/V0FormatTests`
- [x] V0-24 — a keyed V0 payload embedded in a larger stream stops at the root value and is not confused by the trailing bytes *(§22.8)* — `Format/V0FormatTests`
- [x] V0-25 — a keyed write on V0 to a destination that cannot seek → `NotSupportedException` naming the seekable payload stream, while a positional write to the same destination succeeds *(§10.2, §14.2, §8.10)* — `Format/V0FormatTests`

---

# 14. Wire format — `Format/`

Every row of §22 is pinned at the byte level. This is the section a second implementation would be written from.

## 14.1 Primitives and framing

- [x] WF-01 — each fixed-size primitive encoding of §22.1, little-endian, exact width *(§22.1)* — `Format/ScalarWireTests`
- [x] WF-02 — `decimal` is four `int32` in `GetBits` order *(§22.1)* — `Format/ScalarWireTests`
- [x] WF-03 — a string is a 7-bit length prefix then UTF-8 bytes *(§22.1)* — `Format/WireFormatTests`
- [x] WF-04 — a blob is a 7-bit length prefix then bytes *(§22.1)* — `Format/ScalarWireTests`
- [x] WF-05 — a count is a raw `int32` *(§22.1)* — `Format/WireFormatTests`
- [x] WF-06 — an optional string is a present flag then the string *(§22.1)* — `Format/WireFormatTests`
- [x] WF-07 — 7-bit integers use the shortest form on write, and boundary values round-trip *(§22)* — `Format/WireFormatTests`
- [x] WF-08 — the null flag is present for reference types and `Nullable<T>`, absent for non-nullable value types *(§22.2)* — `Format/WireFormatTests`
- [x] WF-09 — a `false` null flag ends the value with no further bytes *(§22.2)* — `Format/WireFormatTests`
- [x] WF-10 — a reference frame is a marker byte plus an `int32` id, present only under `PreserveReferences` and only for structural reference types *(§22.2)* — `Format/WireFormatTests`
- [x] WF-11 — scalars, strings included, and all value types are never reference-framed *(§22.2, §16)* — `Format/WireFormatTests`
- [x] WF-12 — marker `0` precedes the shape payload; marker `1` ends the value *(§22.2)* — `Format/WireFormatTests`
- [x] WF-13 — any other marker → `BinaryFormatException` *(§22.2)* — `Format/WireFormatTests`

## 14.2 Shapes

- [x] WF-14 — a sequence is a count then framed elements *(§22.3)* — `Format/WireFormatTests`
- [x] WF-15 — a map is an entry count then framed key/value pairs *(§22.3)* — `Format/WireFormatTests`
- [x] WF-16 — a positional object writes members in plan order: `[BinaryOrder]` ascending first, then ordinal name order *(§22.3)* — `Format/WireFormatTests`
- [x] WF-17 — a keyed object is a 7-bit field count then `key, int32 length, payload` per field *(§22.3)* — `Format/WireFormatTests`
- [x] WF-18 — keyed fields are written in ascending key order *(§22.3)* — `Format/WireFormatTests`
- [x] WF-19 — a union writes one tag byte before the member layout *(§22.3)* — `Format/WireFormatTests`
- [x] WF-20 — a field payload is exactly its declared length; trailing bytes inside a field → `BinaryFormatException` *(§22.3)* — `Format/WireFormatTests`

## 14.3 Scalar encodings

- [x] WF-21 — every row of the §22.4 table is pinned by a byte-level assertion *(§22.4)* — `Format/ScalarWireTests`
- [x] WF-22 — an enum is encoded as its underlying primitive, for every underlying type in use *(§22.4)* — `Format/ScalarWireTests`
- [x] WF-23 — `Rune` with an invalid scalar value → `BinaryFormatException` *(§22.4)* — `Format/ScalarWireTests`
- [x] WF-24 — `BitArray` is an `int32` bit count then `ceil(bits/8)` blob bytes *(§22.4)* — `Format/ScalarWireTests`

## 14.4 Composites

- [x] WF-25 — `KeyValuePair`, `Tuple`, `ValueTuple`, `Lazy<T>` per §22.5 *(§22.5)* — `Format/CompositeWireTests`
- [x] WF-26 — `ImmutableArray<T>` writes a present flag; `default` writes `false` and stays distinct from empty *(§22.5)* — `Format/CompositeWireTests`
- [x] WF-27 — a rank > 1 array writes rank, per-dimension lengths, then row-major elements *(§22.5)* — `Format/CompositeWireTests`

## 14.5 Associated data

- [x] WF-28 — the AAD image covers exactly the §22.7 fields, in order *(§22.7)* — `Format/AssociatedDataTests`
- [x] WF-29 — `OnDiskLength` is excluded from the AAD *(§22.7)* — `Format/AssociatedDataTests`
- [x] WF-30 — the AAD is recomputed, never stored on the wire *(§22.7)* — `Format/AssociatedDataTests`

---

# 15. Contracts and members — `Contracts/`

- [x] CTR-01 — public read/write properties are included *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-02 — public non-readonly fields are included *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-03 — read-only and get-only members are excluded *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-04 — compiler-generated fields, delegates and indexers are skipped *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-05 — `[BinaryIgnore]` excludes a member *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-06 — `[BinaryInclude]` includes a non-public property *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-07 — `[BinaryInclude]` includes a non-public field *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-08 — `[BinaryOrder]` fixes positional order *(§14.1, §22.3)* — `Contracts/MemberPlanTests`
- [x] CTR-09 — unordered members fall back to ordinal name order, deterministically *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-10 — duplicate `[BinaryOrder]` values → `BinaryTypeException` *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-11 — `[BinaryKey]` without `[BinaryContract]` → `BinaryTypeException` *(§14.1)* — `Contracts/MemberPlanTests`
- [x] CTR-12 — `[BinaryInclude]` together with `[BinaryIgnore]` → `BinaryTypeException` *(§14.1)* — `Contracts/AttributeContractTests`
- [x] CTR-13 — a complete keyed contract round-trips *(§14.2)* — `Contracts/KeyedContractTests`
- [x] CTR-14 — a keyed contract member with neither `[BinaryKey]` nor `[BinaryIgnore]` → `BinaryTypeException` *(§14.2)* — `Contracts/AttributeContractTests`
- [x] CTR-15 — `[BinaryContract]` with `[BinaryInclude]` → `BinaryTypeException` *(§14.2)* — `Contracts/AttributeContractTests`
- [x] CTR-16 — `[BinaryContract]` with `[BinaryOrder]` → `BinaryTypeException` *(§14.2)* — `Contracts/AttributeContractTests`
- [x] CTR-17 — duplicate `[BinaryKey]` values → `BinaryTypeException` *(§14.2)* — `Contracts/AttributeContractTests`
- [x] CTR-18 — `[BinaryKey]` together with `[BinaryIgnore]` → `BinaryTypeException`, and the member never reaches the payload *(§14.2, C05)* — `Contracts/AttributeContractTests`
- [x] CTR-19 — every contradiction is rejected when the contract is built, not on first field write *(§14.2)* — `Contracts/MemberPlanTests`, `Contracts/AttributeContractTests`
- [x] CTR-20 — a member-encoded type without a parameterless constructor → `BinaryTypeException` on read *(§23)* — `Contracts/TypeSupportTests`
- [x] CTR-21 — an interface or abstract class without a union map → `BinaryTypeException` on read *(§23, D3)* — `Contracts/AttributeContractTests`
- [x] CTR-22 — a delegate as root, member or element → `BinaryTypeException` *(§14.1, §23)* — `Contracts/DelegateMemberTests`
- [x] CTR-23 — an unsupported type is `BinaryTypeException` at first use, never silently member-encoded into nothing *(§23)* — `Contracts/TypeSupportTests`
- [x] CTR-24 — `FormatterRegistry.Resolve` returning `null` means member encoding; no catch-all shadows a specific formatter *(L2, §2.4)* — `Contracts/TypeSupportTests`
- [x] CTR-25 — a struct containing a reference member round-trips *(§23)* — `Contracts/MemberPlanTests`
- [x] CTR-26 — nested member-encoded graphs of three or more formatter families round-trip *(§23)* — `Contracts/MemberPlanTests`

---

# 16. Keyed schema evolution — `Contracts/`

- [x] KEY-01 — the same schema round-trips *(§14.2)* — `Contracts/KeyedContractTests`
- [x] KEY-02 — a member removed from the reader's schema is skipped by declared length *(§14.2)* — `Contracts/KeyedContractTests`
- [x] KEY-03 — a member absent from the payload keeps its CLR default *(§14.2)* — `Contracts/KeyedContractTests`
- [x] KEY-04 — an unknown key between two known keys is skipped without disturbing them *(§14.2)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-05 — an unknown field whose payload is a nested structure is skipped whole *(§14.2, §7.3)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-06 — an unknown field is skipped in bounded chunks, never copied into one attacker-sized array *(§7.3)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-07 — an unknown field is skipped without resolving a formatter for its unavailable type *(§14.2)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-08 — a truncated unknown field → `BinaryFormatException` *(§14.2)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-09 — a duplicate key on the wire → `BinaryFormatException` *(§14.2)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-10 — a malformed 7-bit key encoding → `BinaryFormatException` *(§22.1)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-11 — key value boundaries: `0`, `127`, `128`, and the largest supported key *(§14.2)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-12 — a known field is decoded through a window and cannot read into the next field *(§7.3)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-13 — a known field decoded through a window shares the parent budget and reference state *(§7.3)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-14 — a polymorphic member inside a keyed field round-trips *(§15)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-15 — a cycle that crosses a keyed field boundary resolves through the ancestor chain *(§16.2)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-16 — an object shared between two sibling keyed fields is written twice and read as two instances *(§16.2)* — `Contracts/KeyedEvolutionTests`
- [x] KEY-17 — skipping an unknown field can never produce a dangling reference *(§16.2, C03)* — `Contracts/KeyedContractTests`
- [x] KEY-18 — the keyed encoding belongs to the payload, not to a wire format version: a contract encodes byte-identically under V0 and V1 *(§10.2, §14.2, §22.8)* — `Contracts/KeyedContractTests`

---

# 17. Polymorphism — `Contracts/`

- [x] PM-01 — a registered derived type round-trips with its runtime type intact *(§15)* — `Contracts/PolymorphismTests`
- [x] PM-02 — several derived types under one base are distinguished by tag *(§15)* — `Contracts/UnionDeclarationTests`
- [x] PM-03 — a union declared on an interface resolves the implementation *(§15)* — `Contracts/PolymorphismTests`
- [x] PM-04 — a union inside a collection element *(§15)* — `Contracts/PolymorphismTests`
- [x] PM-05 — a union inside a dictionary value *(§15)* — `Contracts/PolymorphismTests`
- [x] PM-06 — a union inside a keyed member *(§15)* — `Contracts/KeyedEvolutionTests`
- [x] PM-07 — an unknown discriminator on read → `BinaryTypeException` *(§15)* — `Contracts/UnionDeclarationTests`
- [x] PM-08 — a runtime type differing from the declared type with no union map → `BinaryTypeException` **on write** *(§15, C04)* — `Contracts/PolymorphismTests`
- [x] PM-09 — a value written through `object` without a map → `BinaryTypeException` *(§15, A03)* — `Contracts/PolymorphismTests`
- [x] PM-10 — a derived value inside a collection without a map → `BinaryTypeException` on write *(§15)* — `Contracts/PolymorphismTests`
- [x] PM-11 — duplicate union tags → `BinaryTypeException` *(§15)* — `Contracts/UnionDeclarationTests`
- [x] PM-12 — a tag outside the byte range → `BinaryTypeException` *(§15)* — `Contracts/UnionDeclarationTests`
- [x] PM-13 — a derived type not assignable to the declared base → `BinaryTypeException` *(§15)* — `Contracts/UnionDeclarationTests`
- [x] PM-14 — only tags travel; no type name appears anywhere in the payload *(§15)* — `Contracts/UnionDeclarationTests`
- [x] PM-15 — the tag precedes the member layout by exactly one byte *(§22.3)* — `Contracts/UnionDeclarationTests`
- [x] PM-16 — concurrent first-touch of a union map is safe and yields one consistent map *(L4, §28)* — `Contracts/UnionDeclarationTests`

---

# 18. References and cycles — `References/`

- [x] REF-01 — with `PreserveReferences`, a shared object is written once and restored as one instance *(§16)* — `References/ReferenceIdentityTests`
- [x] REF-02 — without it, a shared object is duplicated into distinct instances *(§16)* — `References/ReferenceIdentityTests`
- [x] REF-03 — identity covers member-encoded objects *(§16)* — `References/ReferenceIdentityTests`
- [x] REF-04 — identity covers arrays, collections and dictionaries *(§16, C02)* — `References/ReferenceIdentityTests`
- [x] REF-05 — value types are never framed, boxed or otherwise *(§16)* — `References/ReferenceFramingTests`
- [x] REF-06 — strings are never framed *(§16)* — `References/ReferenceFramingTests`
- [x] REF-07 — identity is reference identity; an overridden `Equals` does not merge two distinct objects *(§16)* — `References/ReferenceFramingTests`
- [x] REF-08 — the header's `PreserveReferences` flag, not local configuration, drives payload interpretation *(§16, §2.2)* — `References/ReferenceFramingTests`
- [x] REF-09 — a mutable container is registered **before** its children, so a cycle through it closes *(§16.1)* — `References/RegistrationOrderTests`
- [x] REF-10 — an array, immutable or frozen collection, or tuple is registered **after** completion *(§16.1)* — `References/RegistrationOrderTests`
- [x] REF-11 — a reference resolving to a still-building object → deterministic `BinaryFormatException`, never a half-built instance *(§16.1)* — `References/RegistrationOrderTests`
- [x] REF-12 — an unknown reference id → `BinaryFormatException` *(§16)* — `References/ReferenceFramingTests`
- [x] REF-13 — a negative reference id → `BinaryFormatException` *(§16)* — `References/ReferenceFramingTests`
- [x] REF-14 — an invalid marker byte → `BinaryFormatException` *(§16)* — `References/ReferenceFramingTests`
- [x] REF-15 — ids are visible only along the ancestor chain *(§16.2)* — `References/ReferenceFramingTests`
- [x] REF-16 — a back reference is never emitted between sibling keyed fields *(§16.2)* — `References/ReferenceFramingTests`
- [x] REF-17 — a repeated reference does not consume a second graph node *(§5.8)* — `Limits/DepthAndNodeTests`
- [x] CYC-01 — a direct self-reference round-trips under `PreserveReferences` *(§16)* — `References/ReferenceIdentityTests`
- [x] CYC-02 — a two-object cycle round-trips *(§16)* — `References/ReferenceIdentityTests`
- [x] CYC-03 — a cycle through a collection round-trips *(§16.1)* — `References/ReferenceIdentityTests`
- [x] CYC-04 — a cycle through a dictionary round-trips *(§16.1)* — `References/RegistrationOrderTests`
- [x] CYC-05 — a cycle through a polymorphic member round-trips *(§15, §16)* — `References/RegistrationOrderTests`
- [x] CYC-06 — a cycle through a struct wrapper behaves per §16 *(§16)* — `References/RegistrationOrderTests`
- [x] CYC-07 — a shared DAG without a cycle succeeds *(§16)* — `References/ReferenceIdentityTests`
- [x] CYC-08 — equal-but-distinct objects stay distinct *(§16)* — `References/ReferenceIdentityTests`
- [x] CYC-09 — a cycle without `PreserveReferences` → `BinaryTypeException` on write *(§16)* — `References/ReferenceIdentityTests`
- [x] CYC-10 — deep nesting fails as `BinaryLimitException`; no test may risk a stack overflow *(§5.1)* — `Limits/DepthAndNodeTests`

---

# 19. Round-trip corpus — `RoundTrip/`

Every family of §23. Each item means: value round-trips, runtime type is the contractual one, and boundary values are included.

The corpus is written once, in `RoundTrip/Corpus` and its partials, and run under three profiles —
`DefaultCorpusTests` (P0), `HeaderlessCorpusTests` (P7, RT-C08) and `ProtectedCorpusTests` (P6,
RT-C09). An item ticked against `RoundTrip/Corpus` is therefore proven under all three at once.

## 19.1 Primitives

- [x] RT-01 `bool` · [x] RT-02 `byte` · [x] RT-03 `sbyte` · [x] RT-04 `short` · [x] RT-05 `ushort`
- [x] RT-06 `int` · [x] RT-07 `uint` · [x] RT-08 `long` · [x] RT-09 `ulong` · [x] RT-10 `float`
- [x] RT-11 `double` · [x] RT-12 `decimal` · [x] RT-13 `char` · [x] RT-14 `string` · [x] RT-15 enum
- [x] RT-16 `Half` · [x] RT-17 `Int128` · [x] RT-18 `UInt128` · [x] RT-19 `IntPtr` · [x] RT-20 `UIntPtr`
- [x] RT-21 `Rune` · [x] RT-22 `BigInteger` — all of §19.1 in `RoundTrip/Corpus` (`CorpusPrimitives`)

Boundaries, applied across the above:

- [x] RT-B01 — zero, `MinValue`, `MaxValue`, and `-1` where signed *(§23)* — `RoundTrip/Corpus`
- [x] RT-B02 — `NaN`, `+∞`, `-∞`, negative zero for floating-point, asserted on the bits *(§22.4)* — `RoundTrip/Corpus`
- [x] RT-B03 — empty string, surrogate pairs, and a string at exactly `MaxStringBytes` *(§5.5)* — `RoundTrip/Corpus`
- [x] RT-B04 — `BigInteger` zero, negative, and multi-byte magnitudes *(§22.4)* — `RoundTrip/Corpus`
- [x] RT-B05 — enums with each underlying integral type, including undefined values *(§22.4)* — `RoundTrip/Corpus`
- [x] RT-B06 — `Nullable<T>` null and non-null for each value-type family *(§22.2)* — `RoundTrip/Corpus`

## 19.2 Time, numerics, system

- [x] RT-23 `DateTime` including each `DateTimeKind`, min and max *(§22.4)*
- [x] RT-24 `DateTimeOffset` including extreme offsets
- [x] RT-25 `TimeSpan` min, max, negative, fractional
- [x] RT-26 `DateOnly` · [x] RT-27 `TimeOnly` · [x] RT-28 `TimeZoneInfo`
- [x] RT-29 `Complex` · [x] RT-30 `Vector2` · [x] RT-31 `Vector3` · [x] RT-32 `Vector4`
- [x] RT-33 `Quaternion` · [x] RT-34 `Plane` · [x] RT-35 `Matrix3x2` · [x] RT-36 `Matrix4x4`
- [x] RT-37 `Guid` · [x] RT-38 `Uri` · [x] RT-39 `Version` · [x] RT-40 `StringBuilder`
- [x] RT-41 `CultureInfo` · [x] RT-42 `BitArray` including 0, 1, 7, 8, 9 bits
- all of §19.2 in `RoundTrip/Corpus` (`CorpusTimeAndSystem`)

Every numeric component is asserted individually. `TimeZoneInfo` uses a custom zone rather than a
machine zone, so the corpus does not depend on the host's time-zone data.

## 19.3 Arrays and memory

- [x] RT-43 one-dimensional primitive arrays, including empty
- [x] RT-44 one-dimensional reference arrays, including null elements
- [x] RT-45 `null` array
- [x] RT-46 multidimensional rank 2 and rank 3, row-major
- [x] RT-47 a multidimensional array with a zero dimension
- [x] RT-48 `Memory<T>` · [x] RT-49 `ReadOnlyMemory<T>`
- [x] RT-50 `ArraySegment<T>` with a non-zero offset and partial count
- [x] RT-51 single-segment `ReadOnlySequence<T>` · [x] RT-52 multi-segment `ReadOnlySequence<T>`
- all of §19.3 in `RoundTrip/Corpus` (`CorpusArrays`)

A memory-like value is the sequence of §22.3 — a count and its elements — so its backing storage is
not part of the value (§23, Q11). RT-50 and RT-52 therefore assert the offset and the segment count
as well as the elements: a segment comes back at offset zero, a multi-segment sequence as one
segment.

## 19.4 Composites

- [x] RT-53 `KeyValuePair<,>` · [x] RT-54 `Tuple<…>` · [x] RT-55 `ValueTuple<…>`
- [x] RT-56 nested and long tuples (`TRest`) · [x] RT-57 nullable tuple elements
- [x] RT-58 `Lazy<T>` materialized · [x] RT-59 `Lazy<T>` unmaterialized, faulted factory, and deferred restore *(§22.5, §23)* — `RoundTrip/LazyTests`
- [x] RT-60 `ImmutableArray<T>` populated, empty, and `default` *(§22.5)*
- RT-53…RT-57 and RT-60 in `RoundTrip/Corpus` (`CorpusComposites`)

## 19.5 Collections

- [x] RT-61 `List<>` · [x] RT-62 `HashSet<>` · [x] RT-63 `SortedSet<>` · [x] RT-64 `LinkedList<>`
- [x] RT-65 `ObservableCollection<>` · [x] RT-66 `Stack<>` · [x] RT-67 `Queue<>`
- [x] RT-68 `ReadOnlyCollection<>` · [x] RT-69 `ReadOnlyObservableCollection<>`
- [x] RT-70 a custom `ICollection<T>` with a parameterless constructor and `Add` — `Fixtures/Containers.Bag<T>`
- [x] RT-71 `ConcurrentBag<>` · [x] RT-72 `ConcurrentQueue<>` · [x] RT-73 `ConcurrentStack<>`
- [x] RT-74 `Dictionary<,>` · [x] RT-75 `SortedDictionary<,>` · [x] RT-76 `SortedList<,>`
- [x] RT-77 `ConcurrentDictionary<,>` · [x] RT-78 `ReadOnlyDictionary<,>`
- [x] RT-79 `PriorityQueue<,>`: entries round-trip and dequeue order is reconstructed from priorities *(§23)*
- [x] RT-80 `ImmutableList<>` · [x] RT-81 `ImmutableHashSet<>` · [x] RT-82 `ImmutableSortedSet<>`
- [x] RT-83 `ImmutableStack<>` · [x] RT-84 `ImmutableQueue<>`
- [x] RT-85 `ImmutableDictionary<,>` · [x] RT-86 `ImmutableSortedDictionary<,>`
- [x] RT-87 `FrozenSet<>` · [x] RT-88 `FrozenDictionary<,>`
- all of §19.5 in `RoundTrip/Corpus` (`CorpusCollections`)

Cross-cutting:

- [x] RT-C01 — every container round-trips empty *(§23)* — `RoundTrip/Corpus`
- [x] RT-C02 — stack ordering is preserved; elements are written bottom-up for `Stack`, `ConcurrentStack`, `ImmutableStack` *(§23)* — `RoundTrip/Corpus`
- [x] RT-C03 — unordered containers assert contents, never iteration order *(§23)* — `RoundTrip/Corpus`
- [x] RT-C04 — each interface resolves to its documented concrete type: `IList<T>`/`ICollection<T>`/`IEnumerable<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>` → `List<T>`; `ISet<T>` → `HashSet<T>`; `IDictionary<K,V>` → `Dictionary<K,V>`; `IReadOnlyDictionary<K,V>` → `ReadOnlyDictionary<K,V>`; immutable interfaces → their immutable types *(§23)* — `RoundTrip/Corpus`
- [x] RT-C05 — reference identity across an interface-typed member is preserved; the concrete type is not *(§23)* — `RoundTrip/InterfaceMemberTests`
- [x] RT-C06 — `ImmutableArray<T>` is the only container distinguishing default from empty *(§23)* — `RoundTrip/Corpus`
- [x] RT-C07 — nested containers: collection of dictionaries, dictionary of collections, array of objects *(§23)* — `RoundTrip/Corpus`
- [x] RT-C08 — the whole corpus also round-trips under V0 for every shape V0 supports *(§10.2)* — `RoundTrip/HeaderlessCorpusTests`
- [x] RT-C09 — the whole corpus round-trips under P6 (compression + checksum + encryption) *(§22.6)* — `RoundTrip/ProtectedCorpusTests`
- [x] RT-C10 — the object shape itself: a member-encoded type, nested and with a null member, under every profile *(§23)* — `RoundTrip/Corpus`

---

# 20. Limits and budgets — `Limits/`

Every limit gets **below · exact · one above · structurally invalid** where the wire format admits the value.

## 20.1 Per-value limits

- [x] LIM-01 `MaxArrayLength` — the four cases *(§5.2)* — `Limits/ValueLimitTests`
- [x] LIM-02 `MaxCollectionLength` — the four cases *(§5.3)* — `Limits/ValueLimitTests`
- [x] LIM-03 `MaxDictionaryEntries` — the four cases *(§5.4)* — `Limits/ValueLimitTests`
- [x] LIM-04 `MaxStringBytes` — measured in UTF-8 bytes, not characters *(§5.5)* — `Limits/ValueLimitTests`
- [x] LIM-05 `MaxByteBlobBytes` — the four cases *(§5.6)* — `Limits/ValueLimitTests`
- [x] LIM-06 bit counts: 0, 1, 7, 8, 9, exact byte boundary, one above *(§22.4)* — `Limits/ValueLimitTests`
- [x] LIM-07 — a negative wire count → `BinaryFormatException`, never `BinaryLimitException` *(§5)* — `Limits/ValueLimitTests`
- [x] LIM-08 — a negative wire length → `BinaryFormatException` *(§5)* — `Limits/ValueLimitTests`
- [x] LIM-09 — zero succeeds wherever the shape admits an empty value *(§5)* — `Limits/ValueLimitTests`

## 20.2 Multidimensional arrays

- [x] LIM-10 — a negative dimension → `BinaryFormatException` *(§17)* — `Limits/ArrayShapeTests`
- [x] LIM-11 — a zero dimension yields the documented empty array without overflowing the product *(§17)* — `Limits/ArrayShapeTests`
- [x] LIM-12 — a product exactly at `MaxArrayLength` succeeds *(§17)* — `Limits/ArrayShapeTests`
- [x] LIM-13 — a product one above → `BinaryLimitException` *(§17)* — `Limits/ArrayShapeTests`
- [x] LIM-14 — a product that would overflow `long` → `BinaryLimitException`, computed overflow-safe *(§17)* — `Limits/ArrayShapeTests`

## 20.3 Cumulative budgets

- [x] LIM-15 — two individually legal collections exceeding `MaxTotalElements` → `BinaryLimitException` *(§5.7)* — `Limits/BudgetTests`
- [x] LIM-16 — the element count is charged exactly once per validated count *(§6)* — `Limits/BudgetAccountingTests`
- [x] LIM-17 — the element budget never decreases within an operation *(§6)* — `Limits/BudgetAccountingTests`
- [x] LIM-18 — `MaxObjectGraphNodes` counts member-encoded objects **and** container instances *(§5.8, S04)* — `Limits/DepthAndNodeTests`
- [x] LIM-19 — a back reference does not create another node *(§5.8)* — `Limits/DepthAndNodeTests`
- [x] LIM-20 — `MaxKeyedFields` bounds one keyed object, on read and on write *(§5.9)* — `Limits/KeyedFieldLimitTests`
- [x] LIM-21 — `MaxTotalKeyedFields` bounds the operation, including skipped unknown fields *(§5.9a, S12)* — known fields by `Limits/KeyedFieldLimitTests`, skipped ones by `Limits/BudgetTests` and `Hostile/AmplificationTests`
- [x] LIM-22 — unknown keyed fields do **not** consume `MaxTotalElements` *(§21.2)* — `Limits/BudgetTests`
- [x] LIM-23 — the per-object ceiling is evaluated before the cumulative one, on both directions *(§5.9)* — `Limits/KeyedFieldLimitTests`
- [x] LIM-24 — a fresh budget per public call; a prior failure cannot poison a later one *(§2.2)* — `Limits/BudgetTests`
- [x] LIM-25 — a header declaring a different reference mode keeps the same budget object *(§2.2)* — `Limits/BudgetAccountingTests`

## 20.4 Depth

- [x] LIM-26 — every structural shape enters a depth scope; scalars do not *(§5.1)* — `Limits/DepthTests`
- [x] LIM-27 — the exact configured depth succeeds; one deeper → `BinaryLimitException` *(§5.1)* — `Limits/DepthTests`
- [x] LIM-28 — depth unwinds to the previous value on success *(L2, §6)* — `Limits/DepthTests`
- [x] LIM-29 — depth unwinds to the previous value on exception *(L2, §6)* — `Limits/DepthTests`
- [x] LIM-30 — a failed `EnterDepth` leaves depth unchanged *(L2, §6)* — `Limits/DepthTests`
- [x] LIM-31 — sibling nesting unwinds independently *(§5.1)* — `Limits/DepthTests`
- [x] LIM-32 — a recursive container type (`class Tree : List<Tree>`) is bounded exactly like a recursive object *(§5.1, S03)* — `Limits/DepthAndNodeTests`
- [x] LIM-33 — depth applies on write as well as read *(§5.1)* — `Limits/DepthAndNodeTests`

## 20.5 Phases

- [x] LIM-34 `MaxPayloadBytes` — read and write *(§5.10)* — `Limits/PhaseLimitTests`
- [x] LIM-35 `MaxCompressedBytes` — read and write *(§5.10)* — `Limits/PhaseLimitTests`
- [x] LIM-36 `MaxEncryptedBytes` — read and write *(§5.10)* — `Limits/PhaseLimitTests`
- [x] LIM-37 `MaxWireBytes` — read and write *(§5.10)* — `Limits/PhaseLimitTests`
- [x] LIM-38 — a header-declared phase length above its limit is rejected **before** the buffer is allocated *(§22.6, S06)* — `Limits/PhaseLimitTests`
- [x] LIM-39 — the phase check runs in the pipeline, not in an algorithm *(§2.5, §12)* — `Limits/PhaseLimitTests`, `Limits/StructuralBarrierTests`

## 20.6 Internal invariants (L2)

- [x] LIM-40 — an `ElementCount` can only be obtained through `Validate`, which checks and charges together *(§6, §17)* — `Limits/StructuralBarrierTests`
- [x] LIM-41 — `CountKind` selects the correct limit for array, collection and dictionary counts *(L2, §6)* — `Limits/BudgetAccountingTests`
- [x] LIM-42 — `ElementCount.CapacityHint` bounds initial capacity; a declared count never allocates its full size up front *(§17)* — `Limits/BudgetAccountingTests`
- [x] LIM-43 — no type below `Pipeline/` references `SerializationLimits` *(§2, architecture invariant)* — `Exceptions/SourceInvariantTests`
- [x] LIM-44 — payload bytes are reachable only through `ValueReader` / `ValueWriter` *(§2.3)* — `Limits/StructuralBarrierTests`

---

# 21. Security streams — `Streams/`

## 21.1 `MeteredReadStream`

- [x] STR-01 — counts from zero regardless of the caller stream's absolute position *(§7.1)* — `Streams/MeteredReadStreamTests`
- [x] STR-02 — reads under budget succeed; the exact budget succeeds *(§7.1)* — `Streams/MeteredReadStreamTests`
- [x] STR-03 — an over-read against the budget → `BinaryLimitException`, not a format error *(§7.1)* — D1-05
- [x] STR-04 — `RemainingBytes` reflects what may still be read: the lesser of the remaining budget and the physical remainder *(§7.1, §17)* — D1-04
- [x] STR-05 — an underlying `IOException` → `BinaryStreamException` with the original preserved *(§7.1, §9)* — `Streams/MeteredReadStreamTests`
- [x] STR-06 — the caller's stream is never disposed *(§7.1)* — `Streams/MeteredReadStreamTests`
- [x] STR-07 — nesting (V0 wire over payload) applies both ceilings independently *(§7.1)* — `Streams/MeteredReadStreamTests`
- [x] STR-08 — a partial-read source is handled without data loss *(§7.1)* — `Streams/MeteredReadStreamTests`

## 21.2 `MeteredWriteStream`

- [x] STR-09 — the budget is relative to the destination's starting position *(§7.2, C06)* — `Streams/MeteredWriteStreamTests`
- [x] STR-10 — appending to a non-empty stream costs the operation nothing for pre-existing bytes *(§7.2)* — `Streams/MeteredWriteStreamTests`
- [x] STR-11 — writes under budget succeed; the exact budget succeeds *(§7.2)* — `Streams/MeteredWriteStreamTests`
- [x] STR-12 — exceeding the budget → `BinaryLimitException` *(§7.2)* — `Streams/MeteredWriteStreamTests`
- [x] STR-13 — a rewind for keyed-length patching does not double-charge; the budget follows the high-water mark *(§7.2)* — `Streams/MeteredWriteStreamTests`
- [x] STR-14 — an underlying `IOException` → `BinaryStreamException` *(§7.2, §9)* — `Streams/MeteredWriteStreamTests`
- [x] STR-15 — the caller's stream is never disposed *(§7.2)* — `Streams/MeteredWriteStreamTests`
- [x] STR-16 — `CanSeek` follows the inner stream *(§7.2)* — `Streams/MeteredWriteStreamTests`

## 21.3 `WindowReadStream`

- [x] STR-17 — a field decoder may read exactly the declared length *(§7.3)* — `Streams/WindowReadStreamTests`
- [x] STR-18 — reading past the window → `BinaryFormatException`, not a limit error *(§7.3)* — `Streams/WindowReadStreamTests`
- [x] STR-19 — a decoder cannot reach into the next field *(§7.3)* — `Streams/WindowReadStreamTests`
- [x] STR-20 — `SkipRemaining` consumes the rest in bounded chunks *(§7.3)* — `Streams/WindowReadStreamTests`
- [x] STR-21 — a window never materializes the field payload merely to enforce the boundary *(§7.3)* — `Streams/WindowReadStreamTests`
- [x] STR-22 — a window shares the parent operation's budget and reference state *(§7.3)* — `Streams/WindowReadStreamTests`

## 21.4 Public stream behavior

- [x] STR-23 — a seekable `MemoryStream` round-trips *(§20)* — `Streams/PublicStreamTests`
- [x] STR-24 — a non-seekable source is rejected only by APIs that require seekability *(§10.3)* — `Streams/PublicStreamTests`
- [x] STR-25 — a stream returning short reads round-trips correctly *(§7.1)* — `Streams/PublicStreamTests`
- [x] STR-26 — premature EOF → `BinaryFormatException` *(§8.2)* — `Streams/PublicStreamTests`
- [x] STR-27 — a non-readable source and a non-writable destination fail with normal BCL semantics *(§20)* — `Streams/PublicStreamTests`
- [x] STR-28 — an inspection API restores position even on failure *(§20)* — `Streams/PublicStreamTests`

---

# 22. Hostile input — `Hostile/`

## 22.1 Mutation

- [x] HST-01 — a mutated magic → `BinaryFormatException` *(§22.6)* — `Hostile/MutationTests`
- [x] HST-02 — a mutated version → `BinaryFormatNotSupportedException` *(§22.6)* — `Hostile/MutationTests`
- [x] HST-03 — a mutated algorithm identifier → `BinaryFormatNotSupportedException` *(§22.6)* — `Hostile/MutationTests`
- [x] HST-04 — a mutated optional-string presence flag, length or content → deterministic documented failure *(§22.1)* — `Hostile/MutationTests`
- [x] HST-05 — a mutated `PreserveReferences` flag → deterministic failure or correct alternate interpretation *(§16)* — `Hostile/MutationTests`
- [x] HST-06 — each mutated length field → the documented exception *(§22.6)* — `Hostile/MutationTests`
- [x] HST-07 — a mutated checksum → `BinaryIntegrityException` *(§8.5)* — `Hostile/MutationTests`
- [x] HST-08 — mutated ciphertext → `BinaryIntegrityException` *(§13.1)* — `Hostile/MutationTests`
- [x] HST-09 — **every byte** of an encrypted frame's header flipped in turn always fails *(§13.1)* — `Hostile/MutationTests`

## 22.2 Truncation

- [x] HST-10 — every prefix of a valid V1 frame has a deterministic, documented outcome *(§8.2)* — `Hostile/TruncationTests`
- [x] HST-11 — a truncated fixed-size primitive → `BinaryFormatException`, one case per primitive width *(§2.3, C07)* — `Hostile/TruncationTests` covers 1, 2, 4, 8 and 16 bytes; `Hostile/MalformedPayloadTests` keeps the original Guid, Int128, UInt128 and int64 cases
- [x] HST-12 — a truncated 7-bit integer → `BinaryFormatException` *(§22.1)* — `Hostile/TruncationTests`
- [x] HST-13 — excessive 7-bit continuation bytes → `BinaryFormatException` *(§22.1)* — `Hostile/TruncationTests`
- [x] HST-14 — a 7-bit integer overflowing `Int32` → `BinaryFormatException` *(§22.1)* — `Hostile/TruncationTests`
- [x] HST-15 — V0 truncation → `BinaryFormatException` *(§8.2)* — `Hostile/TruncationTests`
- [x] HST-16 — a failed `ReadExact` retains no partial output *(§2.3)* — `Hostile/TruncationTests`

## 22.3 Amplification

- [x] HST-17 — a declared count at the limit with a truncated element stream allocates nothing proportional to the count *(§17)* — `Hostile/AmplificationTests`
- [x] HST-18 — a declared string or blob length beyond the bytes physically present → `BinaryFormatException` before allocation, on the wire as well as inside the payload *(§17)* — wire half by D1-02, payload and keyed-window halves by `Hostile/AmplificationTests`
- [x] HST-19 — a declared phase length above its limit → `BinaryLimitException` before allocation *(§22.6)* — `Hostile/MalformedPayloadTests`
- [x] HST-20 — a declared plaintext length exceeding the ciphertext delivered → rejected before allocation *(§13)* — `Hostile/AmplificationTests`
- [x] HST-21 — a decompression bomb is bounded by `MaxPayloadBytes`; the attacker must deliver `CompressedLength` real bytes *(§12)* — `Hostile/AmplificationTests`
- [x] HST-22 — nested individually-valid containers cannot bypass the cumulative element budget *(§5.7)* — `Hostile/AmplificationTests`
- [x] HST-23 — many small keyed objects cannot bypass `MaxTotalKeyedFields` *(§5.9a)* — `Hostile/AmplificationTests`
- [x] HST-24 — an unknown keyed field is skipped incrementally *(§7.3)* — `Streams/WindowReadStreamTests`
- [x] HST-25 — a hostile deeply nested payload fails as a limit violation, never a stack overflow *(§5.1)* — `Limits/DepthAndNodeTests`
- [x] HST-26 — an oversized or infinite `IEnumerable<T>` on write is abandoned at the limit, not enumerated *(§17, S11)* — `Limits/BudgetTests`

## 22.4 Property and metamorphic (L3)

- [x] HST-27 — primitive round-trip closure over generated values *(§23)* — `Hostile/PropertyTests`
- [x] HST-28 — collection round-trip closure within limits *(§23)* — `Hostile/PropertyTests`
- [x] HST-29 — acyclic nested graph closure *(§23)* — `Hostile/PropertyTests`
- [x] HST-30 — hash-container round trip is order-independent *(§23)* — `Hostile/PropertyTests`
- [x] HST-31 — sorted-container ordering is deterministic *(§23)* — `Hostile/PropertyTests`
- [x] HST-32 — tightening a limit never turns a failure into a success *(§5)* — `Hostile/PropertyTests`
- [x] HST-33 — entry points agree for the same value and configuration *(§3)* — `Hostile/PropertyTests`
- [x] HST-34 — a malformed-byte corpus never causes an uncontrolled process failure, and every outcome is a Viper exception *(§8)* — `Hostile/PropertyTests`

---

# 23. Compression — `Algorithms/`

- [ ] CMP-01 — `NoCompression` round-trips and still honors phase limits *(§12)* — round trip proven by `Algorithms/CompressionTests`; the phase-limit half lands in M7
- [x] CMP-02 — `Deflate` round-trips *(§12)* — `Algorithms/CompressionTests`
- [x] CMP-03 — `Brotli` round-trips *(§12)* — `Algorithms/CompressionTests`
- [x] CMP-04 — malformed Deflate input → `BinaryFormatException` *(§12)* — `Algorithms/CompressionTests`
- [x] CMP-05 — malformed Brotli input → `BinaryFormatException` *(§12)* — `Algorithms/CompressionTests`
- [ ] CMP-06 — raw input above `MaxPayloadBytes` → `BinaryLimitException` *(§12)*
- [ ] CMP-07 — compressed output above `MaxCompressedBytes` → `BinaryLimitException` *(§12)*
- [ ] CMP-08 — compressed input above `MaxCompressedBytes` → `BinaryLimitException` *(§12)*
- [ ] CMP-09 — an expected decompressed length above `MaxPayloadBytes` → `BinaryLimitException` before allocation *(§12)*
- [x] CMP-10 — decompression producing **fewer** bytes than declared → rejected *(§12, S09)* — `Algorithms/CompressionTests`
- [x] CMP-11 — decompression producing **more** bytes than declared → rejected *(§12, S09)* — `Algorithms/CompressionTests`
- [ ] CMP-12 — `ICompressionAlgorithm` receives no limits and is invoked inside the phase barrier *(§2.5, §12)*
- [ ] CMP-13 — a custom compression algorithm round-trips and its name is recorded in the header *(§4.1, §11)*

---

# 24. Checksum — `Algorithms/`

- [ ] CHK-01 — `NoChecksum` writes a zero-length checksum *(§22.6)*
- [ ] CHK-02 — `Crc32` round-trips *(§22.6)*
- [ ] CHK-03 — a checksum mismatch → `BinaryIntegrityException` *(§8.5)*
- [ ] CHK-04 — a checksum of unexpected length → the documented failure *(§11)*
- [ ] CHK-05 — the checksum is computed over the **raw** payload, before compression *(§22.6)*
- [ ] CHK-06 — a custom checksum round-trips under its registered name *(§4.1)*
- [ ] CHK-07 — a payload naming an unregistered custom checksum → `BinaryFormatNotSupportedException` *(§8.4)*
- [ ] CHK-08 — `RequireChecksum` rejects a payload with `ChecksumAlgorithm.None` → `BinaryIntegrityException` *(§21.1)*

---

# 25. Encryption — `Algorithms/`

- [x] ENC-01 — `Aes256Gcm` with a 32-byte key round-trips *(§13)* — `Algorithms/EncryptionTests`
- [ ] ENC-02 — an invalid direct key size → `ArgumentException` *(§8.10)*
- [x] ENC-03 — a wrong key → `BinaryIntegrityException` at the authentication boundary *(§8.5)* — `Algorithms/EncryptionTests`
- [x] ENC-04 — tampered ciphertext → `BinaryIntegrityException` *(§13.1)* — `Algorithms/EncryptionTests`
- [x] ENC-05 — any altered authenticated header byte → `BinaryIntegrityException` *(§13.1)* — `Algorithms/EncryptionTests`
- [ ] ENC-06 — `OnDiskLength` is not authenticated, and a wrong value still fails by truncation or tag *(§22.7)*
- [x] ENC-07 — no key configured for an encrypted payload → `BinaryEncryptionKeyException` *(§8.7)* — `Algorithms/EncryptionTests`
- [ ] ENC-08 — a resolver returning null → `BinaryEncryptionKeyException` *(§8.7)*
- [x] ENC-09 — a `KeyId` mismatch against the provider's configured id → `BinaryEncryptionKeyException` *(§13.2)* — `Algorithms/EncryptionTests`
- [x] ENC-10 — the resolver receives the header's `KeyId` *(§13.2)* — `Algorithms/EncryptionTests`
- [ ] ENC-11 — `SecretKey` always holds its own copy *(§13.2)*
- [x] ENC-12 — a resolver's returned buffer is not mutated or zeroed by the serializer *(§13.2, S07)* — `Algorithms/EncryptionTests`
- [x] ENC-13 — disposing a provider clears only its own copy; the caller's array is untouched *(§13.2)* — `Algorithms/EncryptionTests`
- [x] ENC-14 — using a disposed provider → `ObjectDisposedException` *(§13.2, S08)* — `Algorithms/EncryptionTests`
- [ ] ENC-15 — each resolved key is disposed at the end of the phase that requested it *(§13.2)*
- [ ] ENC-16 — serializer-owned temporary crypto buffers are cleared when their lifetime ends *(§13.2)*
- [ ] ENC-17 — a second payload with a different key id does not fall back to a previously resolved key *(§13.2)* — M7
- [ ] ENC-18 — `AuthenticatesAssociatedData == false` is rejected under `RequireEncryption` *(§13.1)*
- [x] ENC-19 — `NoEncryption` with `RequireEncryption` on the read path → `BinaryIntegrityException` *(§21.1)* — `Algorithms/EncryptionTests`
- [x] ENC-20 — an encryption **capability** does not force encryption: a plaintext payload is read normally without the policy *(§21.1, S01)* — `Algorithms/EncryptionTests`
- [ ] ENC-21 — plaintext input ≤ `MaxCompressedBytes`, ciphertext ≤ `MaxEncryptedBytes`, both directions *(§13)*
- [ ] ENC-22 — a custom encryption algorithm round-trips under its registered name *(§4.1)*

---

# 26. Algorithm catalog — `Algorithms/`

- [ ] CAT-01 — built-in algorithms resolve from the enum identifier *(§4.1)*
- [ ] CAT-02 — a custom name resolves through the options' catalog *(§4.1)*
- [ ] CAT-03 — a payload naming an unregistered custom algorithm → `BinaryFormatNotSupportedException` *(§8.4)*
- [ ] CAT-04 — the catalog is a snapshot: registering after `Build()` changes nothing *(§4.1)*
- [ ] CAT-05 — two options instances have independent catalogs *(§4.1)*
- [ ] CAT-06 — no process-wide mutable state can substitute a built-in algorithm *(§4.1)*
- [ ] CAT-07 — a factory is invoked per resolution as documented, and a throwing factory surfaces a defined exception *(§4.1)*
- [ ] CAT-08 — concurrent resolution from one catalog is safe *(L4)*

---

# 27. Inspection and diagnostics — `Metadata/`, `Diagnostics/`

- [x] INS-01 — `Peek` returns the header metadata of a valid V1 payload *(§19)* — `Metadata/InspectorTests`
- [x] INS-02 — `Peek` restores the source position, on success and on failure *(§19, §20)* — `Metadata/InspectorTests`
- [x] INS-03 — `Peek` on a non-seekable stream → `NotSupportedException` *(§19)* — `Metadata/InspectorTests`
- [x] INS-04 — `Peek` returns `null` for bytes not recognized as a supported format *(§19)* — `Metadata/InspectorTests`
- [ ] INS-05 — `Peek` on a recognized magic with an unsupported version → `BinaryFormatNotSupportedException` *(§19)*
- [x] INS-06 — `Peek` on a recognized but malformed header → `BinaryFormatException`, not `null` *(§19)* — `Metadata/InspectorTests`
- [ ] INS-07 — limits passed to `Peek` are honored *(§19)* — `Metadata/InspectorTests` proves the limits are validated; enforcement during the read lands in M8
- [x] INS-08 — `Peek(stream, null)` → `ArgumentNullException` *(§8.10)* — `Metadata/InspectorTests`
- [ ] INS-09 — an underlying `IOException` during inspection → `BinaryStreamException` *(§19)*
- [ ] INS-10 — `BinaryHeaderInfo` reports version, algorithms, custom names and `KeyId` and nothing secret *(§11)* — `Metadata/InspectorTests` covers version, algorithms and KeyId; custom names land in M8
- [ ] DMP-01 — `DumpHeader(byte[])` renders a valid envelope *(§19)*
- [ ] DMP-02 — `DumpHeader(Stream)` renders a valid envelope and does not consume the stream *(§19)*
- [ ] DMP-03 — unrecognized input produces diagnostic text rather than a thrown exception *(§19)*
- [ ] DMP-04 — the dumper catches only `BinarySerializerException`; it does not normalize arbitrary exceptions *(§19)*
- [ ] DMP-05 — no production type outside `Diagnostics/` reports a failure as output *(§19)*

---

# 28. Concurrency and caches — `Concurrency/`

- [ ] CN-01 — one shared `BinarySerializer` used from many threads produces correct results *(§2.2)*
- [ ] CN-02 — per-operation state is isolated; no budget is shared between calls *(§2.2)*
- [ ] CN-03 — `TypeContractCache` first touch under contention yields one consistent contract *(L4)*
- [ ] CN-04 — the union-map cache first touch is safe *(L4, §15)*
- [ ] CN-05 — `FormatterRegistry` resolution under contention is safe and stable *(L4)*
- [ ] CN-06 — `ActivatorCache` *(L4)*
- [ ] CN-07 — `DictionaryAccessorCache` *(L4)*
- [ ] CN-08 — `FrozenFactoryCache` *(L4)*
- [ ] CN-09 — `ImmutableFactoryCache`, including `ImmutableCollectionsMarshal.AsArray` resolution *(§17)*
- [ ] CN-10 — `LazyAccessorCache` *(L4)*
- [ ] CN-11 — `MethodInvokerCache` *(L4)*
- [ ] CN-12 — `ReadOnlySequenceAccessorCache` *(L4)*
- [ ] CN-13 — `TupleAccessorCache` *(L4)*
- [ ] CN-14 — a cached accessor is functionally identical on first and subsequent use *(L2)*
- [ ] CN-15 — cache construction never depends on request-local budget state *(§2.2)*
- [ ] CN-16 — an invalid type or member fails deterministically on every attempt, not only the first *(L2)*
- [ ] CN-17 — concurrent encryption and decryption with distinct key ids stay correct *(§13.2)*
- [ ] CN-18 — concurrent inspection of separate streams stays correct *(§19)*
- [ ] CN-19 — `ImmutableArray<T>` obtains its backing array through `ImmutableCollectionsMarshal.AsArray<T>`, never a reflective instance `ToArray` *(§17)*

---

# 29. Test utilities — `Fixtures/`

Implemented centrally in M0; every helper with logic of its own is itself tested.

```text
AssertEx.Throws<TException>(messageSubstring, act)
AssertEx.AllocatesLessThan(ceiling, act)
AssertEx.DoesNotContainBytes(haystack, needle)
AssertEx.SameContents<T>()
AssertEx.PopsInOrder<T>() · DequeuesInOrder<T>() · DequeuesInPriorityOrder<TElement,TPriority>()

Wire.Payload · Wire.Header · Wire.Frame · Wire.FrameWith · Wire.NestedCollections · Wire.KeyedFields
Wire.FrameWithOversizedCustomName · Wire.FrameWithOversizedChecksum
Wire.NotNull · Wire.ReferenceFrame · Wire.KeyedField · Wire.KeyedBody              (added by M4)
Wire.ReadHeader (an independent header decoder) · Wire.Fixture (committed *.bin)
Wire.PlainHeaderLength · PreserveReferencesOffset · UncompressedLengthOffset
CompressedLengthOffset · OnDiskLengthOffset · ChecksumLengthOffset

Mutate.FlipByte · SetByte · SetInt32 · Truncate · Prefixes · SevenBitEncoded

NonSeekableStream · PartialReadStream · FailingStream · NonSeekableWriteStream · TrackingStream

Sequences.Of (a multi-segment ReadOnlySequence<T>) · Bag<T> (a custom ICollection<T>)   (added by M5)

Wire.Container · Wire.StringValue · Wire.BitArrayValue · Wire.MultiDimensionalArray   (added by M6)
WriteOnlyStream (a seekable destination that cannot be read)                          (added by M6)

Sum8 · WideChecksum · IdentityCompression · UnauthenticatedCipher   (custom algorithm doubles)
```

There is deliberately no `ThrowsExact`: xUnit's `Assert.Throws<T>` already matches the exact type, and
a wrapper that restates it would only invite the assumption that the plain form is loose.

Helpers a later stage needs — byte-observing stream wrappers, if a suite turns out to need them — are
added by that stage rather than built ahead of use. The committed `*.bin` fixtures arrived with M3.

- [x] UTIL-01 — assertion helpers behave correctly, including their negative cases — `Fixtures/UtilityTests`
- [x] UTIL-02 — frame builders produce bytes a real reader accepts — `Fixtures/UtilityTests`
- [x] UTIL-03 — mutation helpers change exactly the targeted bytes — `Fixtures/UtilityTests`
- [x] UTIL-04 — truncation helpers enumerate every prefix — `Fixtures/UtilityTests`
- [x] UTIL-05 — `NonSeekableStream` reports `CanSeek == false` and throws on `Position` — `Fixtures/UtilityTests`
- [x] UTIL-06 — `PartialReadStream` returns short reads without losing data — `Fixtures/UtilityTests`
- [x] UTIL-07 — `FailingStream` raises `IOException` at the configured offset, on read and on write — `Fixtures/UtilityTests`
- [ ] UTIL-08 — byte-observing stream wrappers, only if a suite needs them *(deferred; nothing so far does)*
- [x] UTIL-09 — committed `Fixtures/Wire/*.bin` compatibility fixtures load and are never regenerated by the code under test — `Fixtures/UtilityTests`
- [x] UTIL-10 — the keyed and reference frame builders declare the counts and lengths they were given, not the real ones — `Fixtures/UtilityTests`
- [x] UTIL-11 — `Sequences.Of` chains its segments in order and reports more than one — `Fixtures/UtilityTests`
- [x] UTIL-12 — the value frame builders declare the counts, lengths and ranks they were given, not the real ones — `Fixtures/UtilityTests`
- [x] UTIL-13 — `WriteOnlyStream` accepts writes, seeks, and refuses reads — `Fixtures/UtilityTests`

---

# 30. Findings

## 30.1 Confirmed defects

Found while aligning this plan with the contract, reproduced against `src/` at `082bf32`. These are **not** open questions: the contract is explicit and the code disagrees with it. Each must be fixed in `src/` before the checkpoint that covers it can be ticked.

### D1 — declared wire lengths are not checked against physically available bytes — **FIXED**

**Contract:** §17 — "Every declared byte length is additionally compared with the bytes that can still arrive before it is used to allocate — the read source reports its remaining bytes, and a source that cannot satisfy a declaration is rejected first." §24 ticks "Declared lengths are compared with physically available bytes before allocation."

**Actual:** the check is lost for everything read directly off the wire, which is the entire V1 header and the on-disk payload.

`ValueReader.RequireAvailable` matches `IRemainingBytes` first. `MeteredReadStream.RemainingBytes` is `MaxWireBytes - bytesRead` — a **budget**, not a physical measurement — and `MeteredReadStream.CanSeek` is `false`, so the physical `_source.Length - _source.Position` branch is never reached. The declaration is therefore compared with ~80 MB of unspent budget and accepted, and the buffer is allocated before the truncation is discovered.

Measured with default limits:

```text
30-byte frame declaring OnDiskLength = 60 MiB   →  62,917,968 bytes allocated
                                                    then BinaryFormatException
15-byte frame declaring a 3 MB custom-algorithm name →  3,003,480 bytes allocated
                                                    then BinaryFormatException
```

That is a ~2,000,000× amplification from a single malformed frame, repeatable per call. The ceiling is `MaxEncryptedBytes` (64 MiB + 64 KiB by default) for the payload and `MaxStringBytes` (4 MB) for each of the four header strings.

The payload interior is unaffected: the V1 payload is re-read from a seekable `MemoryStream`, so the physical branch applies there. The hole is exactly at the wire/header layer.

**Cause:** the metered stream reported budget where the contract requires availability.

**Fix applied** in `Security/MeteredReadStream.cs`, no other file touched and nothing on the wire changed:

- `RemainingBytes` is now the lesser of the remaining budget and the inner stream's physical remainder; a nested meter answers through the inner meter, so a chain propagates the physical truth.
- `Exceeded` classifies by the bound that broke: past the budget is `BinaryLimitException`, within the budget but past the remaining bytes is `BinaryFormatException`.
- `Read` is bounded by the budget alone, so an ordinary short read stays a truncation for the caller to report rather than becoming a limit violation.

`System-Contract.md` §7.1 now states all three; the §24 box is restored. Verified by removing the fix and observing 6 of the 13 pinning tests fail.

- [x] D1-01 — a tiny frame declaring `OnDiskLength` near `MaxEncryptedBytes` is rejected with no allocation proportional to the declaration *(§17)* — `Hostile/AllocationAmplificationTests`
- [x] D1-02 — a tiny frame declaring a multi-megabyte custom algorithm name is rejected before allocation *(§17)* — `Hostile/AllocationAmplificationTests`
- [x] D1-03 — a declared checksum length beyond the bytes present is rejected before allocation *(§17)* — `Hostile/AllocationAmplificationTests`
- [x] D1-04 — `MeteredReadStream.RemainingBytes` never exceeds the inner stream's physical remainder, through a chain of meters *(L2, §7.1)* — `Streams/MeteredReadStreamTests`
- [x] D1-05 — the wire-budget semantics of §7.1 still hold: an over-read against the budget remains `BinaryLimitException`, while a declaration beyond the physical bytes is `BinaryFormatException` *(§7.1, §17)* — `Streams/MeteredReadStreamTests`

### D2 — a protection policy combined with V0 is silently ineffective — **FIXED**

Found while repositioning V0 as a first-class compact codec (§10.2), reproduced against `src/` at
`04f396b`.

**Contract:** §21.1 — "With the policy set, an unencrypted payload is `BinaryIntegrityException`."
§10.2 — V0 has no compression, checksum or encryption phase, because a headerless payload has nowhere
to record one.

**Actual:** the two rules meet with no result. `Build()` accepts

```csharp
BinarySerializerOptions.Configure()
    .WithVersion(0)
    .AllowV0Fallback()
    .WithEncryption(new Aes256Gcm(), key)
    .RequireEncryption()
    .Build();
```

and the serializer it produces writes plaintext — `V0FormatPipeline.Write` never reaches
`EncryptionService` — and reads that plaintext back without raising `BinaryIntegrityException`. The
caller asked for mandatory encryption and got none, with no diagnostic on either direction. The same
holds for `RequireChecksum`, and a configured compression or checksum algorithm is likewise dropped
from a V0 write.

**Cause:** the write version and the protection policies are validated independently. V0's absence of
algorithm phases is a property of the format; the policies are a property of the configuration; no
rule connects them.

**Fix applied** in `Configuration/BinarySerializerOptionsBuilder.cs`, at configuration time rather
than at the operation. Nothing on the wire changed and no pipeline was touched.

`Build()` now rejects a protection policy combined with the headerless format on *either* side, as a
`BinaryConfigurationException` alongside the other contradictions of §4.1:

- `RequireEncryption` or `RequireChecksum` with `WriteVersion == 0` — the write side, which would
  otherwise emit unprotected bytes;
- `RequireEncryption` or `RequireChecksum` with `AllowV0Fallback` — the read side, which would
  otherwise accept them.

Closing both directions is what makes an operation-time check unnecessary: a serializer carrying a
policy can neither produce nor accept a V0 payload, so §21.1 needs no exception for the headerless
format. The cost is the combination "write V0 and read protected V1 from one options instance",
which now needs two serializers.

Configuration time was chosen because every other contradiction in this codebase is rejected there
(§4.1), including the write version itself (Q4). The alternative — `BinaryFormatNotSupportedException`
on a V0 write and `BinaryIntegrityException` on a V0 read — would have been the only operation-time
validation of a *configuration* contradiction in the system.

A configured algorithm *without* a policy stays a capability, not a demand (§21.1), so it is not by
itself a contradiction; §10.2 states that it does not apply to a V0 write.

- [x] D2-01 — `RequireEncryption` with `WriteVersion == 0` is `BinaryConfigurationException` naming the write version *(§4.1, §21.1)* — `Api/OptionsTests`
- [x] D2-02 — `RequireEncryption` with `AllowV0Fallback` is `BinaryConfigurationException` naming the fallback *(§4.1, §21.1)* — `Api/OptionsTests`
- [x] D2-03 — `RequireChecksum` is refused on both sides the same way *(§4.1, §21.1)* — `Api/OptionsTests`
- [x] D2-04 — a configured algorithm without a policy builds under version 0 and leaves no trace in the bytes *(§10.2, §21.1)* — `Api/OptionsTests`

### D3 — an abstract class is handed to the activator instead of being refused — **FIXED**

Found while working M4 (CTR-21), reproduced against `src/` at `15aeb66`.

**Contract:** §23 — "Types without a parameterless constructor, including interfaces and abstract
classes without a `[BinaryUnion]` map, are `BinaryTypeException` on read." §8.10 admits no standard
.NET exception here.

**Actual:** an interface is refused correctly, an abstract class is not. Reading a payload as an
abstract type without a union map leaves the library through
`InvalidOperationException: Can't compile a NewExpression with a constructor declared on an abstract
class`, thrown by the expression compiler inside `ActivatorCache`, with no exception of the
documented taxonomy anywhere in the chain.

**Cause:** `TypeContract.Build` decided constructibility by looking for a parameterless constructor
alone. An abstract class declares one — the implicit protected constructor its subclasses chain to —
so `GetConstructor` finds it and the guard in `GraphReader.Construct` never fires. An interface only
escaped because it declares no constructor at all.

**Fix applied** in `Engine/TypeContract.cs`, at the layer that owns the member plan. Nothing on the
wire changed and the write path is untouched:

- the flag is now `CanBeConstructed`, and a type qualifies only when it is a value type, or a
  non-abstract class with a public or non-public parameterless constructor. `IsAbstract` covers
  interfaces and static classes alike, so the interface case keeps its behaviour by the same rule
  rather than by accident;
- the message in `GraphReader.Construct` now names what is missing — a concrete type, or a
  `[BinaryUnion]` map that says which type to build.

- [x] D3-01 — an abstract class without a union map is `BinaryTypeException` on read, not an activator failure *(§23, §8.10)* — `Contracts/AttributeContractTests`

### D4 — a default `ArraySegment<T>` leaves the library as `InvalidOperationException` — **FIXED**

Found while working M5 (RT-C06), reproduced against `src/` at `fb6e12c`.

**Contract:** §23 lists `ArraySegment<T>` among the supported types and carves out no state of it,
and its notes say `ImmutableArray<T>` is the only container that distinguishes default from empty —
so every other container's default instance is an ordinary empty one. §8.10 admits no standard .NET
exception here. Q11 has since made the clause explicit: a default `ArraySegment<T>` is written as an
empty segment.

**Actual:** serializing `default(ArraySegment<int>)` left the library through
`InvalidOperationException: The underlying array is null.`, with nothing of the documented taxonomy
in the chain. `Memory<T>` and `ReadOnlyMemory<T>` were unaffected: their default instance is empty
and their `ToArray()` returns an empty array.

**Cause:** `MemoryLikeFormatter.Enumerate` reached the elements through the value's own `ToArray()`.
`ArraySegment<T>.ToArray()` — alone among the three — refuses a default instance, because a default
segment has no backing array to copy from.

**Fix applied** in `Formatters/Sequences/SequenceFormatters.cs`, in the formatter that owns the
shape. A default segment now enumerates as empty, which is what §23 says it is. Nothing on the wire
changed: a value that previously could not be written at all is now written as the empty sequence of
§22.3, and no existing payload decodes differently.

- [x] D4-01 — a default `ArraySegment<T>` round-trips as an empty segment rather than failing *(§23, §8.10)* — `RoundTrip/Corpus`

### D5 — a zero-element multidimensional shape can still name a dimension the runtime refuses — **FIXED**

Found while working M6 (LIM-11), reproduced against `src/` at `09dd4c1`.

**Contract:** §5.2 says `MaxArrayLength` "limits one-dimensional array length and is also used for
array-specific dimension/product validation", and §17 requires every dimension non-negative, an
overflow-safe product, and the product within `MaxArrayLength`. §8.10 admits no standard .NET
exception for malformed wire data.

**Actual:** a 25-byte frame declaring the shape `[0, int.MaxValue]` passed validation — the product
collapses to zero, so neither the per-dimension check nor the product check had anything to refuse —
and then left the library as `System.OutOfMemoryException: Array dimensions exceeded supported
range.`, raised by `Array.CreateInstance`.

**Cause:** `ElementCount.ValidateShape` bounded the product but never a dimension on its own. §5.2
named dimension validation; only the product half was implemented.

**Fix applied** in `Io/ElementCount.cs`, the sole factory for a validated count. Each dimension is
now compared with `MaxArrayLength` before the product is computed, so the shape is refused as
`BinaryLimitException`. Nothing on the wire changed; the set of accepted payloads narrows, which is
what a limit does. §17 now states the per-dimension bound outright rather than leaving it to §5.2,
and the `MaxArrayLength` XML docs say it too.

- [x] D5-01 — a zero dimension beside an oversized one is a limit violation, not an `OutOfMemoryException` *(§5.2, §17)* — `Limits/ArrayShapeTests`
- [x] D5-02 — a zero dimension beside one exactly at the limit still succeeds *(§17)* — `Limits/ArrayShapeTests`

### D6 — a boolean accepted any non-zero byte, leaving authenticated header flags malleable — **FIXED**

Found while working M6 (HST-09), reproduced against `src/` at `09dd4c1`.

**Contract:** §22.1 encodes `bool` as "1 byte: `0` false, `1` true" — two encodings, not one and a
family. §13.1 binds the V1 header as associated data so that no header field can be altered without
breaking the tag.

**Actual:** flipping byte 14 of an encrypted, authenticated frame — the `KeyId` presence flag —
changed `0x01` to `0xFE` and the payload still decrypted and deserialized successfully. The
associated data is built from the *decoded* header fields, and `0xFE` decoded to the same `true`, so
the tag never saw the edit. Every boolean on the wire was affected the same way: the three optional
string flags, `PreserveReferences`, every null flag, and every `bool` value in a payload.

**Cause:** `ValueReader.ReadBoolean` returned `ReadOneByte(...) != 0`, which admits 255 spellings of
`true` where §22.1 admits one.

**Fix applied** in `Io/ValueReader.cs`, the single type that reads payload bytes. Only `0` and `1`
are accepted; anything else is `BinaryFormatException`. No writer ever produced another byte, so no
valid payload changes meaning — the reader simply stops accepting non-canonical input. §22.1 now
states the rejection and why it matters under §13.1.

- [x] D6-01 — every byte of an encrypted frame flipped in turn always fails *(§13.1, §22.1)* — `Hostile/MutationTests`
- [x] D6-02 — every byte of an encrypted header flipped in turn always fails *(§13.1)* — `Hostile/MutationTests`

## 30.2 Resolved contract questions

Raised while aligning the plan or while working it, decided on the project, and written into
`System-Contract.md` in the same change as the test that pins them. Kept as the record of why the
behavior is what it is.

| | Question | Decision | Contract |
|---|---|---|---|
| **Q1** | `SerializationOperation.EnableTrace` was set, copied and never read | Deleted. No trace facility is claimed, and `BinaryFormatDumper.DumpHeader` remains the whole of §19 diagnostics | — |
| **Q2** | `StreamExtensions` was absent from §3, and its header-derived overloads applied default limits with no way to override | Surface enumerated; `SerializationLimits? limits` threaded through every header-derived overload. The header supplies algorithms, never policy | §3.2 |
| **Q3** | Empty `byte[]` behavior was asserted but undefined, and the existing-instance overload returned `null` against its own documentation | Fail closed: no wire version encodes a value in zero bytes, so all three byte-array overloads throw `BinaryFormatException` and leave the target untouched | §3 |
| **Q4** | `WithVersion(n)` was unvalidated, and writing V0 required the read-side `AllowV0Fallback` | `Build()` rejects an unsupported write version with `BinaryConfigurationException`; the V0 pipeline is registered for writing whenever `WriteVersion == 0`, and `AllowV0Fallback` governs reading only | §4.1, §10.2 |
| **Q5** | The evaluation order of `MaxKeyedFields` against `MaxTotalKeyedFields` was unfixed | The per-object ceiling is evaluated first on both directions, so the reported limit is the one the payload actually broke | §5.9 |
| **Q6** | Header strings borrowed `MaxStringBytes`, a 4 MB payload policy, inside a security boundary | A fixed 256 UTF-8 byte ceiling belongs to the format. Above it on read is `BinaryFormatException`; a configured value too large to write is `BinaryConfigurationException` | §11, §22.6 |
| **Q7** | `Lazy<T>` write semantics were unspecified | Writing materializes the value and a factory exception is the caller's own, propagating unwrapped; reading yields a `Lazy<T>` that already holds the value, with `IsValueCreated` false until asked | §23 |
| **Q8** | §23 rejected a delegate member while `CLAUDE.md` and `TypeContract` skipped it silently | Reject, when the contract is built, naming the member. A delegate is eligible under the positional inclusion rules, so dropping it would lose state those rules said was included; `[BinaryIgnore]` states the intent. Decided at plan-build time, never per value, because a null callback must not serialize where a set one fails. Events are unaffected: their backing field is private | §14.1, §23 |
| **Q9** | V0 refused `[BinaryContract]` as if keyed encoding were a format capability, although the keyed layout is payload-level and needs no header | Keyed contracts belong to the type and apply under both wire formats; the pipeline flag that could refuse them is removed, since it could no longer be `false`. The one format-visible consequence is the seekable-payload requirement: a field's length is patched after the field is written, which V1 hides by buffering the payload and V0 passes to the caller's destination as `NotSupportedException`. Reference preservation stays V1-only for the opposite reason — it is an options-level switch that silently changes the bytes, and a headerless format cannot announce it, so a reader configured differently would decode wrong data with no diagnostic | §10.2, §14.2, §22.8 |
| **Q10** | §4.1 listed "an encryption algorithm without key material" among the rejections `Build()` performs, but every `WithEncryption` overload assigns the key source together with the algorithm and refuses a null one, so no caller could reach the guard | Contract narrowed: the bullet is removed and §4.1 states that missing key material is not a configuration contradiction. The guard stays as an invariant over the constructed options. A reader whose options name an algorithm it has no key for — `FromHeader`/`FromStream` with no keys, a resolver that yields nothing, a mismatched `keyId` — fails at the operation as `BinaryEncryptionKeyException`. CFG-07 is rewritten to assert the overloads leave no gap | §4.1, §8.7 |
| **Q11** | §23 listed the memory-like types and §22.3 encoded them as a bare count and elements, so a segment's offset into a larger array and a sequence's segment boundaries could not survive a round trip — derivable, but never stated, and invisible to anyone reading §23 alone | Contract states it: a memory-like value travels as its elements alone, so the backing storage is not part of the value. A read builds a fresh array and wraps the whole of it — an `ArraySegment<T>` comes back at offset zero over an array exactly as long as the segment, a multi-segment `ReadOnlySequence<T>` comes back as one segment, and a default `ArraySegment<T>`, which has no backing array, is written as empty. That last clause is the rule D4 was fixed against, now said outright rather than inferred from the `ImmutableArray<T>` note. RT-50 and RT-52 assert the offset and the segment count, not only the elements | §23 |

---

# 31. Cross-entry-point equivalence — `Api/`

For one logical value under one configuration, all entry points must agree.

- [ ] XEP-01 — `byte[]` serialize/deserialize *(§3.1)*
- [ ] XEP-02 — `Stream` serialize/deserialize *(§3.1)*
- [ ] XEP-03 — `StreamExtensions` *(§3.2)*
- [ ] XEP-04 — the existing-instance overloads *(§3)*
- [ ] XEP-05 — the `ref` value-type overloads *(§3)*
- [ ] XEP-06 — parity under the full V1 pipeline; encrypted payloads compare semantics, never ciphertext bytes *(§22.6)*
- [ ] XEP-07 — parity under V0 for every shape V0 supports *(§10.2)*
- [x] EXT-01 — every §3 public type is reachable from an external consumer assembly *(§3)* — `Api/PublicSurfaceTests`
- [ ] EXT-02 — no normal usage requires an internal type *(§3)* — `Api/PublicSurfaceTests` proves the engine types are not exported; the claim itself needs the separate consumer assembly of M8
- [ ] EXT-03 — the public algorithm primitives are constructible and implementable externally *(§3)*
- [ ] EXT-04 — every public member carries XML documentation; CS1591 remains a build error *(§3)*
- [x] EXT-05 — the compiled public surface contains nothing beyond §3 *(§3)* — `Api/PublicSurfaceTests`

---

# 32. Release gate

Checked only when source **and** a test prove it. Mirrors `System-Contract.md` §24.

## Correctness

- [x] Every formatter family in `FormatterRegistry` has mapped coverage. *(RT-01…RT-88, RT-C10; the delegate rejection in CTR-22)*
- [x] Every §23 family round-trips, including nullability and empty containers. *(RT-01…RT-88, RT-B06, RT-C01, RT-C10)*
- [x] Interface resolution and ordering guarantees are asserted, not assumed. *(RT-C02…RT-C05)*
- [ ] Every public entry point is covered and mutually consistent.

## Format

- [x] Every row of §22 is pinned at the byte level. *(WF-01…WF-30)*
- [x] V1 header validation is deterministic and ordered. *(HDR-01…HDR-19, ENV-01, ENV-02)*
- [x] V0 is never confused with V1 and is never selected without the caller's opt-in. *(V0-10…V0-17)*
- [x] V0 carries the same type set, unions, keyed contracts, limits and budgets as V1 — only the envelope is absent. *(V0-03, V0-07, V0-08, V0-19…V0-21)*
- [x] Committed fixed-byte fixtures decode; none is regenerated by the code under test. *(V0-18, UTIL-09)*

## Contracts

- [x] Every attribute rule and every contradiction is covered. *(CTR-01…CTR-26)*
- [x] Keyed evolution — skip, add, remove, unknown, duplicate, truncated — is covered. *(KEY-01…KEY-18)*
- [x] Polymorphism is covered on both read and write, including write-side rejection. *(PM-01…PM-16)*
- [x] Reference scopes and cycle behavior are covered. *(REF-01…REF-17, CYC-01…CYC-10)*

## Security

- [ ] Every limit has below / exact / above / invalid.
- [ ] Cumulative element, node and keyed-field budgets are covered.
- [ ] Declared lengths are proven to be checked against physically available bytes before allocation, on the wire as well as inside the payload (D1).
- [ ] The malformed and truncated corpus passes with no uncontrolled failure.
- [ ] Stream wrappers, key ownership and buffer clearing are covered.
- [ ] No test can cause a process-fatal stack overflow.

## Exceptions

- [ ] The taxonomy is pinned branch by branch.
- [ ] No OR-list or `ThrowsAny` assertion remains without a documented reason.
- [ ] No raw framework exception escapes a declared truncation.
- [ ] Inner exceptions are preserved where §9 requires it.

## Process

- [ ] Every checkpoint in this document is `[x]` or `BLOCKED (Qn)`.
- [x] Every question in §30.2 is resolved, and the contract updated accordingly.
- [ ] Every bug found during testing was fixed in `src/`, not accommodated by a test.
- [ ] Every defect in §30.1 is fixed and pinned by its checkpoint.
- [ ] No test relies on undocumented project history.
- [ ] `dotnet test` is green with no skipped tests.
