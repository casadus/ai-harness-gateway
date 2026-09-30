param(
    [int] $GatewayPort = 5873,
    [int] $UpstreamPort = 5874
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$repoRoot = Split-Path -Parent $PSScriptRoot
$gatewayDll = Join-Path $repoRoot 'src/AiHarnessGateway/bin/Debug/net8.0/AiHarnessGateway.dll'
$upstreamDll = Join-Path $repoRoot 'tests/FakeUpstream/bin/Debug/net8.0/FakeUpstream.dll'
$tempRoot = Join-Path $repoRoot '.local/test-forwarding'
$configPath = Join-Path $tempRoot 'gateway.routes.json'
$gatewayUrl = "http://127.0.0.1:$GatewayPort"
$upstreamUrl = "http://127.0.0.1:$UpstreamPort"

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

function Start-DotnetProcess {
    param(
        [string[]] $Arguments
    )

    $processInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = 'dotnet'
    $processInfo.WorkingDirectory = $repoRoot
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true

    $processInfo.Arguments = ($Arguments | ForEach-Object { Format-ProcessArgument $_ }) -join ' '

    return [System.Diagnostics.Process]::Start($processInfo)
}

function Format-ProcessArgument {
    param(
        [string] $Argument
    )

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    return '"' + ($Argument -replace '\\', '\\' -replace '"', '\"') + '"'
}

function Assert-ProcessRunning {
    param(
        [System.Diagnostics.Process] $Process,
        [string] $Name
    )

    if ($Process.HasExited) {
        $stdout = $Process.StandardOutput.ReadToEnd()
        $stderr = $Process.StandardError.ReadToEnd()
        throw "$Name exited early with code $($Process.ExitCode). Stdout: $stdout Stderr: $stderr"
    }
}

function Wait-Http {
    param(
        [string] $Url,
        [int] $TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-WebRequest -Uri $Url -TimeoutSec 2 -UseBasicParsing | Out-Null
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Endpoint did not respond at $Url within $TimeoutSeconds seconds."
}

function Invoke-JsonPost {
    param(
        [string] $Uri,
        [string] $Json,
        [hashtable] $Headers = @{}
    )

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $Uri)
        $request.Content = [System.Net.Http.StringContent]::new($Json, [System.Text.Encoding]::UTF8, 'application/json')
        foreach ($key in $Headers.Keys) {
            $request.Headers.TryAddWithoutValidation($key, [string] $Headers[$key]) | Out-Null
        }

        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{
            StatusCode = [int] $response.StatusCode
            ContentType = $response.Content.Headers.ContentType.ToString()
            Body = $body
        }
    }
    finally {
        $client.Dispose()
    }
}

if (Test-Path -LiteralPath $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null

$config = @{
    listenUrl = $gatewayUrl
    diagnostics = @{
        logFilePath = (Join-Path $tempRoot 'gateway.ndjson')
        maxLogBytes = 1048576
        retainedLogFiles = 1
    }
    routes = @(
        @{
            alias = 'test-alias'
            provider = 'fake'
            protocol = 'openai'
            model = 'upstream-model'
            baseUrl = $upstreamUrl
        }
    )
} | ConvertTo-Json -Depth 8

Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8

$upstream = Start-DotnetProcess -Arguments @($upstreamDll, "$UpstreamPort")

$gateway = $null

try {
    Start-Sleep -Milliseconds 500
    Assert-ProcessRunning -Process $upstream -Name 'Fake upstream'
    Wait-Http -Url $upstreamUrl

    $gateway = Start-DotnetProcess -Arguments @($gatewayDll, '--config', $configPath)

    Start-Sleep -Milliseconds 500
    Assert-ProcessRunning -Process $gateway -Name 'Gateway'
    Wait-Http -Url "$gatewayUrl/healthz"

    $toolJson = '{"model":"test-alias","messages":[{"role":"user","content":"hi"}],"tools":[{"type":"function","function":{"name":"sample_tool","parameters":{"type":"object"}}}]}'
    $toolResponse = Invoke-JsonPost -Uri "$gatewayUrl/v1/chat/completions" -Json $toolJson -Headers @{ Authorization = 'Bearer should-not-forward' }
    if ($toolResponse.StatusCode -ne 202) {
        throw "Expected upstream status 202 but received $($toolResponse.StatusCode). Body: $($toolResponse.Body)"
    }

    $toolBody = $toolResponse.Body | ConvertFrom-Json
    if ($toolBody.path -ne '/v1/chat/completions') {
        throw "Expected upstream path /v1/chat/completions but received '$($toolBody.path)'."
    }

    if ($toolBody.model -ne 'upstream-model') {
        throw "Expected model rewrite to upstream-model but received '$($toolBody.model)'."
    }

    if ($null -ne $toolBody.authorization) {
        throw "Expected gateway to strip inbound Authorization, but upstream received '$($toolBody.authorization)'."
    }

    if ($toolBody.firstToolName -ne 'sample_tool') {
        throw "Expected tool payload to reach upstream, but received '$($toolBody.firstToolName)'."
    }

    $streamJson = '{"model":"test-alias","stream":true,"messages":[{"role":"user","content":"stream"}]}'
    $streamResponse = Invoke-JsonPost -Uri "$gatewayUrl/v1/chat/completions" -Json $streamJson
    if ($streamResponse.StatusCode -ne 200) {
        throw "Expected streamed response status 200 but received $($streamResponse.StatusCode)."
    }

    if ($streamResponse.Body -notmatch 'data: one' -or $streamResponse.Body -notmatch 'data: two') {
        throw "Expected streamed response chunks were not copied. Body: $($streamResponse.Body)"
    }

    Write-Host 'Gateway forwarding test passed.'
}
finally {
    foreach ($process in @($gateway, $upstream)) {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
}
