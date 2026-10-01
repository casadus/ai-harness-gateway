param(
    [Parameter(Mandatory = $true)]
    [string] $Alias,
    [string] $ProjectPath = (Get-Location).Path,
    [string] $ConfigPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'config/gateway.routes.example.json'),
    [string] $Prompt,
    [ValidateSet('read-only', 'workspace-write')]
    [string] $Sandbox = 'workspace-write'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$gatewayDll = Join-Path $repoRoot 'src/AiHarnessGateway/bin/Debug/net8.0/AiHarnessGateway.dll'
$configPath = (Resolve-Path -LiteralPath $ConfigPath).Path
$projectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$route = @($config.routes | Where-Object { $_.alias -eq $Alias })

if ($route.Count -ne 1) {
    throw "Expected exactly one route with alias '$Alias'."
}

if (-not (Test-Path -LiteralPath $gatewayDll)) {
    throw "Gateway build is missing. Run dotnet build .\AiHarnessGateway.sln first."
}

if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw 'Codex CLI is not installed or is not on PATH.'
}

$listenUri = [Uri] $config.listenUrl
if ($listenUri.Host -ne '127.0.0.1') {
    throw 'Gateway must listen on 127.0.0.1.'
}

$route = $route[0]
$destination = if ($route.provider -eq 'ollama') { 'LOCAL' } elseif ($route.provider -eq 'openrouter') { 'CLOUD' } else { 'CUSTOM' }
if ($route.provider -eq 'openrouter' -and [string]::IsNullOrWhiteSpace($env:OPENROUTER_API_KEY)) {
    throw 'OPENROUTER_API_KEY is required for this cloud route.'
}

if ($route.provider -eq 'ollama') {
    & ollama show $route.model | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Local Ollama model '$($route.model)' is unavailable. Run: ollama pull $($route.model)"
    }
}

Write-Host "$destination route: $Alias -> $($route.provider) / $($route.model)"
Write-Host "Project: $projectPath"

$codexHome = Join-Path $repoRoot '.local/codex-home'
New-Item -ItemType Directory -Force -Path $codexHome | Out-Null
$codexConfig = @"
model_provider = "gateway"

[model_providers.gateway]
name = "AI Harness Gateway"
base_url = "$($config.listenUrl.TrimEnd('/'))/v1"
wire_api = "responses"
requires_openai_auth = false
"@
Set-Content -LiteralPath (Join-Path $codexHome 'config.toml') -Value $codexConfig -Encoding UTF8

$processInfo = [System.Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = 'dotnet'
$processInfo.WorkingDirectory = $repoRoot
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.Arguments = '"{0}" --config "{1}"' -f $gatewayDll, $configPath
$gateway = [System.Diagnostics.Process]::Start($processInfo)
$gateway.BeginOutputReadLine()
$gateway.BeginErrorReadLine()
$previousCodexHome = $env:CODEX_HOME
$previousOpenRouterKey = $env:OPENROUTER_API_KEY

try {
    Start-Sleep -Milliseconds 500
    if ($gateway.HasExited) {
        throw "Gateway exited with code $($gateway.ExitCode). Check the gateway config and listen port."
    }

    $healthUrl = "$($config.listenUrl.TrimEnd('/'))/healthz"
    $healthy = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 2
            if ($health.status -eq 'ok') {
                $healthy = $true
                break
            }
        }
        catch {
            if ($gateway.HasExited) { break }
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $healthy -or $gateway.HasExited) {
        throw "Gateway did not start at $healthUrl. Check the gateway config and listen port."
    }

    $env:CODEX_HOME = $codexHome
    $env:OPENROUTER_API_KEY = $null
    if ([string]::IsNullOrWhiteSpace($Prompt)) {
        & codex -C $projectPath -m $Alias -s $Sandbox
    }
    else {
        & codex exec --ephemeral --skip-git-repo-check -C $projectPath -m $Alias -s $Sandbox $Prompt
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Codex exited with code $LASTEXITCODE."
    }
}
finally {
    $env:CODEX_HOME = $previousCodexHome
    $env:OPENROUTER_API_KEY = $previousOpenRouterKey
    if ($gateway -and -not $gateway.HasExited) {
        $gateway.Kill()
        $gateway.WaitForExit()
    }
}
