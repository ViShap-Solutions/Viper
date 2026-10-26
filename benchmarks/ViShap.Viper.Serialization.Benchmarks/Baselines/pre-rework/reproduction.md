# Reproducing pre-rework

Revision `916f80551fd59f9fceeb25176ec9ee4d7806a443`, tag `pre-rework`. Nothing below depends on a file outside this repository.

## Machine state

The timings of this run belong to the machine `environment.json` records. Reproduce them on the
same hardware, on mains power, with no other interactive workload, and under the same GC mode.
Payload sizes, compression ratios and result states are properties of the format and hold on any
machine.

## Commands, in order

```text
git clone <this repository>
git checkout pre-rework
dotnet restore Viper.sln
dotnet build Viper.sln --configuration Release --no-restore
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --track A
```

The last line is the whole of it: it writes the manifest, verifies every dataset, records the
sizes, runs the suites in order, measures cold start and sustained load, and generates this
directory's report and charts.

## Re-running one part alone

```text
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --verify          # round trips, no timing
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --sizes           # the size table
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --manifest        # the environment
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --contract-cold   # member-plan construction
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --cold            # cold start
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --soak 10         # sustained load
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --track A --filter '*Algorithm*'
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks -c Release -- --report Baselines/pre-rework
```

A filter narrows the run to the suites whose name matches it, and a narrowed run is a partial
run: it lands under `Measurements/` and never under `Baselines/`. The last line regenerates the
report and the charts from the files already in this directory, which is what makes the report a
view over them rather than a record of its own.

## Checking these instructions are complete

Run them in a container that starts from the vendor image and holds nothing from this machine. It
compares the payload sizes, the verification outcomes and the result states, which are
machine-independent, and it does not compare timings.

```text
docker run --rm -v "$PWD:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0                 bash /src/benchmarks/ViShap.Viper.Serialization.Benchmarks/reproduction/check.sh pre-rework                 benchmarks/ViShap.Viper.Serialization.Benchmarks/Baselines/pre-rework
```
