<#
.SYNOPSIS
    QA test environment: runs the whole stack from one commit of main, next to the developer
    stack on the same machine (RT-121).

.DESCRIPTION
    Actions:
      up [<commit>]  Exports <commit> (default: the latest origin/main) from this clone into
                     .qa-env/<name>/src, gives it its own secrets, and starts that commit's own
                     compose.yaml with fresh volumes. Then runs the smoke check. An environment
                     that already exists is replaced, data included.
      status         The commit, the addresses and the containers, then the smoke check again.
      reset          Starts the same commit again on fresh volumes: back to the seeded state,
                     nothing left from the previous run.
      fingerprint    Row count and checksum of every table in every SQL Server database of the
                     environment, and one hash over them all. Equal hashes mean equal data.
      logs           The latest container logs (-Service to pick one, -Tail for more lines).
      down           Removes the containers, volumes, networks, images and .qa-env/<name>.

    How the environment stays apart from the developer stack and from the outside:
      * Its own Compose project, <COMPOSE_PROJECT_NAME or reporting-tool>-<name>, so its own
        containers, volumes, networks and images (tagged with the commit).
      * A block of 10 ports from -PortBase (default: RT_WEB_PORT + 400, or 8480): web +0,
        API +1, SQL Server +2, further RT_*_PORT variables from +3. Every port is published on
        127.0.0.1 only.
      * The application containers sit on an internal Docker network with no route out (D-15).
        A gateway container (the web image's nginx) forwards the published ports to them.
      * RT_* and COMPOSE_* variables from your shell are ignored: what the tool doesn't set
        falls back to the commit's compose.yaml defaults, so the data doesn't depend on who
        starts it. .NET services run with ASPNETCORE_ENVIRONMENT=Test.

    Needs git and Docker with Compose 2.24.4 or later. Runs in Windows PowerShell 5.1 and
    PowerShell 7 (pwsh on macOS and Linux). Guide: "Architecture/Developer Setup & Pipelines"
    in Paca, section "QA test environment".

.EXAMPLE
    scripts/qa-env.ps1 up               # the latest main
    scripts/qa-env.ps1 up 3f2a9c1       # main at a merge commit
    scripts/qa-env.ps1 status
    scripts/qa-env.ps1 reset
    scripts/qa-env.ps1 down
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('up', 'status', 'reset', 'fingerprint', 'logs', 'down')]
    [string]$Action,

    # up only: the commit to run. A SHA, a tag or a ref; it must be on origin/main.
    [Parameter(Position = 1)]
    [string]$Commit,

    # The environment's name: the Compose project suffix and the folder under .qa-env/.
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,19}$')]
    [string]$Name = 'test',

    # up only: the first of the environment's 10 ports. Required for any -Name but 'test'.
    [ValidateRange(1024, 65526)]
    [int]$PortBase,

    # up only: allow a commit that isn't on origin/main (a pull request branch, for example).
    [switch]$AnyCommit,

    # up only: use this clone's origin/main as it is, without fetching.
    [switch]$NoFetch,

    # How long up, reset and status wait for /health/ready.
    [ValidateRange(30, 3600)]
    [int]$TimeoutSeconds = 300,

    # logs only.
    [string]$Service,
    [ValidateRange(1, 100000)]
    [int]$Tail = 200
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$BlockSize = 10
$DefaultPortOffset = 400
# The web image's runtime base (src/Web/Dockerfile), so the gateway adds no new component.
$GatewayImage = 'nginxinc/nginx-unprivileged:stable-alpine'
$GatewayService = 'qa-gateway'
# Each /health/ready call gets its own client timeout: while SQL Server is down the endpoint
# can take about 2 minutes to answer (RT-191).
$ReadyCallTimeoutSeconds = 10
$MinComposeVersion = [version]'2.24.4'
# Fixed slots in the port block; any other RT_*_PORT variable in compose.yaml gets the next one.
$FixedPortSlots = [ordered]@{ RT_WEB_PORT = 0; RT_API_PORT = 1; RT_SQL_PORT = 2 }

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BaseProject = if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { 'reporting-tool' }
$Project = "$BaseProject-$Name"
$StateDir = Join-Path (Join-Path $RepoRoot '.qa-env') $Name
$SrcDir = Join-Path $StateDir 'src'
$StateFile = Join-Path $StateDir 'state.json'
$OverrideFile = Join-Path $StateDir 'compose.qa-env.yaml'

function Write-Step([string]$Message) {
    Write-Host "qa-env: $Message" -ForegroundColor Cyan
}

# Runs a native command and fails on a non-zero exit code. Windows PowerShell 5.1 can turn a
# native command's stderr into terminating errors under 'Stop', and docker writes its progress
# there, so the preference is relaxed for the call.
function Invoke-Native([string]$File, [string[]]$Arguments, [switch]$AllowFailure) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $File @Arguments
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $saved
    }
    if ($code -ne 0 -and -not $AllowFailure) {
        throw "'$File $($Arguments -join ' ')' failed with exit code $code."
    }
}

# Same, returning stdout as one string.
function Get-NativeOutput([string]$File, [string[]]$Arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $File @Arguments
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $saved
    }
    if ($code -ne 0) {
        throw "'$File $($Arguments -join ' ')' failed with exit code $code."
    }
    return (@($lines) -join "`n")
}

function Invoke-Git([string[]]$Arguments) {
    Invoke-Native 'git' (@('-C', $RepoRoot) + $Arguments)
}

function Get-GitOutput([string[]]$Arguments) {
    return (Get-NativeOutput 'git' (@('-C', $RepoRoot) + $Arguments)).Trim()
}

function Assert-Prerequisites {
    $null = Get-NativeOutput 'git' @('--version')
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $versionText = & docker compose version --short 2>$null
        $code = $LASTEXITCODE
        if ($code -eq 0) {
            $null = & docker info --format '{{.ServerVersion}}' 2>$null
            $code = $LASTEXITCODE
        }
    }
    finally {
        $ErrorActionPreference = $saved
    }
    if ($code -ne 0) {
        throw 'Docker is not answering. Start Docker Desktop (Linux containers) and try again.'
    }
    $version = [version](([string]$versionText).Trim().TrimStart('v') -replace '[-+].*$', '')
    if ($version -lt $MinComposeVersion) {
        throw "Docker Compose $version is too old: the QA environment needs $MinComposeVersion or later (update Docker Desktop)."
    }
}

function Read-State {
    if (-not (Test-Path $StateFile)) {
        return $null
    }
    return (Get-Content -Raw -Path $StateFile | ConvertFrom-Json)
}

function Write-State($State) {
    [System.IO.File]::WriteAllText($StateFile, (ConvertTo-Json -InputObject $State -Depth 6))
}

function Get-StateVariables($State) {
    $variables = [ordered]@{}
    foreach ($property in $State.variables.PSObject.Properties) {
        $variables[$property.Name] = [string]$property.Value
    }
    return $variables
}

# Runs a script block with exactly the Compose variables the environment uses: every RT_* and
# COMPOSE_* variable of this process is removed for the duration and restored afterwards.
function Invoke-WithComposeVariables([System.Collections.IDictionary]$Variables, [scriptblock]$Script) {
    $saved = @{}
    foreach ($item in @(Get-ChildItem Env:)) {
        if ($item.Name -like 'RT_*' -or $item.Name -like 'COMPOSE_*') {
            $saved[$item.Name] = $item.Value
            [Environment]::SetEnvironmentVariable($item.Name, $null)
        }
    }
    try {
        foreach ($key in $Variables.Keys) {
            [Environment]::SetEnvironmentVariable($key, [string]$Variables[$key])
        }
        & $Script
    }
    finally {
        foreach ($key in $Variables.Keys) {
            [Environment]::SetEnvironmentVariable($key, $null)
        }
        foreach ($key in $saved.Keys) {
            [Environment]::SetEnvironmentVariable($key, $saved[$key])
        }
    }
}

function Get-ComposeArguments([switch]$WithOverride) {
    $arguments = @('compose', '--project-name', $Project, '--project-directory', $SrcDir,
        '--file', (Join-Path $SrcDir 'compose.yaml'))
    if ($WithOverride) {
        $arguments += @('--file', $OverrideFile)
    }
    return $arguments
}

function Invoke-Compose($State, [string[]]$Arguments, [switch]$AllowFailure) {
    $composeArguments = (Get-ComposeArguments -WithOverride) + $Arguments
    Invoke-WithComposeVariables (Get-StateVariables $State) {
        Invoke-Native 'docker' $composeArguments -AllowFailure:$AllowFailure
    }
}

function Get-ComposeOutput($State, [string[]]$Arguments) {
    $composeArguments = (Get-ComposeArguments -WithOverride) + $Arguments
    return (Invoke-WithComposeVariables (Get-StateVariables $State) {
        Get-NativeOutput 'docker' $composeArguments
    })
}

function Test-PortFree([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $attempt = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        if ($attempt.AsyncWaitHandle.WaitOne(300) -and $client.Connected) {
            return $false
        }
    }
    catch {
        # Refused: nothing listens there.
    }
    finally {
        $client.Close()
    }
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $Port)
    try {
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        $listener.Stop()
    }
}

function Resolve-Commit([string]$Requested, [switch]$SkipMainCheck) {
    if (-not $NoFetch) {
        Write-Step 'Fetching origin'
        Invoke-Git @('fetch', '--quiet', 'origin') | Out-Null
    }
    if (-not $Requested) {
        $Requested = 'origin/main'
    }
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $sha = & git -C $RepoRoot rev-parse --verify --quiet "$Requested^{commit}" 2>$null
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $saved
    }
    if ($code -ne 0 -or -not $sha) {
        throw "Commit '$Requested' was not found in this clone. Check the SHA (git fetch origin first if it is new)."
    }
    $sha = ([string]$sha).Trim()
    if (-not $SkipMainCheck -and -not $AnyCommit) {
        $saved = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & git -C $RepoRoot merge-base --is-ancestor $sha origin/main 2>$null
            $onMain = $LASTEXITCODE -eq 0
        }
        finally {
            $ErrorActionPreference = $saved
        }
        if (-not $onMain) {
            throw "Commit $sha is not on origin/main. QA tests main at the merge commit; add -AnyCommit to run another commit anyway."
        }
    }
    return $sha
}

# Copies the commit's files (without .git) into .qa-env/<name>/src through a temporary index,
# so this clone's own index, branch and working tree stay untouched.
function Export-Commit([string]$Sha) {
    if (Test-Path $SrcDir) {
        Remove-Item -Recurse -Force $SrcDir
    }
    New-Item -ItemType Directory -Force -Path $SrcDir | Out-Null
    $index = Join-Path $StateDir 'export.index'
    $savedIndex = $env:GIT_INDEX_FILE
    $env:GIT_INDEX_FILE = $index
    try {
        Invoke-Git @('read-tree', $Sha)
        # The prefix is relative to the repository root, where git -C runs.
        Invoke-Git @('checkout-index', '--all', '--force', "--prefix=.qa-env/$Name/src/")
    }
    finally {
        $env:GIT_INDEX_FILE = $savedIndex
        if (Test-Path $index) {
            Remove-Item -Force $index
        }
    }
}

# The commit's own setup script writes its Docker Compose secrets, so secrets added by later
# stories come along. -ComposeOnly keeps it away from the user-secrets of the developer clone.
# Commits from before that switch get the two secrets they need from here.
function New-EnvironmentSecrets {
    $secretsDir = Join-Path $SrcDir '.secrets'
    $script = Join-Path $SrcDir 'scripts/init-local-secrets.ps1'
    if ((Test-Path $script) -and (Get-Command $script).Parameters.ContainsKey('ComposeOnly')) {
        & $script -ComposeOnly | Out-Null
    }
    else {
        New-LegacySecrets $secretsDir
    }
    if ($env:OS -ne 'Windows_NT') {
        # Bind-mounted Compose secrets keep host permissions; the containers run as non-root users.
        Invoke-Native 'chmod' (@('644') + @(Get-ChildItem -File $secretsDir | ForEach-Object { $_.FullName }))
    }
}

# The two secrets compose.yaml needed before -ComposeOnly existed, made the way
# init-local-secrets.ps1 makes them.
function New-LegacySecrets([string]$SecretsDir) {
    New-Item -ItemType Directory -Force -Path $SecretsDir | Out-Null
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $password = (-join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })) + '-Rt7'
    $options = 'Database=ReportingTool;User Id=sa;Encrypt=True;TrustServerCertificate=True'
    [System.IO.File]::WriteAllText((Join-Path $SecretsDir 'sql_sa_password'), $password)
    [System.IO.File]::WriteAllText((Join-Path $SecretsDir 'Database__ConnectionString'),
        "Server=sqlserver,1433;$options;Password=$password")
}

function Get-PortVariables([string]$ComposeText, [int]$Base) {
    $variables = [ordered]@{}
    foreach ($key in $FixedPortSlots.Keys) {
        $variables[$key] = $Base + $FixedPortSlots[$key]
    }
    $slot = $FixedPortSlots.Count
    $found = @([regex]::Matches($ComposeText, '\$\{(RT_[A-Z0-9_]+_PORT)\b') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    foreach ($variable in $found) {
        if (-not $variables.Contains($variable)) {
            if ($slot -ge $BlockSize) {
                throw "compose.yaml uses more port variables than the block of $BlockSize ports holds."
            }
            $variables[$variable] = $Base + $slot
            $slot++
        }
    }
    return $variables
}

function ConvertTo-YamlString([string]$Value) {
    return "'" + ($Value -replace "'", "''") + "'"
}

# Reads the commit's resolved Compose model and writes the override that isolates it.
function New-Override($State) {
    $variables = Get-StateVariables $State
    $baseArguments = (Get-ComposeArguments) + @('config', '--format', 'json')
    $json = Invoke-WithComposeVariables $variables { Get-NativeOutput 'docker' $baseArguments }
    $model = $json | ConvertFrom-Json

    $reserved = @{}
    foreach ($key in $variables.Keys) {
        if ($key -like '*_PORT') {
            $reserved[[int]$variables[$key]] = $true
        }
    }
    $taken = @{}
    $endpoints = @()
    $images = @()
    $databases = @()
    $tag = $State.commit.Substring(0, 12)

    $services = @($model.services.PSObject.Properties | Sort-Object Name)
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Written by scripts/qa-env.ps1 for $Project at $($State.commit). 'up' and 'reset' rewrite it.")
    $lines.Add('services:')
    foreach ($entry in $services) {
        $serviceName = $entry.Name
        $definition = $entry.Value
        $names = @($definition.PSObject.Properties.Name)
        if ($names -contains 'network_mode' -and $definition.network_mode) {
            throw "Service '$serviceName' sets network_mode, which the QA environment can't isolate."
        }
        $lines.Add("  ${serviceName}:")
        if ($names -contains 'build' -and $definition.build) {
            $image = "$Project-${serviceName}:$tag"
            $images += $image
            $lines.Add("    image: $(ConvertTo-YamlString $image)")
        }
        if ($names -contains 'environment' -and $definition.environment -and
            (@($definition.environment.PSObject.Properties.Name) -contains 'ASPNETCORE_ENVIRONMENT')) {
            $lines.Add('    environment:')
            $lines.Add("      ASPNETCORE_ENVIRONMENT: 'Test'")
        }
        $lines.Add('    ports: !reset []')
        $lines.Add('    networks: !override')
        $lines.Add('      - qa-internal')

        if ($names -contains 'ports' -and $definition.ports) {
            foreach ($port in @($definition.ports)) {
                $published = ''
                if (@($port.PSObject.Properties.Name) -contains 'published' -and $null -ne $port.published) {
                    $published = [string]$port.published
                }
                if ($published -match '-') {
                    throw "Service '$serviceName' publishes a port range ($published), which the QA environment doesn't support."
                }
                $hostPort = 0
                if ($published -match '^\d+$' -and $reserved.ContainsKey([int]$published)) {
                    $hostPort = [int]$published
                }
                else {
                    # A random or fixed host port: give it a free slot of the block instead.
                    for ($offset = 0; $offset -lt $BlockSize; $offset++) {
                        $candidate = $State.portBase + $offset
                        if (-not $reserved.ContainsKey($candidate) -and -not $taken.ContainsKey($candidate)) {
                            $hostPort = $candidate
                            break
                        }
                    }
                    if ($hostPort -eq 0) {
                        throw "The services publish more ports than the block of $BlockSize ports holds."
                    }
                }
                if ($taken.ContainsKey($hostPort)) {
                    throw "Two services publish host port $hostPort."
                }
                $taken[$hostPort] = $true
                $protocol = 'tcp'
                if (@($port.PSObject.Properties.Name) -contains 'protocol' -and $port.protocol) {
                    $protocol = [string]$port.protocol
                }
                $endpoints += [pscustomobject]@{
                    service  = $serviceName
                    target   = [int]$port.target
                    port     = $hostPort
                    protocol = $protocol
                }
            }
        }

        $image = ''
        if ($names -contains 'image' -and $definition.image) {
            $image = [string]$definition.image
        }
        if ($image -like '*mssql/server*' -and $names -contains 'secrets' -and $definition.secrets) {
            foreach ($secret in @($definition.secrets)) {
                $target = [string]$secret.source
                if (@($secret.PSObject.Properties.Name) -contains 'target' -and $secret.target) {
                    $target = [string]$secret.target
                }
                if ($target -like '*sa_password*') {
                    if (-not $target.StartsWith('/')) {
                        $target = "/run/secrets/$target"
                    }
                    $databases += [pscustomobject]@{ service = $serviceName; passwordFile = $target }
                    break
                }
            }
        }
    }

    # The gateway: the only container with a route to the host. It forwards each published
    # port to its service over the internal network and makes no connections of its own.
    $lines.Add("  ${GatewayService}:")
    $lines.Add("    image: $(ConvertTo-YamlString $GatewayImage)")
    $lines.Add('    networks:')
    $lines.Add('      - qa-internal')
    $lines.Add('      - qa-edge')
    $lines.Add('    configs:')
    $lines.Add('      - source: qa-gateway-conf')
    $lines.Add('        target: /etc/nginx/nginx.conf')
    if ($endpoints.Count -gt 0) {
        $lines.Add('    ports:')
        foreach ($endpoint in $endpoints) {
            $lines.Add("      - '127.0.0.1:$($endpoint.port):$($endpoint.port)/$($endpoint.protocol)'")
        }
    }
    $lines.Add('networks:')
    $lines.Add('  qa-internal:')
    $lines.Add('    internal: true')
    $lines.Add('  qa-edge: {}')
    $lines.Add('configs:')
    $lines.Add('  qa-gateway-conf:')
    $lines.Add('    content: |')
    $lines.Add('      worker_processes 1;')
    $lines.Add('      error_log /dev/stderr notice;')
    $lines.Add('      pid /tmp/nginx.pid;')
    $lines.Add('      events { worker_connections 1024; }')
    $lines.Add('      stream {')
    # Docker's DNS, asked again per connection, so a restarted container is found at its new address.
    $lines.Add('        resolver 127.0.0.11 valid=10s ipv6=off;')
    foreach ($endpoint in $endpoints) {
        $udp = ''
        if ($endpoint.protocol -eq 'udp') {
            $udp = ' udp'
        }
        # $$ is Compose's escape for a literal $ (an nginx variable).
        $lines.Add("        server { listen $($endpoint.port)$udp; set `$`$upstream $($endpoint.service):$($endpoint.target); proxy_pass `$`$upstream; }")
    }
    $lines.Add('      }')
    [System.IO.File]::WriteAllText($OverrideFile, (($lines -join "`n") + "`n"))

    $State.endpoints = @($endpoints)
    $State.images = @($images)
    $State.databases = @($databases)
}

function Get-Endpoint($State, [string]$ServiceName, [int]$Target) {
    foreach ($endpoint in @($State.endpoints)) {
        if ($endpoint.service -eq $ServiceName -and $endpoint.target -eq $Target) {
            return $endpoint
        }
    }
    return $null
}

function Get-Containers($State) {
    $text = Get-ComposeOutput $State @('ps', '--all', '--format', 'json')
    $text = $text.Trim()
    if (-not $text) {
        return @()
    }
    if ($text.StartsWith('[')) {
        return @($text | ConvertFrom-Json)
    }
    return @($text -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}

function Assert-NoFailedContainer($State) {
    $containers = @(Get-Containers $State)
    if ($containers.Count -eq 0) {
        throw "The environment has no containers (Docker was restarted, or they were removed). Start it again with: scripts/qa-env.ps1 reset"
    }
    foreach ($container in $containers) {
        if ($container.State -eq 'exited' -and [int]$container.ExitCode -ne 0) {
            Invoke-Compose $State @('logs', '--tail', '40', $container.Service) -AllowFailure
            throw "Service '$($container.Service)' stopped with exit code $($container.ExitCode) (its last log lines are above)."
        }
    }
}

function Invoke-Http([string]$Uri) {
    return Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec $ReadyCallTimeoutSeconds
}

# /health/ready on the API, /healthz on the web app, then /api/system/info through the web
# app: it must show the commit under test and the Test environment.
function Invoke-SmokeCheck($State) {
    $api = Get-Endpoint $State 'api' 8080
    $web = Get-Endpoint $State 'web' 8080
    if ($null -eq $api -or $null -eq $web) {
        throw 'The commit has no published api and web services on port 8080, so the smoke check cannot run.'
    }

    Write-Step "Waiting for http://127.0.0.1:$($api.port)/health/ready (up to $TimeoutSeconds s)"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $ready = $false
    $lastError = ''
    while (-not $ready) {
        Assert-NoFailedContainer $State
        try {
            $response = Invoke-Http "http://127.0.0.1:$($api.port)/health/ready"
            $ready = $response.StatusCode -eq 200
        }
        catch {
            $lastError = $_.Exception.Message
        }
        if (-not $ready) {
            if ((Get-Date) -gt $deadline) {
                throw "/health/ready did not report Healthy within $TimeoutSeconds s. Last answer: $lastError. See 'scripts/qa-env.ps1 logs'."
            }
            Start-Sleep -Seconds 3
        }
    }

    $healthz = Invoke-Http "http://127.0.0.1:$($web.port)/healthz"
    if ($healthz.StatusCode -ne 200) {
        throw "The web app's /healthz answered $($healthz.StatusCode)."
    }

    $info = Invoke-RestMethod -UseBasicParsing -Uri "http://127.0.0.1:$($web.port)/api/system/info" -TimeoutSec $ReadyCallTimeoutSeconds
    if (-not ([string]$info.version).EndsWith("+$($State.commit)")) {
        throw "/api/system/info shows version '$($info.version)', not the commit under test ($($State.commit))."
    }
    if ($info.environment -ne 'Test') {
        throw "/api/system/info shows environment '$($info.environment)', not 'Test'."
    }
    Write-Step "Smoke check passed: $($info.service) $($info.version) ($($info.environment))"
}

function Show-Environment($State) {
    Write-Host ''
    Write-Host "QA environment '$Name' (Compose project $Project)"
    Write-Host "  Commit:  $($State.commit)"
    Write-Host "  Started: $($State.startedUtc) (UTC)"
    $web = Get-Endpoint $State 'web' 8080
    if ($web) {
        Write-Host "  Open:    http://127.0.0.1:$($web.port)"
    }
    foreach ($endpoint in @($State.endpoints)) {
        Write-Host ("  {0,-12} port {1,-5} -> 127.0.0.1:{2}" -f $endpoint.service, $endpoint.target, $endpoint.port)
    }
    if (@($State.databases).Count -gt 0) {
        Write-Host "  SQL Server login: sa, password in $(Join-Path $SrcDir '.secrets')"
    }
    Write-Host ''
}

function Remove-Environment($State, [switch]$KeepImages) {
    if ($State -and (Test-Path (Join-Path $SrcDir 'compose.yaml')) -and (Test-Path $OverrideFile)) {
        Invoke-Compose $State @('down', '--volumes', '--remove-orphans', '--timeout', '10')
    }
    else {
        # No usable state: remove whatever carries the project's label.
        Invoke-Native 'docker' @('compose', '--project-name', $Project, 'down', '--volumes', '--remove-orphans', '--timeout', '10')
    }
    if ($State -and -not $KeepImages) {
        foreach ($image in @($State.images)) {
            if ($image) {
                Invoke-Native 'docker' @('image', 'rm', '--force', $image) -AllowFailure | Out-Null
            }
        }
    }
}

# Anything of the project still on the Docker host, found by Compose's project label and by the
# image names the environment built.
function Get-Leftovers($State) {
    $filter = "label=com.docker.compose.project=$Project"
    $left = @()
    foreach ($kind in @('container', 'volume', 'network')) {
        $listArguments = @($kind, 'ls', '--quiet', '--filter', $filter)
        if ($kind -eq 'container') {
            $listArguments = @('container', 'ls', '--all', '--quiet', '--filter', $filter)
        }
        $ids = (Get-NativeOutput 'docker' $listArguments).Trim()
        if ($ids) {
            $left += "$(@($ids -split "`n").Count) $kind(s)"
        }
    }
    if ($State) {
        foreach ($image in @($State.images)) {
            if ($image -and (Get-NativeOutput 'docker' @('image', 'ls', '--quiet', $image)).Trim()) {
                $left += "image $image"
            }
        }
    }
    return $left
}

function Start-Environment([string]$Sha, [int]$Base) {
    $existing = Read-State
    if ($existing) {
        Write-Step "Removing the current environment (commit $($existing.commit)) first"
        Remove-Environment $existing -KeepImages:($existing.commit -eq $Sha)
    }
    elseif (Test-Path $StateDir) {
        Remove-Environment $null
    }

    $busy = @()
    for ($offset = 0; $offset -lt $BlockSize; $offset++) {
        if (-not (Test-PortFree ($Base + $offset))) {
            $busy += $Base + $offset
        }
    }
    if ($busy.Count -gt 0) {
        throw "Ports $($busy -join ', ') on 127.0.0.1 are in use. The environment needs $Base-$($Base + $BlockSize - 1); pick another block with -PortBase."
    }

    if (Test-Path $StateDir) {
        Remove-Item -Recurse -Force $StateDir
    }
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    Write-Step "Exporting commit $Sha"
    Export-Commit $Sha
    New-EnvironmentSecrets

    $composeText = [System.IO.File]::ReadAllText((Join-Path $SrcDir 'compose.yaml'))
    $variables = Get-PortVariables $composeText $Base
    $variables['COMPOSE_PROJECT_NAME'] = $Project
    $variables['RT_SOURCE_REVISION'] = $Sha
    $state = [pscustomobject]@{
        name       = $Name
        project    = $Project
        commit     = $Sha
        portBase   = $Base
        startedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        variables  = [pscustomobject]$variables
        endpoints  = @()
        images     = @()
        databases  = @()
    }
    New-Override $state
    Write-State $state

    Write-Step "Building and starting $Project"
    Invoke-Compose $state @('up', '--detach', '--build', '--remove-orphans')
    Invoke-SmokeCheck $state
    Show-Environment $state
}

function Get-RequiredState {
    $state = Read-State
    if (-not $state) {
        throw "There is no QA environment '$Name' in this clone. Start one with: scripts/qa-env.ps1 up"
    }
    return $state
}

# Row count and checksum per table, for every user database of every SQL Server service.
function Get-Fingerprint($State) {
    $sql = @'
SET NOCOUNT ON;
CREATE TABLE #tables (db sysname, tbl nvarchar(300));
CREATE TABLE #result (db sysname, tbl nvarchar(300), row_count bigint, checksum_sum decimal(38, 0));
DECLARE @db sysname, @tbl nvarchar(300), @q nvarchar(max);
DECLARE dbs CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 ORDER BY name;
OPEN dbs;
FETCH NEXT FROM dbs INTO @db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @q = N'INSERT #tables SELECT N''' + REPLACE(@db, N'''', N'''''') + N''', QUOTENAME(s.name) + N''.'' + QUOTENAME(t.name) FROM '
        + QUOTENAME(@db) + N'.sys.tables t JOIN ' + QUOTENAME(@db) + N'.sys.schemas s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0;';
    EXEC (@q);
    FETCH NEXT FROM dbs INTO @db;
END
CLOSE dbs;
DEALLOCATE dbs;
DECLARE tbls CURSOR LOCAL FAST_FORWARD FOR SELECT db, tbl FROM #tables ORDER BY db, tbl;
OPEN tbls;
FETCH NEXT FROM tbls INTO @db, @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @q = N'INSERT #result SELECT N''' + REPLACE(@db, N'''', N'''''') + N''', N''' + REPLACE(@tbl, N'''', N'''''')
        + N''', COUNT_BIG(*), ISNULL(SUM(CAST(BINARY_CHECKSUM(*) AS decimal(38, 0))), 0) FROM ' + QUOTENAME(@db) + N'.' + @tbl + N';';
    EXEC (@q);
    FETCH NEXT FROM tbls INTO @db, @tbl;
END
CLOSE tbls;
DEALLOCATE tbls;
SELECT CAST(db + N'|' + tbl + N'|' + CAST(row_count AS nvarchar(20)) + N'|' + CAST(checksum_sum AS nvarchar(40)) AS nvarchar(400))
FROM #result ORDER BY db, tbl;
'@
    # The query travels base64-encoded in the command line: Windows PowerShell 5.1 can put a byte
    # order mark in front of anything piped to a native command's stdin, which breaks sqlcmd.
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($sql))
    $lines = @()
    foreach ($database in @($State.databases)) {
        $command = "echo $encoded | base64 -d > /tmp/qa-env-fingerprint.sql && SQLCMDPASSWORD=`$(cat $($database.passwordFile)) " +
            '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -i /tmp/qa-env-fingerprint.sql'
        $output = Get-ComposeOutput $State @('exec', '-T', $database.service, 'bash', '-c', $command)
        foreach ($row in ($output -split "`n")) {
            $row = $row.Trim()
            if ($row -match '^[^|]+\|[^|]+\|\d+\|-?\d+$') {
                $lines += "$($database.service)|$row"
            }
        }
    }
    return $lines
}

function Get-Sha256([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text))
    }
    finally {
        $sha.Dispose()
    }
    return (-join ($bytes | ForEach-Object { $_.ToString('x2') }))
}

try {
    if ($Commit -and $Action -ne 'up') {
        throw "Only 'up' takes a commit. 'reset' restarts the environment's own commit."
    }
    switch ($Action) {
        'up' {
            Assert-Prerequisites
            $base = $PortBase
            if (-not $base) {
                if ($Name -ne 'test') {
                    throw "-PortBase is required with -Name ${Name}: the default block belongs to the 'test' environment."
                }
                $webPort = 8080
                if ($env:RT_WEB_PORT) {
                    $webPort = [int]$env:RT_WEB_PORT
                }
                $base = $webPort + $DefaultPortOffset
            }
            $sha = Resolve-Commit $Commit
            Start-Environment $sha $base
        }
        'reset' {
            Assert-Prerequisites
            $state = Get-RequiredState
            Write-Step "Resetting to the seeded state of commit $($state.commit)"
            Start-Environment $state.commit ([int]$state.portBase)
        }
        'status' {
            $state = Get-RequiredState
            Assert-Prerequisites
            Show-Environment $state
            Invoke-Compose $state @('ps', '--all')
            Invoke-SmokeCheck $state
        }
        'fingerprint' {
            $state = Get-RequiredState
            Assert-Prerequisites
            $lines = @(Get-Fingerprint $state)
            if ($lines.Count -eq 0) {
                throw 'No tables found. Is the environment running? Check with: scripts/qa-env.ps1 status'
            }
            Write-Output "# QA environment '$Name' at $($state.commit): service|database|table|rows|checksum"
            $lines | Write-Output
            Write-Output "fingerprint $(Get-Sha256 ($lines -join "`n"))"
        }
        'logs' {
            $state = Get-RequiredState
            $arguments = @('logs', '--tail', [string]$Tail)
            if ($Service) {
                $arguments += $Service
            }
            Invoke-Compose $state $arguments
        }
        'down' {
            Assert-Prerequisites
            $state = Read-State
            Write-Step "Removing $Project"
            Remove-Environment $state
            if (Test-Path $StateDir) {
                Remove-Item -Recurse -Force $StateDir
            }
            $left = @(Get-Leftovers $state)
            if ($left.Count -gt 0) {
                throw "These are still there: $($left -join ', ')"
            }
            Write-Step 'Done: no containers, volumes, networks or images of the environment are left.'
        }
    }
    exit 0
}
catch {
    Write-Host "qa-env: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
