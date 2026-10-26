// A consumer of the three packages, built with native AOT analysis by Api/AotAnalysisTests (EXT-07)
// and published with native AOT by the CI job. It runs ContextRun: every entry point that takes a
// context's type information, the frozen payloads read and written back through the generated contexts,
// and a type no context offered refused — with the reflection path switched off.

return await ContextRun.Run();
