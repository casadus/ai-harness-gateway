param(
    [Parameter(Mandatory = $true)]
    [string] $Alias,
    [string] $ConfigPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'config/gateway.routes.example.json'),
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$')]
    [string] $RunId
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$configPath = (Resolve-Path -LiteralPath $ConfigPath).Path
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$route = @($config.routes | Where-Object { $_.alias -eq $Alias })
if ($route.Count -ne 1) {
    throw "Expected exactly one route with alias '$Alias'."
}
$route = $route[0]
$gatewayDll = Join-Path $repoRoot 'src/AiHarnessGateway/bin/Debug/net8.0/AiHarnessGateway.dll'
if (-not (Test-Path -LiteralPath $gatewayDll)) {
    throw 'Gateway build is missing. Run dotnet build .\AiHarnessGateway.sln first.'
}
if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw 'Codex CLI is not installed or is not on PATH.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is not installed or is not on PATH.'
}

$newEvaluation = Join-Path $PSScriptRoot 'new-evaluation.ps1'
$runRoot = if ([string]::IsNullOrWhiteSpace($RunId)) {
    & $newEvaluation
} else {
    & $newEvaluation -RunId $RunId
}
$runRoot = [string] $runRoot
$resultPath = Join-Path $runRoot 'result.json'
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result.runId = Split-Path -Leaf $runRoot
$result.dateUtc = [DateTime]::UtcNow.ToString('o')
$result.harness = 'codex'
$result.harnessVersion = (& codex --version) -join ' '
$result.provider = $route.provider
$result.model = $route.model
$result.modelTag = $route.model
$result.gatewayVersion = [System.Reflection.AssemblyName]::GetAssemblyName($gatewayDll).Version.ToString()

$timer = [System.Diagnostics.Stopwatch]::StartNew()
$harnessCompleted = $false
try {
    $prompt = Get-Content -LiteralPath (Join-Path $runRoot 'TASK.md') -Raw
    & (Join-Path $PSScriptRoot 'launch-codex.ps1') -Alias $Alias -ProjectPath $runRoot -ConfigPath $configPath -Prompt $prompt -Sandbox workspace-write
    $harnessCompleted = $true
}
catch {
    Write-Warning 'Codex did not complete. The result will record a failed run without storing the error text.'
}
finally {
    $timer.Stop()
    $result.durationMs = $timer.ElapsedMilliseconds
}

Push-Location -LiteralPath $runRoot
$checkExitCode = -1
try {
    & dotnet run --project .\Checks\Checks.csproj
    $checkExitCode = $LASTEXITCODE
}
catch {
    Write-Warning 'The C# check could not run. The result will record a failed check without storing the error text.'
}
finally {
    Pop-Location
}

$result.checkOutcome.exitCode = $checkExitCode
$result.checkOutcome.passed = ($checkExitCode -eq 0)
$result.harnessOutcome = if ($harnessCompleted) { 'completed' } else { 'failed' }
if (-not $harnessCompleted -or $checkExitCode -ne 0) {
    $result.success = $false
}

# Tool use and human intervention require review of the harness session.
# Leave these fields null rather than infer them from the final check alone.
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
Write-Output "Result: $resultPath"
