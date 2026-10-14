<#
.SYNOPSIS
    End-to-end test of the QA test environment, scripts/qa-env.ps1 (RT-121).

.DESCRIPTION
    Starts a commit the way QA starts main and checks, in order:
      1. a commit that isn't on origin/main is refused (skipped when the commit is on main);
      2. a port block that is in use is refused before anything is created;
      3. up: the smoke check passes and /api/system/info shows the commit and Test;
      4. isolation: only the gateway publishes ports, on 127.0.0.1 only; every other container
         is on the internal network only and can't reach the internet;
      5. reset: data added after up is gone and the fingerprint equals the first one;
      6. nothing that git would commit was created, the secrets are gitignored, and the
         developer clone's user-secrets are untouched;
      7. down: no containers, volumes, networks, images or state folder are left.

    It uses its own environment, named 'e2e', on its own port block, so a QA environment in the
    same clone is left alone. CI runs it from .github/workflows/qa-env.yml.

.PARAMETER Commit
    The commit to start. Default: HEAD, which needn't be on main (the test passes -AnyCommit).

.PARAMETER PortBase
    The first of the test environment's 10 ports. Default: RT_WEB_PORT + 700, or 8780.
#>
[CmdletBinding()]
param(
    [string]$Commit = 'HEAD',
    [int]$PortBase
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Tool = Join-Path $RepoRoot 'scripts/qa-env.ps1'
$Shell = (Get-Process -Id $PID).Path
$Name = 'e2e'
$BaseProject = if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { 'reporting-tool' }
$Project = "$BaseProject-$Name"
$StateDir = Join-Path (Join-Path $RepoRoot '.qa-env') $Name
if (-not $PortBase) {
    $webPort = 8080
    if ($env:RT_WEB_PORT) {
        $webPort = [int]$env:RT_WEB_PORT
    }
    $PortBase = $webPort + 700
}
$WebUrl = "http://127.0.0.1:$PortBase"

function Write-Check([string]$Message) {
    Write-Host "  ok  $Message" -ForegroundColor Green
}

function Assert-That([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "FAILED: $Message"
    }
    Write-Check $Message
}

# Runs the tool in a new process, as QA does, and returns its exit code; its output goes to the
# console. -Capture returns the output lines instead (and the exit code in $script:ToolExit).
function Invoke-Tool([string[]]$Arguments, [switch]$Capture) {
    $shellArguments = @('-NoProfile')
    if ($env:OS -eq 'Windows_NT') {
        $shellArguments += @('-ExecutionPolicy', 'Bypass')
    }
    $shellArguments += @('-File', $Tool) + $Arguments + @('-Name', $Name)
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($Capture) {
            $lines = @(& $Shell @shellArguments)
            $script:ToolExit = $LASTEXITCODE
            return $lines
        }
        & $Shell @shellArguments | Out-Host
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $saved
    }
}

function Get-DockerLines([string[]]$Arguments) {
    $output = & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    return @($output | Where-Object { $_ -and $_.Trim() })
}

function Get-Fingerprint {
    $lines = Invoke-Tool @('fingerprint') -Capture
    Assert-That ($script:ToolExit -eq 0) 'fingerprint exits with 0'
    return ($lines -join "`n")
}

# The developer clone's user-secrets file, hashed so the test never prints a secret.
function Get-UserSecretsHash {
    $project = [xml](Get-Content -Raw (Join-Path $RepoRoot 'src/Services/ReportingTool.Api/ReportingTool.Api.csproj'))
    $id = @($project.Project.PropertyGroup | ForEach-Object { $_.UserSecretsId } | Where-Object { $_ })[0]
    if (-not $id) {
        return 'no UserSecretsId'
    }
    if ($env:OS -eq 'Windows_NT') {
        $file = Join-Path $env:APPDATA "Microsoft/UserSecrets/$id/secrets.json"
    }
    else {
        $file = Join-Path $HOME ".microsoft/usersecrets/$id/secrets.json"
    }
    if (-not (Test-Path $file)) {
        return 'absent'
    }
    return (Get-FileHash -Algorithm SHA256 -Path $file).Hash
}

$sha = (& git -C $RepoRoot rev-parse --verify "$Commit^{commit}").Trim()
Write-Host "QA environment end-to-end test: commit $sha, project $Project, ports $PortBase-$($PortBase + 9)"
$gitStatusBefore = (& git -C $RepoRoot status --porcelain) -join "`n"
$userSecretsBefore = Get-UserSecretsHash
$passed = $false

try {
    # Leftovers of an aborted earlier run.
    $null = Invoke-Tool @('down')

    Write-Host 'Refusals'
    & git -C $RepoRoot fetch --quiet origin
    & git -C $RepoRoot merge-base --is-ancestor $sha origin/main
    if ($LASTEXITCODE -ne 0) {
        $code = Invoke-Tool @('up', $sha, '-PortBase', [string]$PortBase, '-NoFetch')
        Assert-That ($code -ne 0) 'up refuses a commit that is not on origin/main'
        Assert-That (-not (Test-Path $StateDir)) '... and creates nothing'
    }
    else {
        Write-Host "  --  $sha is on origin/main, so the not-on-main refusal is not tested"
    }

    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, ($PortBase + 9))
    $listener.Start()
    try {
        $code = Invoke-Tool @('up', $sha, '-PortBase', [string]$PortBase, '-AnyCommit', '-NoFetch')
    }
    finally {
        $listener.Stop()
    }
    Assert-That ($code -ne 0) 'up refuses a port block that is in use'
    Assert-That (-not (Test-Path $StateDir)) '... and creates nothing'
    Assert-That (@(Get-DockerLines @('container', 'ls', '--all', '--quiet', '--filter', "label=com.docker.compose.project=$Project")).Count -eq 0) '... and starts no container'

    Write-Host 'Up'
    $code = Invoke-Tool @('up', $sha, '-PortBase', [string]$PortBase, '-AnyCommit', '-NoFetch')
    Assert-That ($code -eq 0) 'up exits with 0 (smoke check passed)'
    $info = Invoke-RestMethod -UseBasicParsing -Uri "$WebUrl/api/system/info" -TimeoutSec 10
    Assert-That (([string]$info.version).EndsWith("+$sha")) "/api/system/info shows the commit ($($info.version))"
    Assert-That ($info.environment -eq 'Test') '/api/system/info shows the Test environment'

    Write-Host 'Isolation'
    $internalNetwork = "${Project}_qa-internal"
    # JSON rather than a Go template: Windows PowerShell 5.1 can't pass the template's quotes.
    $containers = @(Get-DockerLines @('container', 'ls', '--filter', "label=com.docker.compose.project=$Project",
        '--format', 'json') | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($containers.Count -ge 4) "$($containers.Count) containers are running"
    foreach ($container in $containers) {
        $service = ([regex]::Match($container.Labels, 'com\.docker\.compose\.service=([^,]+)')).Groups[1].Value
        $networks = $container.Networks
        $published = @($container.Ports -split ',\s*' | Where-Object { $_ -match '->' })
        if ($service -eq 'qa-gateway') {
            Assert-That ($published.Count -gt 0) 'the gateway publishes the ports'
            foreach ($mapping in $published) {
                Assert-That ($mapping.StartsWith('127.0.0.1:')) "the gateway publishes $mapping on 127.0.0.1 only"
            }
        }
        else {
            Assert-That ($published.Count -eq 0) "$service publishes no port itself"
            Assert-That ($networks -eq $internalNetwork) "$service is on the internal network only"
        }
    }
    $internal = @(Get-DockerLines @('network', 'inspect', $internalNetwork, '--format', '{{.Internal}}'))[0]
    Assert-That ($internal -eq 'true') "$internalNetwork is an internal network (no route out)"
    $sqlContainer = @(Get-DockerLines @('container', 'ls', '--quiet', '--filter', "label=com.docker.compose.project=$Project",
        '--filter', 'label=com.docker.compose.service=sqlserver'))[0]
    $egress = @(Get-DockerLines @('exec', $sqlContainer, 'bash', '-c',
        "timeout 5 bash -c 'exec 3<>/dev/tcp/1.1.1.1/443' 2>/dev/null && echo open || echo blocked"))[-1]
    Assert-That ($egress -eq 'blocked') 'SQL Server cannot open a connection to the internet'

    Write-Host 'Reset'
    $first = Get-Fingerprint
    Assert-That ($first -match '\|\[dbo\]\.\[Greetings\]\|1\|') 'the seeded data has one greeting'
    $body = '{"message":"left over by the e2e test"}'
    $null = Invoke-RestMethod -UseBasicParsing -Method Post -Uri "$WebUrl/api/greetings" -ContentType 'application/json' -Body $body -TimeoutSec 10
    $changed = Get-Fingerprint
    Assert-That ($changed -ne $first) 'the fingerprint changes when data is added'
    $code = Invoke-Tool @('reset')
    Assert-That ($code -eq 0) 'reset exits with 0 (smoke check passed)'
    $afterReset = Get-Fingerprint
    Assert-That ($afterReset -eq $first) 'after reset the fingerprint equals the first run (same seeded data)'
    $greetings = @(Invoke-RestMethod -UseBasicParsing -Uri "$WebUrl/api/greetings" -TimeoutSec 10)
    Assert-That (-not ($greetings | Where-Object { $_.message -like '*e2e test*' })) 'the greeting added before reset is gone'
    $code = Invoke-Tool @('status')
    Assert-That ($code -eq 0) 'status exits with 0'

    Write-Host 'Secrets'
    & git -C $RepoRoot check-ignore --quiet ".qa-env/$Name/src/.secrets/sql_sa_password"
    Assert-That ($LASTEXITCODE -eq 0) 'the generated secrets are gitignored'
    $gitStatusAfter = (& git -C $RepoRoot status --porcelain) -join "`n"
    Assert-That ($gitStatusAfter -eq $gitStatusBefore) 'git status is unchanged: nothing to commit'
    Assert-That ((Get-UserSecretsHash) -eq $userSecretsBefore) 'the developer user-secrets are untouched'

    Write-Host 'Down'
    $code = Invoke-Tool @('down')
    Assert-That ($code -eq 0) 'down exits with 0'
    foreach ($kind in @('container', 'volume', 'network')) {
        $listArguments = @($kind, 'ls', '--quiet', '--filter', "label=com.docker.compose.project=$Project")
        if ($kind -eq 'container') {
            $listArguments = @('container', 'ls', '--all', '--quiet', '--filter', "label=com.docker.compose.project=$Project")
        }
        Assert-That (@(Get-DockerLines $listArguments).Count -eq 0) "no $kind of $Project is left"
    }
    Assert-That (@(Get-DockerLines @('image', 'ls', '--quiet', "$Project-*")).Count -eq 0) "no image of $Project is left"
    Assert-That (-not (Test-Path $StateDir)) "no .qa-env/$Name folder is left"
    $code = Invoke-Tool @('status')
    Assert-That ($code -ne 0) 'status reports that there is no environment'

    $passed = $true
    Write-Host 'QA environment end-to-end test PASSED' -ForegroundColor Green
}
finally {
    if (-not $passed) {
        Write-Host 'QA environment end-to-end test FAILED; the logs and a clean-up follow.' -ForegroundColor Red
        if (Test-Path $StateDir) {
            $null = Invoke-Tool @('logs', '-Tail', '60')
        }
        $null = Invoke-Tool @('down')
    }
}
