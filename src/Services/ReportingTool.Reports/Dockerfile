# syntax=docker/dockerfile:1
# Build context: the repository root (needs Platform, Directory.*.props, assets/).
#   docker build -f src/Services/ReportingTool.Reports/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY src/Platform/ReportingTool.Platform/ReportingTool.Platform.csproj src/Platform/ReportingTool.Platform/
COPY src/Services/ReportingTool.Reports/ReportingTool.Reports.csproj src/Services/ReportingTool.Reports/
RUN dotnet restore src/Services/ReportingTool.Reports/ReportingTool.Reports.csproj

COPY src/Platform/ src/Platform/
COPY src/Services/ReportingTool.Reports/ src/Services/ReportingTool.Reports/
COPY assets/vendor/echarts/ assets/vendor/echarts/

ARG SOURCE_REVISION=local
RUN dotnet publish src/Services/ReportingTool.Reports/ReportingTool.Reports.csproj \
    --configuration Release --no-restore --output /app \
    -p:SourceRevisionId=${SOURCE_REVISION}

# Alpine, because its chromium package is a real package (Ubuntu's is a snap stub and the
# default .NET 10 image is Ubuntu). Every package here is free for commercial production use
# (D-16): chromium BSD-3 + third-party, qpdf Apache-2.0, ICU Unicode-3.0, fontconfig MIT.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache chromium qpdf icu-libs icu-data-full tzdata fontconfig

# The same OFL-1.1 font files the designer canvas loads, so canvas and PDF match (WYSIWYG).
COPY assets/fonts/ /usr/share/fonts/reporting/
RUN fc-cache -f \
    && mkdir -p /data/outputs /tmp/rt-render \
    && chown -R $APP_UID /data/outputs /tmp/rt-render

# Arabic month names and culture-aware formatting need ICU (the Alpine image ships invariant).
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReportingTool.Reports.dll"]
