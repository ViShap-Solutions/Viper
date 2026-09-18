# ViShap.Viper — QA Plan

**Target release:** v1.0.0  
**Status:** Release-gate test specification  
**Framework:** xUnit 2.9+  
**Scope:** production `src/` behavior
**Companion:** `System-Contract.md`

---

# 1. QA philosophy

The test suite is an executable verification of `System-Contract.md`.

A test is complete only when it asserts the behavior that matters:

- exact result semantics;
- exact runtime type where contractual;
- exact reference identity where contractual;
- exact exception type;
- exact limit boundary;
- exact stream ownership/position behavior where contractual;
- exact V0/V1 distinction;
- exact member/key inclusion rules.

Never broaden an assertion merely because multiple exceptions are convenient for a current implementation.

Forbidden as a default pattern:

```csharp
Assert.Throws<Exception>(...)
```

and:

```csharp
Assert.True(
    ex is BinaryFormatException ||
    ex is EndOfStreamException ||
    ex is IOException);
```

A documented failure has one expected exception category.

---

# 2. Test layers

- [ ] **Layer 1 — Public contract:** no internals; consume the library as a normal application.
- [ ] **Layer 2 — Internal security:** use `InternalsVisibleTo` for budget, guard, header, stream-wrapper, cache, and registry invariants.
- [ ] **Layer 3 — Property/fuzz:** generated valid objects and malformed bytes.
- [ ] **Layer 4 — Concurrency:** actual parallel execution, not artificial serial loops.
- [ ] **Layer 5 — Benchmarks:** BenchmarkDotNet only; never mixed with correctness assertions.

---

# 3. Test style and xUnit rules

## 3.1 Arrange / Act / Assert

Every test should have a readable three-stage structure.

Prefer:

```csharp
var serializer = new BinarySerializer(options);

var bytes = serializer.Serialize(value);

var result = serializer.Deserialize<MyType>(bytes);

Assert.Equal(expected, result);
```

For failure cases:

```csharp
var ex = Assert.Throws<BinaryLimitException>(
    () => serializer.Deserialize<MyType>(bytes));

Assert.Contains("MaxArrayLength", ex.Message);
```

Do not use `Assert.ThrowsAny<T>` for a contract failure unless the contract intentionally allows a family and the test documents why.

## 3.2 Exact type assertions

Use:

```csharp
Assert.Equal(typeof(ExpectedType), actual!.GetType());
```

when runtime type is contractual.

For collections, test contents separately from runtime type.

## 3.3 Reference assertions

Use:

```csharp
Assert.Same(expected, actual);
Assert.NotSame(expected, actual);
```

Do not replace identity assertions with equality.

## 3.4 Dictionaries and sets

Never rely on arbitrary enumeration order.

Use membership/content assertions unless the type contract guarantees ordering.

## 3.5 Stack/queue/priority queue

Use behavioral assertions:

```text
Stack    → repeated Pop()
Queue    → repeated Dequeue()
Priority → repeated Dequeue with expected priorities
```

Do not assume heap/internal enumeration order.

---

# 4. Required test profiles

## P0 — Default

```csharp
BinarySerializerOptions.Default
```

- [ ] default V1 round trip
- [ ] default API stability

## P1 — Explicit defaults

```csharp
BinarySerializerOptions.Configure().Build()
```

- [ ] semantic equivalence with P0

## P2 — Preserve references

```csharp
BinarySerializerOptions.Configure()
    .PreserveReferences(true)
    .Build();
```

- [ ] shared references
- [ ] reference markers
- [ ] cycles according to contract

## P3 — Tight limits

```csharp
var limits = new SerializationLimits
{
    MaxDepth = 4,
    MaxArrayLength = 3,
    MaxCollectionLength = 3,
    MaxDictionaryEntries = 2,
    MaxStringBytes = 8,
    MaxByteBlobBytes = 8,
    MaxTotalElements = 5,
    MaxObjectGraphNodes = 5,
    MaxKeyedFields = 3,
    MaxPayloadBytes = 32,
    MaxCompressedBytes = 32,
    MaxEncryptedBytes = 64,
    MaxWireBytes = 64
};
```

- [ ] every boundary has exact-limit and one-above tests

## P4 — Deflate

- [ ] V1 Deflate round trip
- [ ] Deflate malformed-input behavior

## P5 — Brotli

- [ ] V1 Brotli round trip
- [ ] Brotli malformed-input behavior

## P6 — CRC32

- [ ] checksum round trip
- [ ] mismatch rejection

## P7 — AES-256-GCM

- [ ] 32-byte key round trip
- [ ] key resolver
- [ ] header key-id routing

## P8 — Full V1

Compression + checksum + encryption together.

- [ ] Brotli + CRC32 + AES-GCM
- [ ] Deflate + CRC32 + AES-GCM

## P9 — V0

```csharp
BinarySerializerOptions.Configure()
    .WithVersion(0)
    .AllowV0Fallback(true)
    .Build();
```

- [ ] V0 write
- [ ] V0 read
- [ ] positional-only semantics
- [ ] keyed-contract rejection

---

# 5. Public API tests — `Api/`

- [ ] API-01 `new BinarySerializer()`
- [ ] API-02 options constructor
- [ ] API-03 `BinarySerializerOptions.Default`
- [ ] API-04 builder/default equivalence
- [ ] API-05 external-consumer `record with`
- [ ] API-06 `WithLimits`
- [ ] API-07 compression/checksum/encryption builder
- [ ] API-08 fixed key overload
- [ ] API-09 key-resolver overload
- [ ] API-10 resolver receives header `keyId`
- [ ] API-11 `PreserveReferences`
- [ ] API-12 V0 write selection
- [ ] API-13 V0 fallback on/off
- [ ] API-14 byte-array/stream parity
- [ ] API-15 existing class instance overload
- [ ] API-16 `ref struct` overload
- [ ] API-17 null required arguments
- [ ] API-18 caller stream remains open

---

# 6. Exception taxonomy tests — `Exceptions/`

## 6.1 Hierarchy

- [ ] BinarySerializerException is abstract base
- [ ] BinaryLimitException derives from BinaryFormatException
- [ ] BinaryEncryptionKeyException derives from BinaryEncryptionException
- [ ] all other documented Viper exceptions derive directly from the expected branch

## 6.2 Configuration

- [ ] invalid `MaxDepth = 0` → `BinaryConfigurationException`
- [ ] negative `MaxDepth` → `BinaryConfigurationException`
- [ ] invalid array/collection/dictionary limits → `BinaryConfigurationException`
- [ ] invalid string/blob limits → `BinaryConfigurationException`
- [ ] invalid total/node/keyed-field limits → `BinaryConfigurationException`
- [ ] invalid phase limits → `BinaryConfigurationException`

## 6.3 Format

- [ ] truncated V1 header → `BinaryFormatException`
- [ ] truncated payload → `BinaryFormatException`
- [ ] malformed 7-bit integer → `BinaryFormatException`
- [ ] negative wire count → `BinaryFormatException`
- [ ] negative wire length → `BinaryFormatException`
- [ ] invalid reference marker → `BinaryFormatException`
- [ ] malformed keyed framing → `BinaryFormatException`

## 6.4 Limit

- [ ] array count one above max → `BinaryLimitException`
- [ ] collection count one above max → `BinaryLimitException`
- [ ] dictionary entries one above max → `BinaryLimitException`
- [ ] string byte length one above max → `BinaryLimitException`
- [ ] blob one above max → `BinaryLimitException`
- [ ] cumulative element budget overrun → `BinaryLimitException`
- [ ] graph-node budget overrun → `BinaryLimitException`
- [ ] depth overrun → `BinaryLimitException`
- [ ] payload phase overrun → `BinaryLimitException`
- [ ] compressed phase overrun → `BinaryLimitException`
- [ ] encrypted phase overrun → `BinaryLimitException`
- [ ] wire phase overrun → `BinaryLimitException`

## 6.5 Unsupported

- [ ] unknown format version → `BinaryFormatNotSupportedException`
- [ ] unknown built-in algorithm → `BinaryFormatNotSupportedException`
- [ ] missing custom algorithm registration → `BinaryFormatNotSupportedException`
- [ ] V0 keyed contract → `BinaryFormatNotSupportedException`

## 6.6 Integrity/crypto

- [ ] checksum mismatch → `BinaryIntegrityException`
- [ ] GCM tag failure → `BinaryIntegrityException`
- [ ] missing key → `BinaryEncryptionKeyException`
- [ ] resolver returns null → `BinaryEncryptionKeyException`
- [ ] key-id mismatch → `BinaryEncryptionKeyException`
- [ ] no decrypt capability for encrypted payload → `BinaryEncryptionKeyException` or documented encryption/configuration subtype, exactly one defined result

## 6.7 Streams/types

- [ ] underlying `IOException` → `BinaryStreamException`
- [ ] inner `IOException` preserved
- [ ] invalid contract/type → `BinaryTypeException`
- [ ] invalid union configuration → `BinaryTypeException`
- [ ] invalid polymorphic runtime type → `BinaryTypeException`

---

# 7. Zero and negative boundary rules

This section exists to prevent the old ambiguity.

- [ ] configured limit `0` → `BinaryConfigurationException`
- [ ] configured limit `< 0` → `BinaryConfigurationException`
- [ ] wire count `-1` → `BinaryFormatException`
- [ ] wire length `-1` → `BinaryFormatException`
- [ ] collection count `0` succeeds where zero is a valid collection size
- [ ] dictionary count `0` succeeds
- [ ] array count `0` succeeds
- [ ] string byte length `0` succeeds
- [ ] blob length `0` succeeds
- [ ] bit count `0` succeeds
- [ ] multidimensional zero dimension follows the documented empty-array semantics
- [ ] header zero lengths are accepted when the combination is internally consistent
- [ ] one above each limit → `BinaryLimitException`

---

# 8. Formatter round-trip coverage — `RoundTrip/`

## 8.1 Primitive

- [ ] bool
- [ ] byte
- [ ] sbyte
- [ ] short
- [ ] ushort
- [ ] int
- [ ] uint
- [ ] long
- [ ] ulong
- [ ] float
- [ ] double
- [ ] decimal
- [ ] char
- [ ] string
- [ ] enum
- [ ] Half
- [ ] Int128
- [ ] UInt128
- [ ] IntPtr
- [ ] UIntPtr
- [ ] Rune
- [ ] BigInteger

Boundary values:

- [ ] zero
- [ ] min/max
- [ ] `-1` where signed
- [ ] NaN/infinity for floating-point
- [ ] Unicode/surrogate boundaries
- [ ] BigInteger multi-byte encodings

## 8.2 Time/system

- [ ] DateTime min/max/kind
- [ ] DateTimeOffset
- [ ] TimeSpan min/max/negative/fractional
- [ ] DateOnly
- [ ] TimeOnly
- [ ] TimeZoneInfo
- [ ] StringBuilder
- [ ] CultureInfo
- [ ] Guid
- [ ] Uri
- [ ] Version
- [ ] BitArray
- [ ] Lazy<T>

## 8.3 Numeric

- [ ] Complex
- [ ] Plane
- [ ] Quaternion
- [ ] Matrix3x2
- [ ] Matrix4x4
- [ ] Vector2
- [ ] Vector3
- [ ] Vector4

Assert every component.

## 8.4 Arrays/memory

- [ ] one-dimensional primitive arrays
- [ ] one-dimensional reference arrays
- [ ] empty arrays
- [ ] null array elements
- [ ] multidimensional rank 1/2/3 where supported
- [ ] zero dimensions
- [ ] Memory<T>
- [ ] ReadOnlyMemory<T>
- [ ] ArraySegment<T> with offset/count
- [ ] ReadOnlySequence<T>
- [ ] multi-segment ReadOnlySequence

## 8.5 Tuples

- [ ] KeyValuePair
- [ ] Tuple
- [ ] ValueTuple
- [ ] nested tuple
- [ ] nullable tuple elements where legal

## 8.6 Collections

- [ ] List<T>
- [ ] HashSet<T>
- [ ] SortedSet<T>
- [ ] LinkedList<T>
- [ ] ObservableCollection<T>
- [ ] Stack<T>
- [ ] Queue<T>
- [ ] PriorityQueue<TElement,TPriority>
- [ ] custom ICollection<T>

## 8.7 Concurrent

- [ ] ConcurrentBag<T>
- [ ] ConcurrentQueue<T>
- [ ] ConcurrentStack<T>
- [ ] ConcurrentDictionary<TKey,TValue>

## 8.8 Dictionaries

- [ ] Dictionary<TKey,TValue>
- [ ] SortedDictionary<TKey,TValue>
- [ ] SortedList<TKey,TValue>

## 8.9 Read-only

- [ ] ReadOnlyCollection<T>
- [ ] ReadOnlyDictionary<TKey,TValue>
- [ ] ReadOnlyObservableCollection<T>

## 8.10 Immutable

- [ ] ImmutableArray<T>
- [ ] ImmutableList<T>
- [ ] ImmutableHashSet<T>
- [ ] ImmutableSortedSet<T>
- [ ] ImmutableStack<T>
- [ ] ImmutableQueue<T>
- [ ] ImmutableDictionary<TKey,TValue>
- [ ] ImmutableSortedDictionary<TKey,TValue>

Special regression:

- [ ] IMM-REG-01 `ImmutableArray<T>` writer obtains backing array through `ImmutableCollectionsMarshal.AsArray<T>`
- [ ] IMM-REG-02 no reflective instance `ToArray` lookup is used for ImmutableArray serialization
- [ ] IMM-REG-03 immutable array count is validated before wire encoding

## 8.11 Frozen

- [ ] FrozenDictionary<TKey,TValue>
- [ ] FrozenSet<T>

## 8.12 Nested graphs

- [ ] nested POCO
- [ ] public fields
- [ ] public properties
- [ ] private `[BinaryInclude]`
- [ ] ignored member
- [ ] nested collection
- [ ] nested dictionary
- [ ] struct containing reference
- [ ] three-or-more formatter families in one graph

---

# 9. Attribute and contract tests — `Attributes/`

- [ ] ATTR-01 public property inclusion
- [ ] ATTR-02 public field inclusion
- [ ] ATTR-03 `[BinaryIgnore]`
- [ ] ATTR-04 private property `[BinaryInclude]`
- [ ] ATTR-05 private field `[BinaryInclude]`
- [ ] ATTR-06 ignore/include interaction
- [ ] ATTR-07 explicit `[BinaryOrder]`
- [ ] ATTR-08 duplicate order → `BinaryTypeException`
- [ ] ATTR-09 `[BinaryKey]` without `[BinaryContract]` → `BinaryTypeException`
- [ ] ATTR-10 complete keyed contract
- [ ] ATTR-11 keyed + ignored members
- [ ] ATTR-12 missing key/ignore decision → `BinaryTypeException`
- [ ] ATTR-13 `[BinaryContract] + [BinaryInclude]` → `BinaryTypeException`
- [ ] ATTR-14 `[BinaryContract] + [BinaryOrder]` → `BinaryTypeException`
- [ ] ATTR-15 duplicate key → `BinaryTypeException`
- [ ] ATTR-16 7-bit key boundaries

Regression:

- [ ] ATTR-REG-01 `[BinaryKey] + [BinaryIgnore]` is rejected as a contradictory declaration and cannot silently leak the member into the payload.

---

# 10. Schema evolution — `SchemaVersioning/`

- [ ] KEY-01 same schema round trip
- [ ] KEY-02 removed member skipped
- [ ] KEY-03 added member remains CLR default when absent
- [ ] KEY-04 unknown key among known keys
- [ ] KEY-05 unknown nested payload skipped
- [ ] KEY-06 bounded unknown blob skipped
- [ ] KEY-07 malformed/truncated unknown field → `BinaryFormatException`
- [ ] KEY-08 duplicate key → exact documented strict failure
- [ ] KEY-09 negative/invalid key encoding → `BinaryFormatException`
- [ ] KEY-10 varint key boundaries
- [ ] KEY-11 polymorphic keyed member
- [ ] KEY-12 cycle crossing keyed boundary
- [ ] KEY-13 reference identity across keyed members

Regression lesson:

- [ ] KEY-REG-01 unknown-field handling must not accidentally invoke a formatter for an unavailable declared type.

---

# 11. Polymorphism — `Polymorphism/`

- [ ] PM-01 registered derived type
- [ ] PM-02 multiple derived types
- [ ] PM-03 registered concrete base
- [ ] PM-04 union inside collection
- [ ] PM-05 union inside dictionary value
- [ ] PM-06 union inside keyed member
- [ ] PM-07 unknown discriminator → `BinaryTypeException`
- [ ] PM-08 unregistered runtime type on write → `BinaryTypeException`
- [ ] PM-09 duplicate union tag → `BinaryTypeException`
- [ ] PM-10 tag outside byte range → `BinaryTypeException`
- [ ] PM-11 non-assignable derived type → `BinaryTypeException`
- [ ] PM-12 concurrent first-touch map construction

Regression lesson:

- [ ] PM-REG-01 implicit positional polymorphism must not reinterpret derived bytes as base bytes.

---

# 12. Reference preservation — `ReferencePreservation/`

- [ ] REF-01 shared reference
- [ ] REF-02 default mode creates distinct instances when that is the documented behavior
- [ ] REF-03 repeated object reference
- [ ] REF-04 serialized preserve flag controls payload interpretation
- [ ] REF-05 invalid marker
- [ ] REF-06 unknown reference ID
- [ ] REF-07 negative reference ID at relevant low-level boundary
- [ ] REF-08 shared polymorphic instance
- [ ] REF-09 shared identity across keyed members

Reference assertions must use `Assert.Same`/`Assert.NotSame`.

---

# 13. Circular references — `CircularReferences/`

- [ ] CYC-01 direct self-reference
- [ ] CYC-02 two-object cycle
- [ ] CYC-03 cycle through collection
- [ ] CYC-04 cycle through dictionary
- [ ] CYC-05 cycle through polymorphism
- [ ] CYC-06 cycle through struct wrapper
- [ ] CYC-07 shared DAG without cycle succeeds
- [ ] CYC-08 equal-but-reference-distinct objects succeed
- [ ] CYC-09 below depth limit succeeds
- [ ] CYC-10 exact configured boundary follows inclusive/exclusive contract
- [ ] CYC-11 over-depth case is deterministic and never depends on `StackOverflowException`

No test may intentionally cause process-fatal stack overflow.

---

# 14. Existing-instance APIs — `ExistingInstance/`

- [ ] EXI-01 reference-type reuse preserves object identity
- [ ] EXI-02 sequential reuse overwrites serialized members
- [ ] EXI-03 null root does not mutate existing object
- [ ] EXI-04 incompatible existing base/derived combination
- [ ] EXI-05 root reference-only payload rejection
- [ ] EXI-06 `ref struct` receives serialized value
- [ ] EXI-07 stream/byte[] parity
- [ ] EXI-08 full V1 pipeline parity

Regression:

- [ ] EXI-REG-01 primitive/value-type `ref` overload does not silently leave the caller's value unchanged.

---

# 15. Security limits — `Limits/`

Every limit has:

```text
below
exact boundary
one above
extreme invalid
```

where the wire format permits those values.

## 15.1 Configuration validation

- [ ] LIM-CFG-01 all zero configuration values → `BinaryConfigurationException`
- [ ] LIM-CFG-02 all negative configuration values → `BinaryConfigurationException`
- [ ] LIM-CFG-03 valid positive configuration succeeds

## 15.2 Array/collection/dictionary

- [ ] LIM-ARR-01 negative → `BinaryFormatException`
- [ ] LIM-ARR-02 exact max succeeds with sufficient payload
- [ ] LIM-ARR-03 max+1 → `BinaryLimitException`
- [ ] LIM-COL-01 same collection matrix
- [ ] LIM-DICT-01 same dictionary matrix

## 15.3 Strings/blobs/bits

- [ ] LIM-STR-01 zero length succeeds
- [ ] LIM-STR-02 exact `MaxStringBytes`
- [ ] LIM-STR-03 one above → `BinaryLimitException`
- [ ] LIM-STR-04 malformed/truncated varint → `BinaryFormatException`
- [ ] LIM-BLOB-01 exact `MaxByteBlobBytes`
- [ ] LIM-BLOB-02 one above → `BinaryLimitException`
- [ ] LIM-BIT-01 0/1/7/8/9
- [ ] LIM-BIT-02 exact byte-bit boundary
- [ ] LIM-BIT-03 one above → `BinaryLimitException`

## 15.4 Multidimensional arrays

- [ ] LIM-MDA-01 negative dimension → `BinaryFormatException`
- [ ] LIM-MDA-02 zero dimension semantics
- [ ] LIM-MDA-03 exact product
- [ ] LIM-MDA-04 product one above → `BinaryLimitException`
- [ ] LIM-MDA-05 multiplication overflow → `BinaryLimitException`

## 15.5 Cumulative budget

- [ ] LIM-BUD-01 two individually legal collections exceed `MaxTotalElements`
- [ ] LIM-BUD-02 cumulative count increases exactly once per validated collection
- [ ] LIM-BUD-03 budget never decreases
- [ ] LIM-BUD-04 budget is fresh for each public operation
- [ ] LIM-BUD-05 failed operation cannot poison a later independent operation

## 15.6 Object graph nodes

- [ ] LIM-NODE-01 one node under limit
- [ ] LIM-NODE-02 exact node boundary
- [ ] LIM-NODE-03 current tracked-node boundary
- [ ] LIM-NODE-04 repeated references do not double-count an already established identity

## 15.7 Depth

- [ ] LIM-DEPTH-01 exact boundary
- [ ] LIM-DEPTH-02 current tracked-depth boundary
- [ ] LIM-DEPTH-03 sibling nesting unwinds correctly on the currently tracked path
- [ ] LIM-DEPTH-04 exception unwinds depth to previous value

## 15.8 Phase byte limits

- [ ] LIM-PHASE-01 `MaxPayloadBytes`
- [ ] LIM-PHASE-02 `MaxCompressedBytes`
- [ ] LIM-PHASE-03 `MaxEncryptedBytes`
- [ ] LIM-PHASE-04 `MaxWireBytes`
- [ ] LIM-PHASE-05 each header-declared phase length obeys its documented current validation order; S06 is deferred

---

# 16. Stream behavior — `Streams/`

## 16.1 `BudgetedReadStream`

- [ ] STR-01 total bytes under budget succeed
- [ ] STR-02 exact budget succeeds
- [ ] STR-03 request beyond remaining budget fails with `BinaryLimitException`
- [ ] STR-04 underlying `IOException` becomes `BinaryStreamException`
- [ ] STR-05 `CanSeek`/`Position`/`Seek` reflect underlying capability
- [ ] STR-06 caller stream remains open

## 16.2 `BudgetedWriteStream`

- [ ] STR-07 bytes under budget succeed
- [ ] STR-08 exact budget succeeds
- [ ] STR-09 budget violation → `BinaryLimitException`
- [ ] STR-10 keyed-field position rewrite does not double-charge unchanged bytes
- [ ] STR-11 current write-budget boundary behavior is explicitly documented and tested; non-zero-offset hardening remains deferred
- [ ] STR-12 underlying `IOException` becomes `BinaryStreamException`
- [ ] STR-13 caller stream remains open

## 16.3 `BoundedReadStream`

- [ ] STR-14 read exactly the declared field boundary
- [ ] STR-15 a field decoder cannot consume the next field
- [ ] STR-16 short/truncated field → `BinaryFormatException`
- [ ] STR-17 unknown field skip uses bounded chunks
- [ ] STR-18 known field decoding shares the parent budget/reference state

## 16.4 Public stream behavior

- [ ] STR-19 seekable MemoryStream round trip
- [ ] STR-20 non-seekable stream rejected only by APIs that require seekability
- [ ] STR-21 partial-read stream works
- [ ] STR-22 premature EOF → `BinaryFormatException`
- [ ] STR-23 read-only destination rejects using normal BCL capability semantics
- [ ] STR-24 non-readable source rejects using normal BCL capability semantics

---

# 17. V1 header and wire tests — `Format/`

- [ ] HDR-01 magic mismatch
- [ ] HDR-02 unsupported version
- [ ] HDR-03 fixed-header truncation → `BinaryFormatException`
- [ ] HDR-04 optional-string zero/exact/above boundary
- [ ] HDR-05 invalid enum
- [ ] HDR-06 no-compression length inconsistency
- [ ] HDR-07 no-encryption length inconsistency
- [ ] HDR-08 checksum truncation
- [ ] HDR-09 payload truncation
- [ ] HDR-10 extra bytes after payload handling
- [ ] HDR-11 inspection preserves position
- [ ] HDR-12 unknown stream is not treated as a valid V1 header

Regression rule:

- [ ] HDR-REG-01 truncation assertions must not accept a raw `EndOfStreamException` as equivalent to `BinaryFormatException`.

---

# 18. Payload consumption

A typed root decode must respect the declared payload boundary.

- [ ] PAY-01 exact current payload consumption behavior is documented and tested
- [ ] PAY-02 trailing-byte enforcement is deferred; no current guarantee is claimed until S10 is closed
- [ ] PAY-03 nested field consumes exactly its bounded field payload
- [ ] PAY-04 unknown keyed field is skipped without reading beyond its declared length
- [ ] PAY-05 compressed/encrypted unwrap produces exactly the declared phase length

---

# 19. Compression/checksum/encryption tests — `Algorithms/`

## Compression

- [ ] CMP-01 None
- [ ] CMP-02 Deflate
- [ ] CMP-03 Brotli
- [ ] CMP-04 malformed Deflate
- [ ] CMP-05 malformed Brotli
- [ ] CMP-06 declared uncompressed length above `MaxPayloadBytes`
- [ ] CMP-07 compressed input above `MaxCompressedBytes`
- [ ] CMP-08 compressed output above `MaxCompressedBytes`
- [ ] CMP-09 current declared-length failure behavior is tested
- [ ] CMP-10 exact-output enforcement is deferred until S09 is closed

## Checksum

- [ ] CHK-01 None
- [ ] CHK-02 CRC32
- [ ] CHK-03 mismatch → `BinaryIntegrityException`
- [ ] CHK-04 wrong checksum length
- [ ] CHK-05 custom checksum
- [ ] CHK-06 missing custom checksum → `BinaryFormatNotSupportedException`

## AES-GCM

- [ ] ENC-01 valid 32-byte key
- [ ] ENC-02 wrong key → exact integrity/key exception per contract
- [ ] ENC-03 ciphertext tampering → `BinaryIntegrityException`
- [ ] ENC-04 key ID mismatch → `BinaryEncryptionKeyException`
- [ ] ENC-05 missing resolver result → `BinaryEncryptionKeyException`
- [ ] ENC-06 invalid direct key size → `ArgumentException`
- [ ] ENC-07 current fixed-key lifecycle behavior is documented and tested
- [ ] ENC-08 resolver-owned buffer ownership is deferred until S07 is closed
- [ ] ENC-09 encryptor usable-state behavior is documented
- [ ] ENC-10 post-Dispose hardening is deferred until S08 is closed
- [ ] ENC-11 temporary encryption buffers are cleared where required
- [ ] ENC-12 resolver receives header key ID

---

# 20. Algorithm registries — `Algorithms/Registry/`

- [ ] REG-01 built-in resolution
- [ ] REG-02 replace registered factory
- [ ] REG-03 custom registration
- [ ] REG-04 missing custom registration
- [ ] REG-05 null factory
- [ ] REG-06 invalid custom name
- [ ] REG-07 replacement semantics
- [ ] REG-08 concurrent resolution
- [ ] REG-09 registry mutation does not race unrelated tests
- [ ] REG-10 custom registration state is isolated/cleaned between tests

---

# 21. Inspector and metadata — `Inspection/`

- [ ] INS-01 valid V1 header
- [ ] INS-02 source position restored
- [ ] INS-03 non-seekable source → `NotSupportedException`
- [ ] INS-04 unrecognized bytes
- [ ] INS-05 `BinarySerializerOptions.FromHeader`
- [ ] INS-06 `FromStream`
- [ ] INS-07 limits passed to inspection are honored
- [ ] INS-08 underlying inspection `IOException` → `BinaryStreamException`

---

# 22. Diagnostics — `Diagnostics/`

- [ ] DMP-01 valid header dump
- [ ] DMP-02 unknown input
- [ ] DMP-03 successful object dump
- [ ] DMP-04 failed parse dump
- [ ] DMP-05 trace fields
- [ ] DMP-06 trace is bounded
- [ ] DMP-07 shared-reference marker
- [ ] DMP-08 cycle handling terminates
- [ ] DMP-09 diagnostic catch-all does not reclassify production exceptions

---

# 23. Concurrency and cache correctness — `Concurrency/`

- [ ] CN-01 shared serializer concurrent use
- [ ] CN-02 TypeAccessorCache first touch
- [ ] CN-03 PolymorphicTypeCache first touch
- [ ] CN-04 formatter cache first touch
- [ ] CN-05 collection accessor caches
- [ ] CN-06 registry concurrency
- [ ] CN-07 serializer operation state is isolated per call
- [ ] CN-08 budget objects are not shared between operations
- [ ] CN-09 concurrent encryption/decryption with resolver key IDs
- [ ] CN-10 concurrent inspector usage on separate streams

---

# 24. Cross-entry-point equivalence

For the same logical value/configuration:

- [ ] XEP-01 byte[] serialize/deserialize
- [ ] XEP-02 stream serialize/deserialize
- [ ] XEP-03 extension API
- [ ] XEP-04 existing-instance API
- [ ] XEP-05 `ref struct` API
- [ ] XEP-06 full V1 pipeline parity
- [ ] XEP-07 V0 parity where V0 supports the shape; payload-read hardening S05 is tracked separately

Encrypted payloads must compare semantic result/header semantics rather than exact ciphertext bytes.

---

# 25. V0/V1 compatibility

## V1

- [ ] V1 default write
- [ ] explicit V1
- [ ] all supported algorithm combinations
- [ ] keyed contracts
- [ ] polymorphism
- [ ] references
- [ ] limits
- [ ] inspection

## V0

- [ ] V0 explicit write
- [ ] V0 round trip
- [ ] V0 positional-only
- [ ] V0 keyed rejection
- [ ] V1 reader fallback enabled
- [ ] fallback disabled behavior
- [ ] V1 does not misinterpret V0 bytes
- [ ] V0 does not require V1 metadata
- [ ] V0 current payload-limit semantics are documented; S05 read-side hardening is deferred

Fixed compatibility fixtures:

- [ ] V0 fixtures committed
- [ ] V1 fixtures committed
- [ ] writer/reader are not both allowed to regenerate the same fixture during the test

---

# 26. Property-based and metamorphic tests

- [ ] PROP-01 primitive closure
- [ ] PROP-02 collection closure within limits
- [ ] PROP-03 acyclic nested graph closure
- [ ] PROP-04 hash collection order independence
- [ ] PROP-05 sorted ordering determinism
- [ ] PROP-06 corruption locality
- [ ] PROP-07 limit monotonicity
- [ ] PROP-08 cumulative budget monotonicity
- [ ] PROP-09 serialize/deserialize entry-point equivalence
- [ ] PROP-10 malformed-byte corpus never causes uncontrolled process failure

---

# 27. Security corpus

## 27.1 Mutation

- [ ] SEC-01 mutate magic
- [ ] SEC-02 mutate version
- [ ] SEC-03 mutate algorithm enums
- [ ] SEC-04 mutate optional-string presence/length/value
- [ ] SEC-05 mutate reference flag
- [ ] SEC-06 mutate lengths
- [ ] SEC-07 mutate checksum
- [ ] SEC-08 mutate encrypted/compressed data

## 27.2 Truncation

- [ ] SEC-09 every valid V1 prefix through complete length has deterministic outcome
- [ ] SEC-10 no truncated structure allocates based only on a declared giant count
- [ ] SEC-11 V0 truncation maps to `BinaryFormatException`

## 27.3 Counts/lengths

- [ ] SEC-12 `-1`
- [ ] SEC-13 `0`
- [ ] SEC-14 `1`
- [ ] SEC-15 `Max-1`
- [ ] SEC-16 `Max`
- [ ] SEC-17 `Max+1`
- [ ] SEC-18 `int.MaxValue`
- [ ] SEC-19 `int.MinValue` where representable

## 27.4 Varints / text

- [ ] SEC-20 valid shortest varint
- [ ] SEC-21 multi-byte varint
- [ ] SEC-22 truncated varint
- [ ] SEC-23 excessive continuation
- [ ] SEC-24 overflowed varint
- [ ] SEC-25 malformed UTF-8 behavior matches the chosen encoding contract

## 27.5 Allocation amplification

- [ ] SEC-26 array declared at limit but truncated before all elements
- [ ] SEC-27 header declared phase length above limit is rejected before phase allocation
- [ ] SEC-28 encrypted expected plaintext above current limit is rejected before allocation
- [ ] SEC-29 nested individually-valid containers do not bypass the current element budget
- [ ] SEC-30 unknown keyed field is skipped incrementally
- [ ] SEC-31 `ReadExactly` does not retain partial output after failure

---

# 28. Required internal security invariants

- [ ] INT-01 fresh `SerializationBudget` per operation
- [ ] INT-02 total element count never decreases
- [ ] INT-03 object node count never decreases
- [ ] INT-04 depth returns to prior value after success
- [ ] INT-05 depth returns to prior value after exception
- [ ] INT-06 failed `EnterDepth` leaves depth unchanged
- [ ] INT-07 each validation consumes the element budget exactly once
- [ ] INT-08 zero dimensions do not overflow total-product calculation
- [ ] INT-09 allocation driven by attacker count happens only after validation
- [ ] INT-10 pooled crypto buffers are cleared when required
- [ ] INT-11 caller-owned key buffers remain caller-owned
- [ ] INT-12 invalid reference markers are rejected
- [ ] INT-13 unknown keyed payload is not deserialized through an unavailable formatter
- [ ] INT-14 current budgeted write accounting behavior is covered
- [ ] INT-15 keyed field rewrites do not double-charge bytes
- [ ] INT-16 bounded field reads cannot cross the field boundary

---

# 29. Required test utilities

Implement centrally; do not duplicate ad-hoc mutation/stream logic.

```text
AssertEx.EqualSequenceAndType<T>()
AssertEx.EqualDictionaryContents<TKey,TValue>()
AssertEx.EqualStackBehavior<T>()
AssertEx.EqualQueueBehavior<T>()
AssertEx.EqualPriorityQueueBehavior<TElement,TPriority>()
AssertEx.AssertSameReferences()
AssertEx.ThrowsExact<TException>()
AssertEx.ThrowsWithSubstring<TException>()

BinaryTestData.CreateV1Payload(...)
BinaryTestData.CreateV1Header(...)
BinaryTestData.CreateV0Payload(...)
PayloadMutation.ReplaceInt32(...)
PayloadMutation.ReplaceByte(...)
PayloadMutation.Truncate(...)
PayloadMutation.FlipByte(...)
PayloadMutation.ReplaceLengthWith(...)
PayloadMutation.Replace7BitInteger(...)

NonSeekableStream
PartialReadStream
FailingReadStream
ReadBudgetProbeStream
WriteBudgetProbeStream
BoundedPayloadStream
```

Utilities must have their own tests.

- [ ] UTIL-01 assertion helpers
- [ ] UTIL-02 byte mutation helpers
- [ ] UTIL-03 truncation helper
- [ ] UTIL-04 non-seekable stream
- [ ] UTIL-05 partial-read stream
- [ ] UTIL-06 failing stream
- [ ] UTIL-07 read budget probe
- [ ] UTIL-08 write budget probe
- [ ] UTIL-09 bounded stream helper

---

# 30. Cache implementation behavior

Required caches include:

```text
ActivatorCache
DictionaryAccessorCache
FrozenFactoryCache
ImmutableFactoryCache
LazyAccessorCache
MethodInvokerCache
PolymorphicTypeCache
ReadOnlySequenceAccessorCache
TupleAccessorCache
TypeAccessorCache
```

For each applicable cache:

- [ ] CACHE-01 first build returns usable accessor/delegate
- [ ] CACHE-02 subsequent use is functionally equivalent
- [ ] CACHE-03 concurrent first-touch is safe
- [ ] CACHE-04 invalid type/method fails deterministically
- [ ] CACHE-05 cache creation does not depend on request-local budget state
- [ ] CACHE-06 ImmutableFactoryCache resolves `AsArray` correctly for immutable array access
- [ ] CACHE-07 array and struct factory lookup paths are not conflated

---

# 31. External-consumer API tests

From a separate consumer assembly:

- [ ] EXT-01 public types accessible
- [ ] EXT-02 `BinarySerializerOptions.Default`
- [ ] EXT-03 `Configure`
- [ ] EXT-04 builder methods type-safe
- [ ] EXT-05 public algorithms constructible
- [ ] EXT-06 public exceptions catchable
- [ ] EXT-07 stream extensions discoverable
- [ ] EXT-08 no normal usage requires internal implementation types

---

# 32. Documentation/sample compile tests

- [ ] DOC-01 default serializer sample
- [ ] DOC-02 builder/options sample
- [ ] DOC-03 compression sample
- [ ] DOC-04 checksum sample
- [ ] DOC-05 AES-GCM sample
- [ ] DOC-06 key-resolver sample
- [ ] DOC-07 preserve-reference sample
- [ ] DOC-08 custom registry sample
- [ ] DOC-09 V0 fallback sample
- [ ] DOC-10 stream extension sample

---

# 33. Historical regression lessons from previous failures

These tests are release-relevant because they guard against regressions already encountered during the refactor.

- [ ] REG-01 no `_budget`/`_limits` accidental symbol drift after moving security logic
- [ ] REG-02 no direct formatter dependency on a second generic guard with overlapping methods
- [ ] REG-03 no redundant `catch (BinarySerializerException) { throw; }`
- [ ] REG-04 truncation maps to exact Viper format exception
- [ ] REG-05 negative wire count/length remains `BinaryFormatException`
- [ ] REG-06 limit overflow is `BinaryLimitException`
- [ ] REG-07 unknown keyed fields do not silently consume `MaxTotalElements`
- [ ] REG-08 `ImmutableArray<T>` uses `ImmutableCollectionsMarshal.AsArray`
- [ ] REG-09 immutable array accessor lookup never regresses to unavailable instance `ToArray`
- [ ] REG-10 AES key ID is taken from the message header during decrypt
- [ ] REG-11 invalid encryption key selection is not mislabeled as payload corruption
- [ ] REG-12 V0 remains minimal/positional and does not accidentally inherit V1-only metadata requirements
- [ ] REG-13 inspect restores stream position
- [ ] REG-14 caller streams remain open
- [ ] REG-15 no broad `IOException` catch surrounds the whole `BinarySerializer` router/codec operation
- [ ] REG-16 exact exception types are asserted instead of accepting OR-lists
- [ ] REG-17 tests distinguish configuration-zero from wire-zero
- [ ] REG-18 test cases are independent; no global registry state leakage
- [ ] REG-19 compatibility fixtures are fixed bytes, not regenerated expected output

---

# 34. Deferred hostile-audit corpus — explicitly non-gating

The 19-observation hostile audit remains part of the engineering knowledge base. These cases are **not current release gates**. They become mandatory only after the corresponding redesign is implemented.

- [ ] FUT-01 S02 — authenticate V1 header metadata
- [ ] FUT-02 S03 — apply depth consistently across recursive container shapes
- [ ] FUT-03 S04 — count container instances in graph-node accounting
- [ ] FUT-04 S05 — enforce V0 payload read boundary
- [ ] FUT-05 S06 — reject oversized phase allocation before allocation
- [ ] FUT-06 S07 — preserve key-resolver buffer ownership
- [ ] FUT-07 S08 — make Encryptor disposal/lifetime semantics strict
- [ ] FUT-08 S09 — enforce exact decompression output
- [ ] FUT-09 S10 — enforce typed payload consumption boundary
- [ ] FUT-10 S11 — bound lazy IEnumerable materialization
- [ ] FUT-11 C01 — restore value-type `ref` results
- [ ] FUT-12 C02 — preserve collection identity when reference preservation promises it
- [ ] FUT-13 C03 — define reference behavior for skipped unknown keyed fields
- [ ] FUT-14 C04 — reject unsafe implicit positional polymorphism
- [ ] FUT-15 C05 — reject contradictory `[BinaryKey] + [BinaryIgnore]`
- [ ] FUT-16 C06 — make write budget operation-relative at non-zero offsets
- [ ] FUT-17 C07 — normalize short primitive input to documented format exceptions

Semantic audit observations that are not failures under the current contract:

```text
S01 Encryptor capability does not imply RequireEncryption.
S12 keyed-field metadata is not MaxTotalElements.
```

- [ ] FUT-18 every deferred case has a target redesign before promotion to the release gate
- [ ] FUT-19 no test in the active gate treats a deferred case as already fixed
- [ ] FUT-20 each future limit introduced by deferred hardening gets its own name and boundary semantics

---

# 35. Release checklist

## Correctness

- [ ] Every registered formatter family has mapped coverage.
- [ ] Primitive, time, numeric, system, collection, immutable, frozen, memory, tuple and nested types are covered.
- [ ] Nullability is covered.
- [ ] Byte[] and stream entry points are covered.
- [ ] Existing-instance and `ref struct` entry points are covered.
- [ ] Cross-entry-point equivalence passes.

## Contracts

- [ ] `[BinaryIgnore]` is covered.
- [ ] `[BinaryInclude]` is covered.
- [ ] `[BinaryOrder]` and duplicate order are covered.
- [ ] `[BinaryContract]` is covered.
- [ ] `[BinaryKey]` and varint boundaries are covered.
- [ ] contradictory key/ignore declarations are rejected.
- [ ] unknown keyed fields are covered.
- [ ] schema evolution fixtures are fixed and reviewed.
- [ ] polymorphism is covered.

## Security

- [ ] All limit boundaries are covered.
- [ ] Cumulative budgets are covered.
- [ ] Node/depth budgets are covered.
- [ ] header lengths are checked before phase allocation.
- [ ] arrays use safe incremental materialization.
- [ ] unknown field skipping is bounded.
- [ ] malformed/truncated corpus passes.
- [ ] reference marker/ID cases pass.
- [ ] stream wrappers pass.
- [ ] key ownership/clearing tests pass.
- [ ] no process-fatal recursion test is required.

## Exceptions

- [ ] exact taxonomy tests pass.
- [ ] no forbidden exception OR-list assertions remain.
- [ ] no raw parser EOF leaks on declared truncation.
- [ ] inner exceptions are preserved where required.

## Concurrency/caches

- [ ] shared serializer concurrency passes.
- [ ] cache first-touch passes.
- [ ] registry tests are isolated.
- [ ] per-operation budgets are isolated.

## Benchmark readiness

- [ ] Benchmark project uses the approved competitor set.
- [ ] unsupported scenarios are explicit.
- [ ] baseline environment manifest is captured.
- [ ] raw and charted benchmark reports are generated.

---

# 36. Definition of QA completion

QA is complete only when every applicable `[ ]` item is checked, every test ID has an implementation reference, and no test relies on undocumented project history.

A green round-trip alone is not a release signal.
