# Wedge — Project History

## Project Context

- **Project:** CsvLoader
- **Tech Stack:** .NET 10, C#, GitHub Actions CI/CD
- **What it does:** CLI tool — single self-contained binary preferred (per PRD section 7)
- **Requested by:** Michael Prattinger
- **Key docs:** `docs/prd.md` (section 7 tech stack, section 9 constraints), `GitVersion.yml` already present

## Key Build Notes

- Target: .NET 10, cross-platform (Windows + Linux minimum per PRD)
- GitVersion.yml already present — wire into CI for semantic versioning
- Single self-contained binary: `dotnet publish --self-contained -p:PublishSingleFile=true`
- CI pipeline must: build → test (Leia's tests must pass) → publish artifact
- Secrets never in workflow files or CI logs

## Learnings
