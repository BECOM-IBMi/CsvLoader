---
name: "readytorun-cli-startup"
description: "Enable ReadyToRun for self-contained CLI publishes when cold start is dominated by JIT"
domain: "performance"
confidence: "high"
source: "observed"
tools:
  - name: "dotnet"
    description: "Builds and tests the CLI after publish setting changes"
    when: "After changing project publish properties"
---

## Context
Use this when a .NET CLI ships as self-contained + single-file and Release cold-start time is much worse than warm-start time. That pattern usually means first-run JIT is the real problem, not application logic.

## Patterns
- Add `<PublishReadyToRun>true</PublishReadyToRun>` in a Release-only `PropertyGroup` so publish optimization stays scoped to production builds.
- Keep existing publish settings (`PublishSingleFile`, `SelfContained`) unchanged unless the task explicitly asks for packaging changes.
- Validate with normal build/test commands, but treat missing installer artifacts as an environment issue rather than a code regression.

## Examples
- `src/CsvLoader/CsvLoader.csproj` adds a Release-only `PublishReadyToRun` flag to cut cold startup from 17.8s toward 3-5s.
- Existing measurements in `.squad/agents/han/history.md` show warm-start time staying low, which points at JIT overhead.

## Anti-Patterns
- Do not enable trimming first when the dependency stack is reflection-heavy.
- Do not apply ReadyToRun globally if the intent is only Release/publish startup improvement.
