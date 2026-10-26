# IDEA-02 — Live tracing

**Class: plan.** **Status:** idea. Deferred from `v1.0.0-rc.2` by the owner on 2026-10-09
(`rc2/RC2-Plan.md` §3.9, T1).

## What and why

Today a failed read is explained after the fact: the caller catches the exception and calls
`BinaryFormatDumper.Dump<T>` on the same bytes, which returns the tree up to the failing member with its
offset and path (`Order.Lines[2].Note`). Live tracing would hand every operation's `BinaryDump` to a sink
configured on the options, for logging or observability in production, on success and on failure, for
writes as well as reads.

## Where it was written

- `rework/Rework-Plan.md` §13 (owner's note, 2026-09-29).
- `performance/PERF-09` — the cost of the trace seam, measured switched off.
- `rc2/RC2-Plan.md` §3 T1, stage P5b (removed from the plan when deferred).

## What is already fixed

- The trace seam exists on the read side: `OperationState.Trace`, an internal observer, `null` for every
  serializer call, set only by the dumper, handed offsets, names, kinds and values — never a reader or a
  byte. The observer stays internal.
- Switched off it costs about 1.5 % on reads (PERF-09).

## Options

| | Option | Gives | Costs | Example |
|---|---|---|---|---|
| **a** | Do nothing; keep `catch` + `Dump<T>` | No cost for anyone; already documented | No trace of successful operations; no write-side trace | `catch (BinarySerializerException) { log(BinaryFormatDumper.Dump<Order>(bytes, options)); }` |
| b | `WithTrace(IBinaryTraceSink)`: the sink gets the dump of each operation | Production observability without re-reading | Trace seam in every write codec too; a cost on every write even when off, to be measured; new public API frozen by §5 | `.WithTrace(new LogSink(logger))` |
| c | b, sampled: `WithTrace(sink, sampleRate)` | Bounded overhead when on | The same seam cost when off; a sampling policy to specify | `.WithTrace(sink, 0.01)` |

Recommended now: **a**. If b or c is taken later, the answers recommended for its questions are: the sink
is called on failure with the partial dump (as `Dump<T>` is); it runs under the options' limits (as the
dumper does); it never sees key material (as the dumper never does); an exception from the sink is
`BinaryConfigurationException` and does not mask the operation's own exception.

## Open questions

1. Is a cost on every write, with tracing off, acceptable — and what ceiling (measured against the
   previous release) is the gate?
2. Sink shape: one interface, or a delegate?
3. Synchronous only, or may the sink be asynchronous? (Recommended: synchronous; the engine never awaits.)

## Version impact

| Surface | Version |
|---|---|
| API: a builder method and a sink type; behavior: none while off | **minor** (§5.3) |
| A measurable slowdown with tracing off | still minor, but it goes into the release note and needs a `PERF-nn` |

## Preconditions

- A write-side measurement of the switched-off seam, with `viper_bencher`'s method, against the release
  it would follow.
