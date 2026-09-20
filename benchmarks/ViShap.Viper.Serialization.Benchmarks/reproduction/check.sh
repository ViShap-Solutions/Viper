#!/usr/bin/env bash
#
# Proves that a baseline's reproduction instructions are complete, by carrying them out in a container
# that starts from the vendor .NET SDK image and holds nothing from the machine that took the baseline.
# A forgotten prerequisite fails here, because there is nothing in the container to satisfy it.
#
# What it checks, and what it deliberately does not:
#
#   checked      the documented sequence runs to completion from a clean clone
#   checked      every dataset round-trips under every profile
#   checked      the payload-size table is byte-identical to the one committed with the baseline
#   checked      every benchmark suite starts and completes one invocation
#   not checked  timings, allocation, throughput — those belong to the recorded machine and differ here
#
# Every step is judged by what it printed, not by its exit code: the benchmark switcher answers an
# argument it does not recognize by printing its help and exiting zero, so a mode that does not exist
# at the checked revision would otherwise pass as a mode that ran.
#
# Launch from the repository root.
#
#   PowerShell:
#     docker run --rm -v "${PWD}:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 `
#         bash /src/benchmarks/ViShap.Viper.Serialization.Benchmarks/reproduction/check.sh <ref> [baseline-path]
#
#   bash:
#     docker run --rm -v "$PWD:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 \
#         bash /src/benchmarks/ViShap.Viper.Serialization.Benchmarks/reproduction/check.sh <ref> [baseline-path]
#
#   Git Bash on Windows rewrites container paths; prefix the command with MSYS_NO_PATHCONV=1 there.
#
#   <ref>            the tag or commit the baseline was taken from, e.g. v1.0.0
#   [baseline-path]  repository-relative path of the committed baseline whose size table to compare
#                    against; omitted when no baseline exists yet, which checks the sequence alone
#
# The clone is taken from the mounted repository, so only committed content reaches the container:
# nothing untracked in the working tree can make the check pass.

set -euo pipefail

REF="${1:?usage: check.sh <ref> [baseline-path]}"
BASELINE="${2:-}"

PROJECT="benchmarks/ViShap.Viper.Serialization.Benchmarks"
CLONE="/work/clone"
LOGS="/work/logs"
STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

step() {
    printf '\n=== %s\n' "$1"
}

fail() {
    printf '\nreproduction check FAILED: %s\n' "$1" >&2
    exit 1
}

# Runs one mode of the benchmark project and keeps its output for the assertions below.
harness() {
    local name="$1"
    shift
    dotnet run --project "$PROJECT" --configuration Release --no-build -- "$@" \
        | tee "$LOGS/$name.log"
}

mkdir -p "$LOGS"

step "environment"
dotnet --info | sed -n '1,12p'
uname -srm

step "clone $REF"
git config --global --add safe.directory '*'
rm -rf "$CLONE"
git clone --quiet /src "$CLONE"
cd "$CLONE"
git checkout --quiet "$REF"
COMMIT="$(git rev-parse HEAD)"
echo "commit $COMMIT"

step "restore and build"
dotnet restore Viper.sln
dotnet build Viper.sln --configuration Release --no-restore

step "verify — every dataset round-trips under every profile"
harness verify --verify
VERIFY_LINE="$(grep -E '^[0-9]+ pairs, [0-9]+ failed\.' "$LOGS/verify.log" || true)"
[ -n "$VERIFY_LINE" ] || fail "--verify printed no result; the mode does not exist at $REF"
VERIFY_PAIRS="$(sed -E 's/^([0-9]+) pairs.*/\1/' <<<"$VERIFY_LINE")"
VERIFY_FAILED="$(sed -E 's/^[0-9]+ pairs, ([0-9]+) failed\..*/\1/' <<<"$VERIFY_LINE")"
[ "$VERIFY_PAIRS" -gt 0 ] || fail "--verify checked no pairs at all"
[ "$VERIFY_FAILED" -eq 0 ] || fail "$VERIFY_FAILED of $VERIFY_PAIRS pairs did not round-trip"

step "sizes — the machine-independent table"
harness sizes --sizes
grep -qE '^[0-9]+ rows\. Written to ' "$LOGS/sizes.log" \
    || fail "--sizes printed no result; the mode does not exist at $REF"
SIZES="$CLONE/$PROJECT/BenchmarkDotNet.Artifacts/payload-sizes.csv"
[ -f "$SIZES" ] || fail "--sizes wrote no table"

SIZES_VERDICT="not compared — no baseline path was given"
if [ -n "$BASELINE" ]; then
    COMMITTED="/src/$BASELINE/payload-sizes.csv"
    [ -f "$COMMITTED" ] || fail "no size table at $COMMITTED"

    # Byte-identical is the requirement, not merely similar: a payload size is a property of the
    # format, so a difference here is a defect in the corpus or in the library, never in the machine.
    diff "$COMMITTED" "$SIZES" || fail "the size table differs from the one committed with the baseline"
    SIZES_VERDICT="byte-identical to $BASELINE/payload-sizes.csv"
fi
echo "$SIZES_VERDICT"

step "smoke — every suite starts"
harness smoke --smoke
SMOKE_LINE="$(grep -E '^Smoke: [0-9]+ benchmarks, [0-9]+ failed\.' "$LOGS/smoke.log" || true)"
[ -n "$SMOKE_LINE" ] || fail "--smoke printed no result; the mode does not exist at $REF"
SMOKE_COUNT="$(sed -E 's/^Smoke: ([0-9]+) benchmarks.*/\1/' <<<"$SMOKE_LINE")"
SMOKE_FAILED="$(sed -E 's/.*, ([0-9]+) failed\.$/\1/' <<<"$SMOKE_LINE")"
[ "$SMOKE_COUNT" -gt 0 ] || fail "--smoke ran no benchmarks at all"
[ "$SMOKE_FAILED" -eq 0 ] || fail "$SMOKE_FAILED of $SMOKE_COUNT benchmarks failed to run"

step "verdict"
cat <<VERDICT
reproduction check passed
  started            $STARTED_AT
  finished           $(date -u +%Y-%m-%dT%H:%M:%SZ)
  ref                $REF
  commit             $COMMIT
  container          $(uname -srm)
  sdk                $(dotnet --version)
  round trip         $VERIFY_PAIRS pairs, $VERIFY_FAILED failed
  payload sizes      $SIZES_VERDICT
  suites             $SMOKE_COUNT benchmarks, $SMOKE_FAILED failed
  timings            not compared — they belong to the recorded machine
VERDICT
