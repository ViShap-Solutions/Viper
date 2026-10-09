# Ideas for later versions

**Class: plan.** A backlog of features considered and deliberately not built for `v1.0.0`. Nothing here
is decided or applied, and nothing here defines behavior: the contract does. An idea becomes work only
when the owner takes its decisions and moves it into a plan with a stage and a gate.

Each idea has one file, `IDEA-nn-<slug>.md`, in the same shape:

| Section | What it holds |
|---|---|
| **Status** | `idea` (not decided) · `decided — <plan and stage>` · `rejected — <reason>` · `superseded by IDEA-nn` |
| **What and why** | The problem a consumer has, and what the feature gives them |
| **Where it was written** | Every earlier document that names it, so the reasoning can be traced |
| **What is already fixed** | What the documents or the owner have settled |
| **Options** | Every reasonable option: what it gives, what it costs, a short example in code or bytes, and the recommendation with its reason |
| **Open questions** | What the owner must answer before a stage can start |
| **Version impact** | Which surface it touches (API, wire, behavior — `Development-Workflow.md` §5.1) and the version it costs under §5.2–5.5, for each option where they differ |
| **Preconditions** | What must exist first: a measurement, a dependency checked, another idea |

A version impact is stated for a release after `v1.0.0`, when §5 of `Development-Workflow.md` applies.
An idea whose cheapest form is a minor stays a minor only while it writes nothing new by default; one that
changes a default, a byte of an existing frame or a public signature is a major.

| Idea | Version impact | Status |
|---|---|---|
| [IDEA-01 Schema fingerprint](IDEA-01-schema-fingerprint.md) | minor (opt-in); major if on by default | idea |
| [IDEA-02 Live tracing](IDEA-02-live-tracing.md) | minor | idea |
| [IDEA-03 AES-GCM-SIV](IDEA-03-aes-gcm-siv.md) | minor | idea |
| [IDEA-04 Native AOT through the context](IDEA-04-native-aot.md) | minor | idea |
| [IDEA-05 Preserving unknown keyed fields](IDEA-05-unknown-keyed-fields.md) | minor (opt-in) | idea |
| [IDEA-06 Command-line tool and schema-driven reading](IDEA-06-cli.md) | minor, or a package of its own | idea |
| [IDEA-07 Public formatter contract](IDEA-07-public-formatters.md) | minor to add; every later change to it a major | idea |
| [IDEA-08 Per-field compression and a V2 envelope](IDEA-08-v2-envelope.md) | minor while V1 stays the default write; major to switch | idea |
| [IDEA-09 Constant-time checksum comparison](IDEA-09-constant-time-checksum.md) | patch | idea |
| [IDEA-10 Asynchronous engine](IDEA-10-async-engine.md) | minor | idea |
| [IDEA-11 Zstandard and LZ4](IDEA-11-zstandard-lz4.md) | minor | idea, partly decided — built-in ids 3 and 4 reserved; waits for an official implementation |
| [IDEA-12 Error correction (Reed–Solomon)](IDEA-12-reed-solomon.md) | minor (outside the format) | idea |

Performance proposals do not belong here: they live in `../performance/` as `PERF-nn`, each cited to the
measurement behind it.
