# MDSM development targets

- Infinite is the development target. Implement new features for Infinite worlds
  and exercise them through the streamed lifecycle. Keep Legacy changes to
  basic bug fixes unless the user asks for broader coverage.
- Legacy means the existing 1024x1024 world option. Use
  `dotnet run -- --legacy-smoke-test` only for basic Legacy bug triage.
- Small, Normal, and Massive are retired. Keep them out of generation runs,
  benchmarks, and regression matrices. Compact, hand-built in-memory maps can
  still serve as fixtures for shared gameplay, rendering, and save-code tests.

Run `dotnet build` and the focused checks for the change. The routine CPU
regression entry point is `dotnet run -- --self-test`. Use
`dotnet run -- --graphics-test` for graphics regression and
`dotnet run -- --infinite-graphics-test` for sustained live Infinite profiling.

Record development notes in `Summaries/CODEX-SUMMARY.md`. Leave `README.md`
for the project author. Publishing is handled manually.
