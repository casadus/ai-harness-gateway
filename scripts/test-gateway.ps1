param(
    [int] $Port = 5872
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'AiHarnessGateway.sln'
$gatewayDll = Join-Path $repoRoot 'src/AiHarnessGateway/bin/Debug/net8.0/AiHarnessGateway.dll'
$configPath = Join-Path $repoRoot 'config/gateway.routes.example.json'
$gatewayUrl = "http://127.0.0.1:$Port"

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Wait-Gateway {
    param(
        [string] $HealthUrl,
        [int] $TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            return Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 2
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Gateway did not become healthy at $HealthUrl within $TimeoutSeconds seconds."
}

function Invoke-GatewayPost {
    param(
        [string] $Uri,
        [string] $Json
    )

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    try {
        $content = [System.Net.Http.StringContent]::new($Json, [System.Text.Encoding]::UTF8, 'application/json')
        $response = $client.PostAsync($Uri, $content).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{
            StatusCode = [int] $response.StatusCode
            Body = $body
        }
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

Invoke-Native dotnet restore $solutionPath --ignore-failed-sources
Invoke-Native dotnet build $solutionPath --no-restore
Invoke-Native dotnet run --project (Join-Path $repoRoot 'tests/AiHarnessGateway.Tests/AiHarnessGateway.Tests.csproj') --no-build

$launcherOutput = & dotnet run --project (Join-Path $repoRoot 'src/AiHarnessGateway.Launcher/AiHarnessGateway.Launcher.csproj') --no-build -- --config $configPath --no-pause
if ($LASTEXITCODE -ne 0) {
    throw "Launcher exited with code $LASTEXITCODE."
}

foreach ($expectedText in @('AI Harness Gateway', 'Harnesses', 'Configured model aliases', 'local-qwen', 'cloud-claude', 'Ollama', 'OpenRouter')) {
    if (($launcherOutput -join "`n") -notmatch [regex]::Escape($expectedText)) {
        throw "Launcher output did not include expected text: $expectedText"
    }
}

Invoke-Native -FilePath powershell -Arguments @(
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    (Join-Path $repoRoot 'scripts/test-forwarding.ps1')
)

$processInfo = [System.Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = 'dotnet'
$processInfo.WorkingDirectory = $repoRoot
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.Arguments = '"{0}" --config "{1}"' -f $gatewayDll, $configPath
$gateway = [System.Diagnostics.Process]::Start($processInfo)

try {
    Start-Sleep -Milliseconds 500
    if ($gateway.HasExited) {
        throw "Gateway exited early with code $($gateway.ExitCode). Stdout: $($gateway.StandardOutput.ReadToEnd()) Stderr: $($gateway.StandardError.ReadToEnd())"
    }

    $health = Wait-Gateway -HealthUrl "$gatewayUrl/healthz"
    if ($health.status -ne 'ok') {
        throw "Expected health status 'ok' but received '$($health.status)'."
    }

    $routeAliases = @($health.routes | ForEach-Object { $_.alias })
    foreach ($expectedAlias in @('local-qwen', 'cloud-claude')) {
        if ($routeAliases -notcontains $expectedAlias) {
            throw "Expected route alias '$expectedAlias' was not reported by /healthz."
        }
    }

    $badAliasJson = '{"model":"missing-alias","messages":[{"role":"user","content":"hi"}]}'
    $badAliasResponse = Invoke-GatewayPost -Uri "$gatewayUrl/v1/chat/completions" -Json $badAliasJson
    if ($badAliasResponse.StatusCode -ne 400) {
        throw "Expected unknown alias response 400 but received $($badAliasResponse.StatusCode)."
    }

    if ($badAliasResponse.Body -notmatch "Unknown model alias 'missing-alias'") {
        throw "Unexpected unknown alias response body: $($badAliasResponse.Body)"
    }

    Write-Host 'Gateway smoke test passed.'
}
finally {
    if ($gateway -and -not $gateway.HasExited) {
        Stop-Process -Id $gateway.Id -Force
        $gateway.WaitForExit()
    }
}
