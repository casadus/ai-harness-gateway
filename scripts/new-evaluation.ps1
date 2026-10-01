param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$')]
    [string] $RunId = ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repoRoot 'fixtures/receipt-totals'
$runRoot = Join-Path $repoRoot (Join-Path '.local/evaluations' $RunId)

if (Test-Path -LiteralPath $runRoot) {
    throw "Evaluation run already exists: $runRoot"
}

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
Copy-Item -Path (Join-Path $fixtureRoot 'starter/*') -Destination $runRoot -Recurse
Copy-Item -LiteralPath (Join-Path $fixtureRoot 'TASK.md') -Destination $runRoot
Copy-Item -LiteralPath (Join-Path $fixtureRoot 'result.template.json') -Destination (Join-Path $runRoot 'result.json')
Write-Output $runRoot
