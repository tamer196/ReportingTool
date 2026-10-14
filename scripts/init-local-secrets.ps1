<#
.SYNOPSIS
    Creates this machine's local development secrets. Run once after cloning.

.DESCRIPTION
    Generates a random SQL Server SA password and writes:
      .secrets/sql_sa_password              Docker Compose secret for the SQL Server container
      .secrets/Database__ConnectionString   Docker Compose secret mounted into the API container
      user-secrets Database:ConnectionString for 'dotnet run' of the API against localhost:1433

    .secrets/ is gitignored and user-secrets live outside the repository, so nothing generated
    here can be committed.

    Works in Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Force
    Replace existing secrets. The SQL Server volume keeps the old SA password, so run
    'docker compose down -v' first (this deletes the local database).
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$secretsDir = Join-Path $repoRoot '.secrets'
$apiProject = Join-Path $repoRoot 'src/Services/ReportingTool.Api'

if ((Test-Path (Join-Path $secretsDir 'sql_sa_password')) -and -not $Force) {
    Write-Host 'Local secrets already exist in .secrets/. Use -Force to replace them (after docker compose down -v).'
    exit 0
}

New-Item -ItemType Directory -Force -Path $secretsDir | Out-Null

# 24 random characters from an alphabet that needs no escaping in a connection string or shell,
# plus a fixed suffix so SQL Server's complexity policy (3 of 4 character classes) always passes.
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$password = (-join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })) + '-Rt7'

function Write-Secret([string]$name, [string]$value) {
    # No trailing newline: the configuration key-per-file provider would keep it in the value.
    [System.IO.File]::WriteAllText((Join-Path $secretsDir $name), $value)
}

$options = 'Database=ReportingTool;User Id=sa;Encrypt=True;TrustServerCertificate=True'
Write-Secret 'sql_sa_password' $password
Write-Secret 'Database__ConnectionString' "Server=sqlserver,1433;$options;Password=$password"

$sqlPort = if ($env:RT_SQL_PORT) { $env:RT_SQL_PORT } else { '1433' }
dotnet user-secrets set 'Database:ConnectionString' "Server=localhost,$sqlPort;$options;Password=$password" --project $apiProject | Out-Null

Write-Host 'Local secrets created:'
Write-Host '  .secrets/sql_sa_password, .secrets/Database__ConnectionString (Docker Compose)'
Write-Host '  user-secrets Database:ConnectionString (dotnet run of ReportingTool.Api)'
Write-Host 'Next: docker compose up --build, then open http://localhost:8080'
