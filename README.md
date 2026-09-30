# AI Harness Gateway

AI Harness Gateway is a planned Windows-first environment for comparing AI coding harnesses, providers, and models independently. A gateway on each developer's machine will route supported harnesses to local models through Ollama or cloud models through OpenRouter.

```text
Codex CLI / Claude Code / OpenCode / GitHub Copilot CLI
                         |
                         v
              gateway on 127.0.0.1
                    /         \
                 Ollama     OpenRouter
              local model   cloud model
```

## Current status

This repository contains the [version 1 plan](PLAN.md), a [dated handoff](HANDOFF-2026-09-30.md), and an initial .NET gateway skeleton. The gateway currently supports loopback binding, explicit model-alias route resolution, safe health metadata, and pass-through POST handlers for:

- `POST /v1/responses`
- `POST /v1/chat/completions`
- `POST /v1/messages`

The installer, launch scripts, harness-specific configuration, and sample task have **not been implemented yet**. There is no installer to download or setup command to run at this stage.

Version 1 targets Windows 11 and one repeatable C# task across four harnesses and both providers. The intended distribution is a single setup wizard EXE in GitHub Releases. The installer will check prerequisites, offer each missing installation for approval, and include diagnostics and an uninstaller. The release will include a SHA-256 checksum.

## How a run will work

The planned `launch-agent.ps1` will let a developer choose a harness, provider, and configured model. It will show whether the route is **LOCAL** or **CLOUD**, start the local gateway, and launch the harness with settings scoped to that run. An evaluation result will record the exact harness, provider, model, task revision, tool behavior, outcome, and time.

For example, these are two distinct routes that the gateway is intended to support:

| Harness | Provider | Model | Source destination |
| --- | --- | --- | --- |
| Codex CLI | Ollama | `qwen3.5:9b` | Local machine |
| Codex CLI | OpenRouter | `anthropic/claude-sonnet-4.6` | Cloud provider |

The same local and cloud routes must also work with Claude Code, OpenCode, and GitHub Copilot CLI before the first release.

## Privacy and configuration

The initial task will use an included sample C# project, with no company code. An OpenRouter run can send the task prompt and source context to cloud services; a local Ollama run uses a local model tag and will disable optional web features during evaluation. Developers will see the route destination before launch.

API keys must not be committed or written to logs or result files. The gateway will read `OPENROUTER_API_KEY` only for a cloud run. The repository's `.gitignore` excludes local configuration, credentials, logs, results, and build artifacts; a release secret scan is also planned. Review files before committing because `.gitignore` does not protect secrets in files that are deliberately tracked.

## Development

The gateway project is in [src/AiHarnessGateway](src/AiHarnessGateway). A sample route file is provided at [config/gateway.routes.example.json](config/gateway.routes.example.json).
The repository pins the .NET SDK with [global.json](global.json).
Safe diagnostics are written as metadata-only newline-delimited JSON to `.local/logs/gateway.ndjson` by default, with size-based rotation.

Build locally:

```powershell
dotnet restore .\AiHarnessGateway.sln --ignore-failed-sources
dotnet build .\AiHarnessGateway.sln --no-restore
```

Run the gateway smoke test:

```powershell
.\scripts\test-gateway.ps1
```

The smoke test builds the solution, runs the dependency-free test runner in [tests/AiHarnessGateway.Tests](tests/AiHarnessGateway.Tests), starts the gateway, checks `/healthz`, verifies unknown aliases fail before upstream traffic, and stops the gateway.
It also runs [scripts/test-forwarding.ps1](scripts/test-forwarding.ps1), which uses [tests/FakeUpstream](tests/FakeUpstream) to verify model alias rewriting, inbound authorization stripping, tool payload forwarding, upstream status preservation, and streamed response copying through the gateway.

Run locally:

```powershell
dotnet run --project .\src\AiHarnessGateway\AiHarnessGateway.csproj --no-build --no-launch-profile -- --config "$PWD\config\gateway.routes.example.json"
```

Check health:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:5872/healthz
```

Cloud routes read the OpenRouter key from `OPENROUTER_API_KEY` in the gateway process only. Do not put keys in route files, command arguments, logs, or result files.

See [PLAN.md](PLAN.md) for the ordered implementation and release tasks. This public repository is the source of truth for the gateway and installer. Installation and usage commands will be added here as soon as the first working release exists.
