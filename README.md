# Reporting Tool

[![CI](https://github.com/tamer196/ReportingTool/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/tamer196/ReportingTool/actions/workflows/ci.yml?query=branch%3Amain)

A web-based reporting tool: business users build raw-data and formatted report templates by drag
and drop, run them on demand or on a schedule, and keep the results in their report collection.

This repository holds the Angular front end, the .NET services, the local environment and the
CI pipeline. Requirements, the technical design and the full developer guide are in Paca (project
**Reporting Tool**, prefix **RT**): see **Architecture/Developer Setup & Pipelines**.

Phase 1 runs entirely on one machine with Docker Compose: nothing is deployed to a cloud.

## Quick start

Prerequisites: Docker Desktop, .NET 10 SDK, Node.js 24.

```powershell
git clone https://github.com/tamer196/ReportingTool.git reporting-tool
cd reporting-tool
dotnet tool restore
./scripts/init-local-secrets.ps1     # macOS/Linux: ./scripts/init-local-secrets.sh
docker compose up --build
```

Open http://localhost:8080. The page lists greetings read from SQL Server through the API; saving
one writes it back. That is the platform's end-to-end "hello world".

The setup script generates a random local SQL Server password into `.secrets/`, which git ignores.
No secret is ever committed.

## Day-to-day commands

| What | Command |
| --- | --- |
| Build the services | `dotnet build ReportingTool.slnx` |
| Test the services (needs Docker) | `dotnet test --solution ReportingTool.slnx` |
| Lint the services | `dotnet format ReportingTool.slnx --verify-no-changes` |
| Run the API from the IDE / CLI | `docker compose up -d sqlserver` then `dotnet run --project src/Services/ReportingTool.Api` |
| Run the front end with hot reload | `cd src/Web && npm ci && npm start` → http://localhost:4300 |
| Test / lint / build the front end | `npm run test:ci`, `npm run lint`, `npm run build` |

## Contributing

Every change reaches `main` through a pull request. The full process is the Paca doc
**Process/Development Process & Automations** (§7 covers code and branches).

1. **One branch per Paca task**, from the latest `main`: `feature/RT-<n>-<short-name>`, or
   `fix/RT-<n>-<short-name>` for a bug.
2. **Commit messages** start with `RT-<n>:`.
3. **Before pushing**, run the build, lint and tests above. CI treats every compiler warning as
   an error.
4. **Open a pull request** titled `RT-<n>: <task title>`. The template asks for the Paca task
   link, what changed and how to test it. Put the pull request link in a comment on the task.
5. **CI must be green.** GitHub Actions runs on every pull request: .NET lint, build and tests;
   Angular lint, tests and build; dependency audits; and a Trivy scan of the repository and both
   images. Don't merge while a check fails.
6. **Review and merge.** The paired reviewer reviews the pull request, posts numbered findings on
   it and on the task, and merges an approved pull request with a merge commit
   (`gh pr merge --merge --delete-branch`). Nobody merges their own pull request.

`main` is protected: no direct pushes, no force-pushes, no deletion, merge commits only.

## Milestones and releases

The product is delivered milestone by milestone. Each milestone is an annotated tag
(`v0.1.0-m0` … `v0.8.0-m7`, then `v1.0.0`) with an entry in [CHANGELOG.md](CHANGELOG.md), release
notes in [docs/releases/](docs/releases/) and a
[GitHub Release](https://github.com/tamer196/ReportingTool/releases). `<Version>` in
`Directory.Build.props` names the milestone `main` is working towards, and
`/api/system/info` shows it with the commit, for example `0.1.0-m0+3f2a…`. The rules are in Paca,
*Delivery/00 - Milestones & Git Release Plan* §5.

## Layout

```
src/Platform/ReportingTool.Platform   shared plumbing: logging, error handling, vault secrets,
                                      health checks, UTC storage, migrations
src/Services/ReportingTool.Api        first .NET service (hello-world slice)
src/Web                               Angular app and its nginx image
tests/                                .NET unit and integration tests
compose.yaml, scripts/                local environment
.github/                              CI, build-only CD, pull request template, Dependabot
CHANGELOG.md, docs/releases/          one entry and one release-notes file per milestone tag
```

Coding conventions for this repository are in [CLAUDE.md](CLAUDE.md) and
[src/Web/STYLE.md](src/Web/STYLE.md).
