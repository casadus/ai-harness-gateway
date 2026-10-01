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

This repository contains the [version 1 plan](PLAN.md), a [current handoff](HANDOFF-2026-10-01.md), and an initial .NET gateway skeleton. The gateway currently supports loopback binding, explicit model-alias route resolution, safe health metadata, and pass-through POST handlers for:

- `POST /v1/responses`
- `POST /v1/chat/completions`
- `POST /v1/messages`

The Codex launch helper and a disposable C# sample task are available for source development. The full multi-harness launcher, installer, and live eight-path evaluation have **not been implemented yet**. There is no installer to download or setup command to run at this stage.

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

Run the launcher preview:

```powershell
dotnet run --project .\src\AiHarnessGateway.Launcher\AiHarnessGateway.Launcher.csproj -- --config .\config\gateway.routes.example.json
```

The launcher preview is intended to become the shortcut target. It shows installed harness commands, configured model aliases, Ollama service/model status, OpenRouter key status, and setup hints.

For an initial Codex-only process-scoped launch after building the gateway:

```powershell
.\scripts\launch-codex.ps1 -Alias local-qwen -ProjectPath C:\path\to\project
```

The helper creates an isolated Codex home under `.local`, starts the gateway for the Codex session, and displays the selected route. Add `-Prompt '...'` for a non-interactive run. The local route requires its Ollama model to be installed; the cloud route requires `OPENROUTER_API_KEY` in the launching shell. Neither route has completed a live coding evaluation yet.

Create a fresh disposable evaluation task:

```powershell
.\scripts\new-evaluation.ps1
```

The command prints the new directory under `.local/evaluations`. Give the harness the `TASK.md` in that directory and record the run in its `result.json`. The untouched task check fails; fixing the subtotal calculation makes all three checks pass.

For a Codex evaluation, create the task, launch Codex, run the check, and write safe result metadata in one command:

```powershell
.\scripts\run-codex-evaluation.ps1 -Alias local-qwen
```

The result leaves tool behavior, human intervention, and overall success for review. It records failure when Codex does not complete or the C# check fails. The runner's successful exit means it wrote the result, not that the harness passed the task.

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
