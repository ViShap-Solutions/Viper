# ViShap.Viper — Native AOT through the context: design brief (rc.2 P4c)

**Class: plan.** A design for the owner to approve before any code. It defines no behavior: P4d builds what
is approved here and changes the contract in the same change.

**Status:** written 2026-10-14; **approved by the owner on 2026-10-14** — every decision (D1…D12) as
recommended, the façade named `SupportedTypes` (D1); recorded in `RC2-Plan.md` §3.9. Each decision keeps
its alternatives with what they give and cost, so P4d can be read against what was weighed.
**Written against:** `release/v1.0.0` at `ee3fefe` (P4b merged), 2 091 + 42 tests green in Debug and Release.
**Governs:** option a of `ideas/IDEA-04` — a façade of names, typed handles, guarded reflection.

---

# 1. What blocks native AOT today

Every member-encoded value already goes through a generated contract when a context holds it. What still
needs reflection is the engine around the contracts:

| # | Where | What it does at run time |
|---|---|---|
| B1 | `Engine/FormatterRegistry.cs` `Create`, `ForArray`, `CodecFor` | closes every codec and shape with `MakeGenericType`, creates it with `Activator.CreateInstance`, finds a shape's interface with `GetInterfaces` |
| B2 | `Engine/Codec.cs` `FormatterCache<T>` | its static initializer calls B1; the only way any codec — root or child — is found (`Graph`, `SequenceCodec`, `MapCodec`, `CompositeCodec`, `Elements`, `MemberWriter`, `MemberReader` all read `FormatterCache<X>.Instance`) |
| B3 | `Engine/Contracts/ReflectedContract.cs` | `Expression.Compile`, `MakeGenericType` — the fallback of `ObjectCodec.Contract` and of `ObjectCodec.RuntimeContract` when the context lacks a type |
| B4 | `Engine/Contracts/TypeContractCache.cs` `GetUnion` | `[BinaryUnion]` read from attributes (not annotated; the analyser reports nothing) |
| B5 | `Formatters/Sequences/SequenceShapes.cs` `CustomCollectionShape` | `Expression.Compile` of `new TCollection()` and of the public `Add(T)` found by `GetMethod` — **not listed in IDEA-04**, found while writing this brief |

The annotations run from the 22 generic methods of `BinarySerializer` and the 4 of `BinaryFormatDumper`
down through the pipelines (`IFormatPipeline`, `V0FormatPipeline`, `V1FormatPipeline`, `FrameReader`) to
`Graph.WriteRoot`/`ReadRoot`; `FormatterCache<T>` and `TypeContractCache.Get` suppress IL2026/IL3050 on
the strength of that chain.

The central fact the design rests on: **a codec finds its children through the static
`FormatterCache<X>`**, not through a reference it holds. Whatever supplies codecs without reflection must
therefore supply them *there*, or every codec must be rewritten to hold its children (D4).

# 2. The design in one picture

```text
generated context constructor                         engine, unchanged in steady state
──────────────────────────────                        ───────────────────────────────────────────
Add(new Shop_OrderContract());          ─ contract ─▶ ContractSet (per options, as today)
Add(SupportedTypes.Object<Order>());        ─ offers  ─▶  OfferedCodec<Order>.Offered     (write-once)
Add(SupportedTypes.List<Line>());           ─ offers  ─▶  OfferedCodec<List<Line>>.Offered
Add(SupportedTypes.Enum<Status>());         ─ offers  ─▶  OfferedCodec<Status>.Offered
Order = TypeInfo<Order>();              ─ handle  ─▶  BinaryTypeInfo<Order> (public, opaque)

FormatterCache<T>.Instance = OfferedCodec<T>.Offered            ← a context offered it
                          ?? BuiltInCodecs.Resolve<T>()       ← the closed scalar set, statically
                          ?? (ReflectionPath.IsEnabled        ← feature switch, a guard the analysers understand
                                ? FormatterRegistry.Resolve<T>()
                                : UnavailableCodec<T>)        ← BinaryConfigurationException at use

serializer.Serialize(order, AppContracts.Default.Order)       ← no annotation: everything it reaches is
                                                                either offered or behind the guard
```

Nothing of the traversal protocol becomes public: a `SupportedType` is an opaque token; the codecs, shapes,
counts and loops stay internal (INV-5). The wire does not change: an offered codec is the codec
`FormatterRegistry` builds for the same type (D12).

---

# 3. Decisions

## D1 — Where the façade of names lives, and what it is called

One statically typed factory per row of the §23 table: each generic definition of `TypeShapes`/`Shapes`
(63), plus `Array<T>` (`T[]`), `MultiDimensionalArray<TArray, TElement>`, `Nullable<T>`, `Enum<T>`,
`Object<T>`, `Union<T>` and `Collection<TCollection, T>` — about 70 members. Generic arity overloads carry
the tuples (`Tuple<T1>` … `Tuple<T1, …, T7, TRest>`).

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | A public static class `ViShap.Viper.Contracts.SupportedTypes`, returning an opaque `SupportedType`; a context registers it through a protected `Add(SupportedType)` | Discoverable beside `TypeContract<T>` in the contract namespace; testable on its own; the context's surface grows by one member; a hand-written context uses it exactly as generated code does | One public static class with ~70 members, one opaque type; method names equal BCL type names (`SupportedTypes.List<T>()`), so the class's own code writes `global::System.Collections.Generic.List<T>` |
| b | ~70 `protected static` members on `BinarySerializerContext` | Only a context can name them; no new type | The context's surface becomes the whole table; every derived context and its IntelliSense carry it; harder to test apart from a context |
| c | One generic entry `Add<T>(SupportedTypeKind kind)` | Few members | Cannot be built without `MakeGenericType` — the problem itself. Rejected |

Name: **`SupportedTypes` / `SupportedType`**, the owner's choice of 2026-10-14. Each factory is one row of
the table of supported types (contract §23, `docs/supported-types.md`) closed over the consumer's types,
so the public name is the term the consumer already reads there. Considered and declined: `KnownTypes`
(a word the system does not use), `TypeEncodings` (pairs with `TypeContract<T>` but "encoding" is used
loosely and collides with `System.Text.Encoding`), `BinaryTypes` (easily confused with
`BinaryTypeInfo<T>`), `TypeCodecs` (names the internal concept the public surface otherwise never says).

```csharp
// generated
Add(global::ViShap.Viper.Contracts.SupportedTypes.Dictionary<string, global::Shop.Line>());
// hand-written, the same
Add(SupportedTypes.Dictionary<string, Line>());
```

**Recommendation: a, `SupportedTypes` / `SupportedType`.** The table is data about the format, not about the
context; keeping it out of the context's surface keeps `BinarySerializerContext` readable.

**`SupportedType` exposes:** `Type` (the declared type) and nothing else — no codec, no shape, no method. Each
factory validates that the rule `FormatterRegistry` applies to its type is the factory's own rule, so
`SupportedTypes.Collection<List<int>, int>()` (claimed by `List<>`) or `SupportedTypes.Object<int[]>()` is
`BinaryConfigurationException` (D12 explains how that check is shared with the registry).

## D2 — The closed scalar set

The 41 scalars of §23 are not generic, so no consumer type ever closes them.

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | The library resolves them itself, statically: `BuiltInCodecs.Resolve<T>()`, a chain of `typeof(T) == typeof(int)` tests the JIT and ILC fold away per instantiation | Nothing to emit or offer for `int`, `string`, `Guid`, …; the generated registration list is only what the consumer's types close | A second table of scalars beside `FormatterRegistry.Scalars`, held equal by the D12 test |
| b | A façade factory per scalar (`SupportedTypes.Int32()`), emitted for every scalar a context reaches | One mechanism for everything | ~41 more public members of no use to a reader; generated code grows by every scalar used |

**Recommendation: a.** Enums are not in the closed set (their codec is closed over the enum type) and get
`SupportedTypes.Enum<T>() where T : struct, Enum`.

## D3 — Where an offered codec lives: the static cache, or a table per context

The plan asks the brief to state why seeding the process-wide `FormatterCache<T>` is not a process-wide
mutable registry in the sense of INV-7, or to choose a per-context table.

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | **Seed the static cache.** A write-once generic static `OfferedCodec<T>.Offered`, set by `Add(SupportedType)` (first offer wins, `Interlocked.CompareExchange`); `FormatterCache<T>`'s initializer reads it first | No change to any codec; steady state identical (`FormatterCache<T>.Instance` stays `static readonly`, so the JIT keeps treating it as a constant and devirtualizing through it); no dictionary keyed by `Type` (CN-20 holds) | (i) A type whose `FormatterCache<T>` was initialized before any context offered it keeps what it got: with reflection off that is `UnavailableCodec<T>` for the life of the process. Reached only through an entry point without a handle, which carries the annotations and warns. (ii) Constructing a context has a process-wide effect — benign because the effect is the value the registry would have computed (below) |
| b | Seed, but `FormatterCache<T>.Instance` mutable, so a later offer replaces an `UnavailableCodec<T>` | Removes (a)(i) | The cache stops being `static readonly`: every child-codec call loses the JIT's constant folding and devirtualization — the per-member cost PERF-11 is about, on every path, reflected included |
| c | A per-context codec table, looked up through `OperationState` | Nothing process-wide | A dictionary lookup per element and per member on the hot path; two resolution mechanisms inside the engine |
| d | Codecs hold their children (a codec graph per context, built by the factories) | Nothing process-wide, no lookup | Every codec and composite rewritten to take children in its constructor; the reflected path needs the same graph builder; the largest change to the engine since the rework; INV-5's traversal owner duplicated per context |

**Why (a) is not a mutable registry in the sense of INV-7.** INV-7 forbids a registry through which code
outside an operation could change *what an algorithm is*. The codec cache never decides anything: the codec
of a declared type is a pure function of that type (§18, "What is cached"), whoever computes it. A context
offers the value `FormatterRegistry` would compute — proved per §23 definition and per corpus type (D12) —
and the first value written is never replaced. No offer can make a type encode differently; an offer of a
different value is not expressible, because each factory builds the registry's codec for its own type and
refuses a type another rule claims. The contract's §18 gains that sentence; INV-7 is unchanged.

**Initialization order.** `FormatterCache<T>` has a `beforefieldinit` initializer; under the JIT it may run
when a method referencing it is compiled, before a context offered it. With reflection on that is harmless
(the registry builds the same codec). With reflection off it is the case (a)(i), and under native AOT the
initializer runs lazily at first access. P4d checks it first (§6, step 1); if the JIT case ever matters, an
explicit static constructor removes `beforefieldinit` at no steady-state cost (tier-1 code is compiled after
initialization).

**Recommendation: a.**

## D4 — Union maps

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | Emitted: `SupportedTypes.Union<Shape>(SupportedTypes.Arm<Circle>(1), SupportedTypes.Arm<Square>(2))` offers the union map with the object codec; `TypeContractCache.GetUnion` is consulted only when no context offered one | Nothing read from attributes at run time, as P4c asks; the tags the generator already validates (VPR015–VPR017) are the tags that travel | Two more factory members; a second producer of a union map, held equal to `BuildUnion` by the D12 test |
| b | Keep reading `[BinaryUnion]` at run time | No emission; the analyser reports nothing for `GetCustomAttributes` | Correctness under native AOT rests on attribute metadata being kept for every union base — true today for a type whose `Type` the program obtains, but a property of the toolchain, not of Viper; the AOT run would be the only proof |

**Recommendation: a.**

**Amended by the owner on 2026-10-26** (`RC2-Plan.md` §3.9, RC2-01 of `Audit-Conformance.md`): an offer of
arms as given let a context that was only created change the tags of every serializer of the process,
against D3's "an offer of a different value is not expressible". `SupportedTypes.Union<T>` keeps its
arms, compares them as a set with the ones `[BinaryUnion]` declares on `T` — read at run time, as D4-b
would — and refuses `T` without them; the map it offers is the one the reflection path reads. Under native
AOT a lost attribute fails closed: the offer is refused, never made with other tags.

## D5 — Custom collections (B5)

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | The factory takes the two operations the shape compiles today: `SupportedTypes.Collection<TCollection, T>(Func<TCollection> create, Action<TCollection, T>? add)`; the generator emits `static () => new X()` and `static (c, e) => c.Add(e)` — the public instance `Add(T)` the shape finds by `GetMethod`, or `null` when there is none (the shape keeps raising its `BinaryTypeException` at use) | No `Expression`, no `GetMethod` on the generated path; the reflected path keeps its own shape | The generator must find `Add(T)` by the same rule as `GetMethod(… Public | Instance, [typeof(T)])`; a snapshot and a byte-identity case per collection |
| b | Keep the reflective shape; under native AOT `Expression.Compile` falls back to the interpreter | Nothing to emit | `RequiresDynamicCode` stays reachable; the "no warning" gate fails. Rejected |

**Recommendation: a.**

**Amended by the owner on 2026-10-26** (`RC2-Plan.md` §3.9, RC2-01 of `Audit-Conformance.md`): operations
given as delegates let an offer read a collection differently from the reflection path. The factory is
`SupportedTypes.Collection<TCollection, T>()` with `TCollection : new()` and takes nothing; the one shape,
for both paths, creates the collection with `new()` and fills it through the public instance `Add(T)` it
finds with `GetMethod(… Public | Instance, [typeof(T)])` on a type annotated for its public methods — a
delegate created once, or an invocation of the method for a value-type collection. A custom collection is no longer compiled with `Expression` on either path, and the generator no longer
looks for `Add`.

## D6 — The guard

Every reflective resolution — B1 (through B2), B3 and, under D4-b, B4 — sits behind one feature switch the
trimming and AOT analysers understand, so that code behind it raises no warning and the toolchain removes it
when the switch is off:

```csharp
internal static class ReflectionPath
{
    [FeatureSwitchDefinition("ViShap.Viper.ReflectionPath.IsEnabled")]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    public static bool IsEnabled { get; } =
        !AppContext.TryGetSwitch("ViShap.Viper.ReflectionPath.IsEnabled", out bool enabled) || enabled;
}
```

With the switch off, a type no context offered — a declared type, a member type, a union arm, the runtime
type of a polymorphic slot — is `BinaryConfigurationException` naming the type, the switch and the fix
("add it to a `[BinaryContext]`"), raised where the type is first used, never reflection. The message is
the operation's, so the operation leaves nothing in its destination (INV-15) and nothing leaves under a
framework name (INV-10).

The package sets the switch through a `buildTransitive` targets file
(`<RuntimeHostConfigurationOption Include="ViShap.Viper.ReflectionPath.IsEnabled" Value="false" Trim="true" />`)
under an MSBuild property `ViperReflectionEnabled`. When it is off by default:

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | Off when `PublishAot` **or** `PublishTrimmed` is true; `ViperReflectionEnabled=true` turns it back on | The trimmed JIT application gets the same guarantee as native AOT — no member silently trimmed away under reflection; the same rule `System.Text.Json` follows | A trimmed application that serializes without a context today fails at run time with a configuration error until it adds a context or sets the property — a behavior change for that consumer (none is released: before `v1.0.0`, `Development-Workflow.md` §5.6) |
| b | Off only when `PublishAot` is true | No change for trimmed JIT applications | A trimmed application keeps the warnings and the risk; the guard gives it nothing |
| c | Never off by default; opt-in through the property | No behavior change anywhere | A native AOT application that forgets the property keeps the reflective path reachable, so ILC keeps the warnings and the code; "no warning under native AOT" holds only for those who know to ask |

**Recommendation: a.** It also lets the internal annotation chain go (D10).

`RuntimeFeature.IsDynamicCodeSupported` alone was considered and is not enough: the analysers accept it as
a guard for `RequiresDynamicCode` only, so every trimming warning would remain.

## D7 — The typed handle

A handle states, in the signature, that the type's codec and contract come from a context, so the entry
point that takes it reaches no reflection and needs no annotation.

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | `public sealed class BinaryTypeInfo<T>` in `ViShap.Viper`, created only by a context (`protected BinaryTypeInfo<T> TypeInfo<T>()`, which refuses a type the context did not offer); exposes `Type` and `Context` | The manner of `JsonTypeInfo<T>` the plan names; one generic type | One public type, one protected member |
| b | Overloads that take the context itself: `Serialize(value, AppContracts.Default)` | No new type | `T` is no longer tied to something the context proved it holds; the overload still reaches a resolution that can fail for `T` — the signature promises nothing |

Names for (a): `BinaryTypeInfo<T>` (parallel to `System.Text.Json`, recognisable), `BinaryTypeHandle<T>`,
`SupportedType<T>` (would pair with D1 but blur the opaque registration and the handle).

**The handle and the serializer's options.** The operation needs the context's contracts in its
`ContractSet`, which belongs to the options:

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | The options must have been built `WithContracts(handle.Context)`; otherwise `BinaryConfigurationException` at the call, naming both contexts | Nothing implicit: one place says which contracts an operation uses; a reference comparison per call | The consumer writes the context twice (`WithContracts(AppContracts.Default)` and `AppContracts.Default.Order`) |
| b | Options without a context adopt the handle's; options with another context are refused | Shorter for the common case | Two serializers built from the same options can use different contracts depending on the call — the options no longer say it alone |
| c | The handle's context always wins | Shortest | The options' `WithContracts` silently ignored on those calls |

**Recommendation: a, `BinaryTypeInfo<T>`, the options must name the handle's context.**

The generated context gets one property per root it lists in `[BinaryContext(typeof(…))]` — not per
reachable type — named after the type's simple name (`Order`); a generic root gets the type-name identifier
the generator already builds for hint names (`List_Order`); a name that collides with another root or a
member of the context is a new error diagnostic, VPR020, naming both. A reachable type that is not a root
has no property; `TryGetTypeInfo<T>()` on the context returns its handle when the context offered `T`.

## D8 — Which entry points take a handle

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | Every one: the 22 generic methods of `BinarySerializer` (sync, async, stream, pipe, `Populate`, `PopulateAsync`, `DeserializeAsyncEnumerable`) and the 4 of `BinaryFormatDumper`, each with a `BinaryTypeInfo<T>` parameter after the value or source | Native AOT has every capability the reflective surface has; the docs say "the same call, with the handle" | 26 overloads, their XML documentation, `PublicSurfaceTests`/`MemberSurfaceTests` rows, tests (each delegates to the same internal path, so behavior tests are shared) |
| b | A core set: `Serialize` to `byte[]`, `IBufferWriter<byte>`, `Stream`; `Deserialize` from span, sequence, stream; their async forms | About 10 overloads | A consumer on native AOT loses pooled payloads, pipes, `Populate`, the async enumerable and the dumper — a split surface to document and explain |

**Recommendation: a.** The existing 26 keep `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` (S4-A's
annotations stay on the entry points without a handle, §3.9): with the switch on they still reach
reflection.

## D9 — What the generator emits, and the types it cannot describe

The walk already visits every type a context reaches (`Planner.Visit`, classified by `TypeShapes`, whose
tables equal the registry's). P4d makes it record one registration per visited non-scalar type — container,
array, nullable, enum, object, union — and emit them in the context's constructor in a deterministic order
(ordinal by the type's display name), after the contracts and before the handles. `GEN-09` stays true: a
registration names a type; it carries no length, count, loop, tag or byte.

A type the generator cannot describe — declared in another assembly, inaccessible, a static or open generic
type — is VPR019 today ("described by reflection at run time"). With reflection off it has no description at
run time at all.

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | The package makes `ViperReflectionEnabled` and `PublishAot`/`PublishTrimmed` visible to the generator (`CompilerVisibleProperty`); when reflection will be off, the same finding is a new **error**, VPR021, "unavailable under native AOT"; otherwise VPR019 as today | The build fails where the run would; the consumer is told to add a context in the type's own assembly | Diagnostics that depend on build properties; snapshots for both modes |
| b | VPR019 stays a warning; the run raises `BinaryConfigurationException` | No new diagnostic | The failure moves from build time to run time |
| c | Describe a type from another assembly from its metadata | Fewer cases left out | A reference assembly omits non-public members, so the member set can differ from the one reflection finds at run time — a byte divergence by construction. Rejected |

**Recommendation: a.**

## D10 — The annotation chain inside the library

With every reflective resolution behind `ReflectionPath.IsEnabled`, the analyser no longer needs the chain
of `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` through the pipelines and `Graph`, and the two
suppressions (`FormatterCache<T>`, `TypeContractCache.Get`) lose their reason.

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | Remove the chain below the public entry points and both suppressions; annotate only `FormatterRegistry`, `ReflectedContract` and the reflective shape, all called behind the guard; the 26 public entry points keep theirs by decision | No suppression left in the library; the analyser checks the guard instead of trusting a comment; one internal path serves handle and non-handle calls | `CLAUDE.md` "The reflection path states its requirements" and contract §2/§3 rewritten; `AotAnalysisTests` reports the same public result |
| b | Keep the chain; give the handle path its own unannotated copy of the pipeline entry | No change to the existing chain | Two copies of every pipeline entry; the suppressions stay |

**Recommendation: a.**

## D11 — Proof

1. **The second resolution path equals the registry.** A test over every §23 definition — closed over a
   value type, `string` and a member-encoded type — and over every type of the corpus: the codec a
   `SupportedType` offers and the codec `FormatterRegistry.Resolve` builds have the same runtime type, hold a
   shape of the same runtime type, and write the same bytes for the test's values. The same test pins D2's
   scalar table and D4's union maps against `BuildUnion`. Rule ownership is shared, not copied: P4d splits
   `FormatterRegistry.Create` into a classification over `Type` (no construction, analyser-clean) and the
   reflective construction; each `SupportedTypes` factory checks its type against that classification.
2. **The generator's registrations equal what the engine resolves.** For the generated contexts of the test
   project: the set of offered types equals the set of types whose `FormatterCache<T>` the corpus
   round trips initialize under reflection, minus the scalars.
3. **The frozen fixtures and `CONF-*` through the context path** — both conformance runs with reflection
   switched off in a child process (`runtimeconfig` with the switch), so a type the context fails to offer is
   a failure, not a silent fallback.
4. **The AOT consumer published and run.** `tests/ViShap.Viper.AotConsumer` gains a generated context over
   the frozen compatibility shapes and the conformance shapes (linked source files from the test project's
   `Fixtures/`), handle calls for every entry point of D8, and a run mode that writes every case's bytes as
   hex lines and reads the frozen `Fixtures/Wire/*.bin` copied beside it. Its existing annotated lines stay
   for the analysis and are not run. Proof: the native executable's output equals the JIT run's output line
   for line, and the publish reports exactly the warnings `Api/AotAnalysisTests` expects (the annotated
   lines, nothing from a handle line, nothing from the packages).
5. **`Api/AotAnalysisTests`** extended: a handle line reports nothing; `ReflectionPath.IsEnabled` is the only
   feature switch; no `UnconditionalSuppressMessage` remains in `src/`.
6. **Structural.** `PublicSurfaceTests`/`MemberSurfaceTests` list the new surface; a new test asserts that no
   public or protected member of the packages has a parameter, return or property type that is internal to
   the engine (`Codec<>`, a shape interface, `ElementCount`, `WireReader`/`WireWriter`), and that `SupportedType`
   and `BinaryTypeInfo<T>` expose only `Type` and `Context`.

## D12 — The CI job

| | Option | Gives | Costs |
|---|---|---|---|
| **a** | A second job in `ci.yml`, `native-aot`, on `ubuntu-latest`: install `clang` and `zlib1g-dev` if absent, `dotnet publish tests/ViShap.Viper.AotConsumer -c Release -r linux-x64`, run the executable and `dotnet run` of the same project, `diff` the outputs | Runs on every PR and push like the rest; Linux toolchain is light; a few minutes | One job; the runner's native toolchain becomes a dependency of CI |
| b | The same on `ubuntu-latest` and `windows-latest` | Both toolchains proven | Twice the minutes; the Windows job needs the C++ build tools of the image |
| c | A test in the test project that publishes and runs locally, skipped when no toolchain | Locally visible | A test that skips itself is a gate that can silently not run; the publish takes minutes inside the suite. Rejected |

**Recommendation: a**, with the local command in `docs/aot.md` and `CLAUDE.md`. The job is added in P4d's
branch; it is the one change outside `src/`, `tests/`, `docs/`, `internal/` and `CLAUDE.md` that P4d makes.

---

# 4. Invariants

- INV-2, INV-4, INV-5 unchanged in force: the façade builds the engine's own codecs and exposes none of
  them; the structural test of D11.6 is added to INV-5's evidence.
- INV-7 unchanged; §18 says why an offered codec is not a registry (D3).
- INV-10, INV-15: the unavailable-type error is the engine's `BinaryConfigurationException`, raised before
  the destination is touched.
- **Proposed INV-19:** with the reflection path switched off, no codec, contract or union map is built by
  reflection; a type no context offered is `BinaryConfigurationException` where it is first used. Evidence:
  D11.3, D11.4.
- **Proposed INV-20:** a codec or union map offered through `SupportedTypes` is the one `FormatterRegistry` and
  `TypeContractCache` build for the same type. Evidence: D11.1, D11.2.

# 5. What P4d changes in the documents

- Contract §2 (the requirement chain), §3 (`SupportedTypes`, `SupportedType`, `BinaryTypeInfo<T>`, the context's new
  protected members, the 26 overloads), §14.3 (offers and handles), §14.4 (what the generator emits, VPR020,
  VPR021), §18 (the offered codec, D3's sentence), §21.3 (the deferred line "a generated codec graph … native
  AOT without warnings is not claimed" replaced by what is now claimed and how it is proved), §23 (each row
  names its `SupportedTypes` factory), §25 (INV-19, INV-20).
- QA plan: the checkpoints of D11, a new AOT section or rows in EXT-07's.
- `docs/aot.md` rewritten around the handle path and the switch; `docs/generator.md`; the generator README
  (VPR020, VPR021); the Serialization README line.
- `CLAUDE.md`: "The reflection path states its requirements", "The source generator", "Projects" (the
  `buildTransitive` targets), the CI line under "Commands".
- `RC2-Plan.md` §7 ledger: none expected — nothing public is renamed or removed.

# 6. How P4d is cut into steps that each end green

1. **Spike:** `ReflectionPath.IsEnabled` with `FeatureSwitchDefinition`/`FeatureGuard`; confirm the
   analysers accept it for both attributes on `net10.0`, and the initialization order of D3 under JIT and
   under ILC. A failure here returns to the owner before anything else is built.
2. Split `FormatterRegistry` into classification and construction; `BuiltInCodecs`; guard in
   `FormatterCache<T>`, `ObjectCodec`, `TypeContractCache`; `UnavailableCodec<T>`. Annotation chain (D10).
   Suite green with the switch on (nothing observable changes).
3. `SupportedTypes`, `SupportedType`, `OfferedCodec<T>`, `Add(SupportedType)`; D11.1 test.
4. `BinaryTypeInfo<T>`, `TypeInfo<T>()`, `TryGetTypeInfo<T>()`, the 26 overloads; surface tests.
5. Generator: registrations, union and collection emission, handle properties, VPR020, VPR021; snapshots;
   D11.2.
6. Switch-off runs (D11.3); the AOT consumer and its run mode (D11.4); `AotAnalysisTests` (D11.5);
   structural test (D11.6); the CI job (D12).
7. Documents (§5); the P4a profile re-run against P4b — under the method R-06 asks for, if the owner has
   decided it; harness `--verify`, `--smoke`.

# 7. Cost and risk

- **Size.** About 70 façade members and their validation, 26 overloads, two opaque public types, a guard, the
  generator's registration emission and two diagnostics; tests as in D11. The largest stage of rc.2.
- **Risk 1 — the feature guard.** If the analysers on `net10.0` do not accept a custom guard for
  `RequiresUnreferencedCode`, D6 and D10 fall back to a suppression on the guard's call sites — which the
  project's rules forbid — so step 1 is a spike and a stop.
- **Risk 2 — initialization order (D3).** Measured in step 1.
- **Risk 3 — steady-state performance.** None expected: `FormatterCache<T>.Instance` stays `static readonly`
  and no codec changes. The profile re-run is the check; R-06 limits what a cross-run comparison can show.
- **Not changed:** the wire, every frozen fixture, the reflective path with the switch on, the 26 existing
  entry points and their annotations.

# 8. Version impact

None before `v1.0.0` (`Development-Workflow.md` §5.6). The same added after `v1.0.0` would be **minor**
(§5.3): new types, members and overloads. D6-a's default-off switch under `PublishTrimmed` would be a
behavior change for a trimmed application — which is why it is decided now, before the first stable
release, and not after.

# 9. Decisions to approve

| | Decision | Recommended |
|---|---|---|
| D1 | Façade: public static `ViShap.Viper.Contracts.SupportedTypes` returning opaque `SupportedType`, registered by protected `Add(SupportedType)` | a |
| D2 | Scalars resolved by the library statically, not offered | a |
| D3 | Offered codecs seed the static cache, write-once; INV-7 argued, not changed | a |
| D4 | Union maps emitted, not read from attributes | a |
| D5 | Custom collections take generated `create`/`add` delegates | a |
| D6 | Feature switch `ViShap.Viper.ReflectionPath.IsEnabled`, off under `PublishAot` or `PublishTrimmed`, MSBuild `ViperReflectionEnabled` overrides | a |
| D7 | `BinaryTypeInfo<T>`, created by the context; the options must name the handle's context; one property per listed root, VPR020 on a name collision | a / a |
| D8 | A handle overload for every one of the 26 generic entry points | a |
| D9 | VPR021, an error, when reflection will be off and a reached type cannot be described | a |
| D10 | Internal annotation chain and both suppressions removed; guard instead | a |
| D11 | The proof as listed, INV-19 and INV-20 added | — |
| D12 | One `native-aot` CI job on `ubuntu-latest` | a |

# 10. As built (P4d)

Where the build differs from the text above, and why. None of it changes a decision of §9.

- **D2.** There is one table of scalars, `BuiltInCodecs.Scalars`, a map from type to a statically typed
  factory; the reflection path consults it before `FormatterRegistry` and the registry holds none. The
  "second table held equal by a test" of D2 does not exist, so nothing has to hold two tables equal.
- **D3.** The spike of §6 step 1 confirmed both risks closed on `net10.0`: the analysers accept a
  `[FeatureGuard]` for `RequiresUnreferencedCode` and `RequiresDynamicCode` alike, and a type first used
  before its offer keeps the unavailable codec while a type offered first gets the offer, under the JIT
  and under ILC. `FormatterCache<T>.Instance` stays `static readonly`.
- **D4.** `Union<T>` takes its arms as `params (int Tag, Type Type)[]` — `Union<Shape>((1, typeof(Circle)),
  (2, typeof(Square)))` — instead of an `Arm<T>(tag)` factory, which would have needed one more public
  type for the arm; it validates the arms as §14.5 states. `Object<T>` also refuses a type that declares
  `[BinaryUnion]`, so a hand-written context cannot drop a union map by naming the wrong method.
- **D5.** The custom-collection shape takes its two operations in every configuration: the reflection
  path compiles them, a context writes them as code.
- **D7.** `TryGetTypeInfo<T>` answers for a scalar as well as for a type the context made supported.
- **D8.** The dumper's four forms take the options as a required argument after the type information:
  the options must name the type information's context, and an omitted `null` never can.
- **D10.** Besides the chain through the pipelines and both suppressions, the attributes on `Graph` and on
  the private helpers of `BinarySerializer` went too; the reflective builders keep theirs.
- **D11.4.** The marked calls moved out of `Program.cs` into `AnnotatedEntryPoints.cs`, which a publish
  leaves out (`_IsPublishing`), so the native AOT publish reports no trimming or AOT warning at all — the
  gate of P4d — while the build's analysis still sees every marked call. The consumer's run also covers
  D11.3: it reads and writes back every frozen payload through the generated context with the switch off,
  in the suite (`Api/AotAnalysisTests`) and natively in CI.
