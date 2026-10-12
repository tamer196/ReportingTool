# Reporting Tool

A web-based reporting tool: business users build raw-data and formatted report templates by drag
and drop, run them on demand or on a schedule, and keep the results in their report collection.

This repository holds the Angular front end, the .NET services, and their build and deployment
pipelines. Requirements, the technical design and the full developer guide are in Paca (project
**Reporting Tool**, prefix **RT**): see **Architecture/Developer Setup & Pipelines**.

## Quick start

Prerequisites: Docker Desktop, .NET 10 SDK, Node.js 24.

```powershell
git clone <this repository>
cd reporting-tool
dotnet tool restore
./scripts/init-local-secrets.ps1     # macOS/Linux: ./scripts/init-local-secrets.sh
docker compose up --build
```

Open http://localhost:8080. The page lists greetings read from SQL Server through the API; saving
one writes it back. That is the platform's end-to-end "hello world".

## Day-to-day commands

| What | Command |
| --- | --- |
| Build the services | `dotnet build ReportingTool.slnx` |
| Test the services (needs Docker) | `dotnet test --solution ReportingTool.slnx` |
| Lint the services | `dotnet format ReportingTool.slnx --verify-no-changes` |
| Run the API from the IDE / CLI | `docker compose up -d sqlserver` then `dotnet run --project src/Services/ReportingTool.Api` |
| Run the front end with hot reload | `cd src/Web && npm ci && npm start` → http://localhost:4300 |
| Test / lint / build the front end | `npm run test:ci`, `npm run lint`, `npm run build` |

## Layout

```
src/Platform/ReportingTool.Platform   shared plumbing: logging, error handling, vault secrets,
                                      health checks, UTC storage, migrations
src/Services/ReportingTool.Api        first .NET service (hello-world slice)
src/Web                               Angular app and its nginx image
tests/                                .NET unit and integration tests
compose.yaml, scripts/                local environment
.github/workflows/                    CI (pull requests) and CD (main)
```

Coding conventions for this repository are in [CLAUDE.md](CLAUDE.md) and
[src/Web/STYLE.md](src/Web/STYLE.md).
