# Security policy

Viper reads bytes it does not trust. Every limit, budget and check it applies on the way in exists so
that a payload — whatever its sender chose to put in it — can cost a reader no more than the bytes
and the configured limits allow, and can fail only with an exception of its own taxonomy.

## Supported versions

Security fixes are released for the latest minor version, as a patch release of it. A fix for a defect
found in an earlier minor is released on the latest one; upgrade to receive it.

## Reporting a vulnerability

Report a vulnerability privately, through
[GitHub private vulnerability reporting](https://github.com/ViShap-Solutions/Viper/security/advisories/new).
Do not open a public issue or pull request for it.

A report is most useful with:

- the package version and the target framework;
- the options the serializer was built with — limits, version, algorithms, references;
- the type read or written, and the payload that triggers the defect, as hexadecimal or Base64, or the
  code that produces it;
- what happened and what was expected.

Each report is acknowledged, confirmed or declined, and a disclosure date is agreed with the reporter.
A confirmed defect is fixed in a patch release, and the security advisory is published with it.

## What is a vulnerability

On any read — `Deserialize`, `Populate`, the stream, pipe and asynchronous readers, and the diagnostic
dumper — each of these is a vulnerability:

- an exception that is not a `BinarySerializerException` leaves the call;
- the process ends: a stack overflow, an access violation, an unbounded allocation;
- the work or the memory a read takes grows faster than the bytes it was given, within the configured
  limits;
- a configured limit is exceeded;
- a frame that was altered is accepted when the options require encryption or a checksum, or a
  protection the options require is downgraded;
- key material is exposed through a payload, an exception or a dump.

These are properties of the format rather than vulnerabilities:

- a checksum detects corruption and is not authentication — the checksums Viper ships have no key, so
  anyone can compute one; authentication is the encryption's tag;
- a V0 payload has no header and carries no checksum or encryption, so nothing about it can be
  verified before it is read;
- an unencrypted frame can be rewritten by whoever carries it.

## Fuzzing

The read path is fuzzed continuously. A coverage-guided job (`.github/workflows/fuzz.yml`) runs every
target of `tests/ViShap.Viper.Fuzz` against the corpus kept in that project, and every pull request
reads the corpus and a seeded set of mutated frames through every entry point, under the same rules
as the list above.
