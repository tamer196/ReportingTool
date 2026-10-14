# syntax=docker/dockerfile:1
# Build context: the repository root. The API needs the shared Platform project and the
# repo-wide Directory.*.props files.
#   docker build -f src/Services/ReportingTool.Api/Dockerfile --build-arg SOURCE_REVISION=$(git rev-parse HEAD) .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from project files only, so the package layer is cached across code changes.
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY src/Platform/ReportingTool.Platform/ReportingTool.Platform.csproj src/Platform/ReportingTool.Platform/
COPY src/Services/ReportingTool.Api/ReportingTool.Api.csproj src/Services/ReportingTool.Api/
RUN dotnet restore src/Services/ReportingTool.Api/ReportingTool.Api.csproj

COPY src/Platform/ src/Platform/
COPY src/Services/ReportingTool.Api/ src/Services/ReportingTool.Api/

# The git commit becomes part of the informational version (/api/system/info), so QA and the
# demo can see which commit runs. .git/ is not in the build context, so the commit comes in as
# this argument: CI passes github.sha, compose.yaml passes RT_SOURCE_REVISION.
ARG SOURCE_REVISION=local
RUN dotnet publish src/Services/ReportingTool.Api/ReportingTool.Api.csproj \
    --configuration Release --no-restore --output /app \
    -p:SourceRevisionId=${SOURCE_REVISION}

# Chiseled: no shell, no package manager, non-root by default. "-extra" adds ICU and tzdata,
# which time-zone conversion (#95) and culture-aware formatting need.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReportingTool.Api.dll"]
