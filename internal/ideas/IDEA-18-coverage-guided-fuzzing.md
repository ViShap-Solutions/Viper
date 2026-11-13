# IDEA-18 — Coverage-guided fuzzing of the read path

**Class: plan.** **Status:** idea.

## What and why

Viper is built to read bytes it does not trust, and the test suite pins that by construction: the
hostile checkpoints (`Hostile/` — truncation, mutation of every header field, amplification, duplicate
entries, canonical scalars) and the property tests over a fixed seed (`Hostile/PropertyTests`, 16 to 64
trials each). Every one of those inputs was chosen by a person. A coverage-guided fuzzer chooses its own:
it mutates a corpus of valid frames, keeps every input that reaches a new branch, and runs millions of
them, so it finds the input nobody thought of — the path through two codecs, a keyed skip inside a union
arm inside a reference frame — that a hand-written test does not reach.

For a reader of the README it is also the strongest evidence of the claim the library makes: a stated
fuzzing job, its corpus and its findings in the repository.

## Where it was written

- An outside assessment of `v1.0.0`, reviewed by the owner on 2026-11-13, which named SharpFuzz and a
  continuous job in GitHub Actions.

## What is already fixed

- Every failure on read is a `BinarySerializerException` of the taxonomy of contract §8; nothing else
  may escape a public read. A fuzzer's crash condition is therefore exact: any other exception, a stack
  overflow, or a process killed for memory.
- Every wire field has exactly one encoding (INV-9). A frame the reader accepts therefore re-serializes
  to the same bytes, when the model knows every keyed field and no compression phase is present — a
  differential oracle no ordinary parser has.
- The dumper never throws for a malformed or hostile frame: it catches only `BinarySerializerException`
  (contract §19). It is a second target with the same crash condition.
- `Fixtures/Wire/*.bin` are frozen valid frames of every shape `Format/CompatibilityTests` reads: a
  seed corpus that already exists.
- A checksum or an authentication tag stops a mutated frame at once, so a fuzzer that mutates protected
  frames measures only the check. The interesting targets are unprotected V1 frames, compressed frames
  (the decompressor has no integrity check in front of it), and V0 payloads read with `AllowV0Fallback`.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Nothing beyond the hostile suite and the fixed-seed property tests | No new machinery | Only inputs a person chose |
| b | A mutation fuzzer without instrumentation in the test project: seeded random edits — flip, insert, delete, splice two fixtures, set a varint to a boundary — of the frozen fixtures, a fixed budget per CI run, with the oracles below | Runs on every PR, in the existing `dotnet test`; reproducible from the seed; a day of work | No coverage feedback, so it explores the shallow layers well and the deep ones by luck |
| c | SharpFuzz with libFuzzer: a project `tests/ViShap.Viper.Fuzz` with one target per entry point, the fixtures as seed corpus, a scheduled job in `.github/workflows/fuzz.yml` on Linux for a fixed time each night; the grown corpus committed, every crash turned into a pinned `Hostile/` test | Coverage-guided search; finds deep paths; the corpus grows with every run | SharpFuzz rewrites the IL of the assemblies under test, so its support of `net10.0` must be checked; a workflow to maintain; CI minutes |
| d | c, run continuously by an external service (OSS-Fuzz, or ClusterFuzzLite inside GitHub Actions) | Continuous fuzzing without owning the machine; findings filed privately | Whether either accepts a SharpFuzz target is to be checked; an external service sees the project's crashes before a release fixes them |

The oracles, shared by b, c and d:

```text
1. only BinarySerializerException escapes        Deserialize<T>, Populate, the stream and pipe readers,
                                                  DeserializeAsyncEnumerable, BinaryFormatDumper.Dump / Dump<T>
2. accepted ⇒ re-serialized bytes are identical  unprotected, uncompressed frames over models that know every key (INV-9)
3. allocation bounded                             GC.GetAllocatedBytesForCurrentThread over one read ≤ a bound
                                                  derived from the input length and SerializationLimits.Default
4. no stack overflow, no process death            libFuzzer reports both as crashes
```

```csharp
// One target of option c: a V1 frame read as the model of a frozen fixture.
public static void Target(ReadOnlySpan<byte> input)
{
    try
    {
        var value = Serializer.Deserialize(input, CompatibilityContext.Default.Order);
        if (!input.SequenceEqual(Serializer.Serialize(value, CompatibilityContext.Default.Order)))
        {
            throw new InvalidOperationException("an accepted frame has a second encoding");
        }
    }
    catch (BinarySerializerException)
    {
    }
}
```

Recommended when taken up: **b** first — it costs a day, runs on every PR and already exercises the
oracles — then **c** as a nightly job once SharpFuzz is confirmed on `net10.0`. **d** only after c has run
long enough that its findings are rare.

## Open questions

1. Where the fuzzing project lives: `tests/` beside the suites, or a `fuzz/` folder of its own outside
   the solution's CI build?
2. Is the grown corpus committed (reproducible, grows the repository) or kept as a CI cache?
3. What the README says, if anything: a sentence naming the job and linking to it, or a figure — and if
   a figure, which one stays true as the job keeps running?
4. Is a fuzzing finding a hotfix (`Development-Workflow.md` §8) when it is a non-taxonomy exception on a
   released version, as a hostile-input defect would be?

## Version impact

| Option | Surface | Version |
|---|---|---|
| a, b, c, d | tests and CI only | **none** — no package changes; a defect a fuzzer finds is released as its own fix |

## Preconditions

- SharpFuzz's support of `net10.0` and of the assemblies' `IsAotCompatible` build, checked before c.
- An allocation bound per entry point that the contract's limits imply, written down before oracle 3 can
  fail anything.
