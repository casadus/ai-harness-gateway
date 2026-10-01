# AI Harness Gateway: Windows v1 plan

## Objective and acceptance

Run a gateway on each developer's Windows 11 machine so coding harnesses, providers, and models can be used for regular coding work and evaluated separately when needed. The gateway listens on `127.0.0.1` and maps explicit model aliases to either a local Ollama model or an OpenRouter cloud model. It records the selected harness, provider, model, and task without storing source content.

The default experience should be easy to start from Windows: install once, open an app shortcut, choose a harness, choose local or cloud, choose a model, choose a project folder, and start working. PowerShell scripts and direct commands must remain available for automation, diagnostics, and source development, but they should not be required for normal daily use.

Version 1 supports these eight paths through the gateway:

| Harness | Ollama local model | OpenRouter cloud model |
| --- | --- | --- |
| Codex CLI | Required | Required |
| Claude Code | Required | Required |
| OpenCode | Required | Required |
| GitHub Copilot CLI | Required | Required |

Each path must complete the same included C# task: read files, make a change, use its tools, run the relevant check, and summarize the result. A text-only completion is insufficient. Start with `qwen3.5:9b` as the 16 GB RAM local candidate and `anthropic/claude-sonnet-4.6` as the cloud candidate. Record exact model tags, configured context, and tool failures. If a path fails, diagnose it before claiming support.

## Gateway and launch slices

- [x] Slice 1: Gateway scaffold
  - [x] Create the .NET gateway solution and project in `src/AiHarnessGateway`.
  - [x] Pin the repository to the .NET 8 SDK with `global.json`.
  - [x] Bind the gateway only to an absolute `127.0.0.1` URL.
  - [x] Add route configuration loading from `--config` or `AI_HARNESS_GATEWAY_CONFIG`.
  - [x] Add the example route file at `config/gateway.routes.example.json`.
  - [x] Add metadata-only `/healthz` output.
  - [x] Build successfully with `dotnet build .\AiHarnessGateway.sln --no-restore`.
  - [x] Add `scripts/test-gateway.ps1` for repeatable local smoke verification.

- [x] Slice 2: Route resolution and safe diagnostics
  - [x] Map stable aliases `local-qwen` and `cloud-claude` to exact upstream model IDs.
  - [x] Reject unknown aliases before upstream traffic.
  - [x] Keep provider, model, and route metadata separate in diagnostics.
  - [x] Avoid logging prompts, responses, headers, source text, or credentials.
  - [x] Persist safe diagnostics to a local log file with rotation.
  - [x] Add tests for duplicate aliases, invalid loopback URLs, missing model aliases, and missing cloud keys.

- [ ] Slice 3: Protocol forwarding
  - [x] Add pass-through handlers for `POST /v1/responses`, `POST /v1/chat/completions`, and `POST /v1/messages`.
  - [x] Rewrite only the request `model` field from gateway alias to upstream model ID.
  - [x] Strip inbound client `Authorization` before forwarding.
  - [x] Read OpenRouter authentication from `OPENROUTER_API_KEY` only in the gateway process.
  - [x] Prove model rewrite, authorization stripping, upstream status preservation, tool payload forwarding, and streamed response copying with a local fake upstream.
  - [x] Preserve `/v1` exactly once when an upstream base URL already ends in `/v1`, including nested `/api/v1` URLs.
  - [ ] Preserve streamed upstream responses under real harness traffic.
  - [ ] Preserve tool-call payloads under real harness traffic.
  - [x] Preserve client cancellation and useful upstream error status in fake-upstream tests.
  - [ ] Add discovery endpoints only if observed harness integration requires them.

- [ ] Slice 4: Codex first path
  - [x] Configure Codex with an isolated user-level `CODEX_HOME` for a process-scoped launch.
  - [x] Verify Codex sends OpenAI Responses traffic through the gateway using a fake upstream.
  - [ ] Run Codex against `local-qwen`.
  - [ ] Run Codex against `cloud-claude`.
  - [ ] Confirm normal user Codex settings are unaffected.

  The installed Codex CLI 0.159.3 completed a fake-upstream Responses request through the gateway on 2026-10-01. This proves the route and process-scoped provider setup; it does not prove model quality or tool use. The target local model is not installed, the OpenRouter key is unset, and this machine has about 2.3 GB free, so the two live Codex paths remain unverified here.

- [ ] Slice 5: Remaining harness paths
  - [ ] Configure Claude Code against the Anthropic-compatible Messages path.
  - [ ] Run Claude Code against `local-qwen`.
  - [ ] Run Claude Code against `cloud-claude`.
  - [ ] Configure OpenCode against the OpenAI Chat Completions path.
  - [ ] Run OpenCode against `local-qwen`.
  - [ ] Run OpenCode against `cloud-claude`.
  - [ ] Configure GitHub Copilot CLI BYOK against the OpenAI Chat Completions path.
  - [ ] Run GitHub Copilot CLI against `local-qwen`.
  - [ ] Run GitHub Copilot CLI against `cloud-claude`.

- [ ] Slice 6: Launch script
  - [ ] Add `launch-agent.ps1` to select harness, provider, model, and task.
  - [ ] Support regular work mode as the default flow: select harness, provider, model, and project folder, then open the harness ready to work.
  - [ ] Support evaluation mode for fixed fixture and matrix runs.
  - [ ] Start the gateway for the duration of a run.
  - [ ] Apply all harness settings process-scoped.
  - [ ] Clearly show LOCAL or CLOUD before launch.
  - [ ] Remove `OPENROUTER_API_KEY` from harness child environments.
  - [ ] Disable optional web features during local evaluation.
  - [ ] Verify the Ollama tag is installed locally rather than an Ollama cloud tag.

- [ ] Slice 7: Interactive launcher and registries
  - [x] Add a Windows-friendly launcher entry point that can be started from a shortcut without opening PowerShell manually.
  - [x] Show installed harnesses for Codex CLI, Claude Code, OpenCode, and GitHub Copilot CLI.
  - [ ] Allow enabling or disabling Codex CLI, Claude Code, OpenCode, and GitHub Copilot CLI.
  - [ ] Show clear install guidance for missing harnesses.
  - [x] Show configured model aliases grouped by provider.
  - [x] Show installed Ollama models from the local Ollama service.
  - [x] Show recommended Ollama models and the exact `ollama pull ...` command when a model is missing.
  - [x] Show configured OpenRouter models and whether `OPENROUTER_API_KEY` is available.
  - [x] Provide OpenRouter model setup guidance, including where to get a key and where to choose model IDs.
  - [x] Make cloud routing and possible provider cost/privacy implications visible before launch.
  - [ ] Let the user add, remove, or edit model aliases without editing JSON by hand.
  - [ ] Let the user add, remove, enable, or disable harness launch options without editing JSON by hand.
  - [ ] Keep command-line equivalents available for every launcher action.

## Installer, documentation, and security slices

- [ ] Slice 8: Fixture and result format
  - [x] Create one disposable C# fixture with no company code.
  - [x] Add a fixed prompt, expected observable result, and fresh-copy reset procedure.
  - [x] Add a structured result template with date, task revision, harness/version, provider, model/tag, gateway/version, context, success, tool behavior, check outcome, duration, intervention, and notes.
  - [x] Record Codex run metadata, duration, and check outcome without storing prompts or source text.
  - [ ] Start every comparison from the same fixture revision.

- [ ] Slice 9: Diagnostics
  - [ ] Add `diagnostics.ps1`.
  - [x] Add a gateway health check endpoint.
  - [ ] Report tool versions, configured route names, provider reachability, and recent categorized errors.
  - [ ] Confirm diagnostics exclude prompts, responses, source text, headers, and credentials.
  - [ ] Check missing-tool, stopped-Ollama, missing-model, missing-key, upstream-error, occupied-port, and interrupted-stream behavior.

- [ ] Slice 10: Security and repository hygiene
  - [x] Add `.gitignore` coverage for keys, `.env`, local config, logs, results, generated state, build output, IDE files, and installer artifacts.
  - [x] Keep the OpenRouter key out of route files and docs examples.
  - [ ] Add a release secret scan.
  - [ ] Inspect release contents before publishing.

- [ ] Slice 11: Installer
  - [ ] Publish a single `win-x64` setup wizard EXE as a public GitHub Release asset.
  - [ ] Bundle the self-contained gateway, Windows launcher, launch and diagnostic scripts, and C# fixture.
  - [ ] Use a per-user Inno Setup installation and Windows uninstaller.
  - [ ] Install gateway-owned files under the user's application directory.
  - [ ] Install a Start Menu shortcut named `AI Harness Gateway`.
  - [ ] Optionally install a Desktop shortcut.
  - [ ] Include and install a recognizable application icon for the launcher and shortcuts.
  - [ ] Ensure the shortcut opens the regular work launcher, not a raw PowerShell prompt.
  - [ ] Keep PowerShell scripts installed and documented for advanced use.
  - [ ] Keep mutable logs, results, and configuration in separate per-user data directories.
  - [ ] Check Windows version, memory, disk, Ollama, Ollama service/model, all four harness commands, and the .NET SDK needed by the sample.
  - [ ] Offer each missing prerequisite installation separately after approval.
  - [ ] Provide manual instructions when an automatic installer is unavailable.
  - [ ] Make setup safe to rerun.
  - [ ] Uninstall gateway-owned files and generated state, with an explicit choice for retaining results.
  - [ ] Remove installed shortcuts and icon assets during uninstall.
  - [ ] Leave independently installed harnesses, Ollama, and model weights in place.

- [ ] Slice 12: README and release docs
  - [x] Document current source build and local gateway run commands.
  - [ ] Document one-file installation after the installer exists.
  - [ ] Document shortcut-based regular work startup after the launcher exists.
  - [ ] Document PowerShell launch examples after `launch-agent.ps1` exists.
  - [ ] Document how to add/remove harness options and model aliases.
  - [ ] Document how to discover installed Ollama models and choose/pull recommended models.
  - [ ] Document how to configure OpenRouter, choose model IDs, and understand cloud routing.
  - [ ] Document route data flow, local/cloud privacy, prerequisites, result collection, diagnostics, and uninstall.
  - [ ] Publish a SHA-256 checksum for the initially unsigned installer.
  - [ ] Document possible Windows SmartScreen prompts.

## Evaluation and release slices

- [ ] Slice 13: Prototype-machine evaluation
  - [ ] Verify Codex CLI + Ollama.
  - [ ] Verify Codex CLI + OpenRouter.
  - [ ] Verify Claude Code + Ollama.
  - [ ] Verify Claude Code + OpenRouter.
  - [ ] Verify OpenCode + Ollama.
  - [ ] Verify OpenCode + OpenRouter.
  - [ ] Verify GitHub Copilot CLI + Ollama.
  - [ ] Verify GitHub Copilot CLI + OpenRouter.
  - [ ] Confirm every path includes streamed tool calls, file edits, command execution, and the expected C# check.
  - [ ] Confirm cloud runs appear in OpenRouter activity.

- [ ] Slice 14: Second-machine release rehearsal
  - [ ] Download the release EXE on a second 16 GB Windows machine.
  - [ ] Install from the downloaded EXE.
  - [ ] Confirm Start Menu shortcut and optional Desktop shortcut launch the regular work flow.
  - [ ] Repeat all eight harness/provider paths.
  - [ ] Uninstall.
  - [ ] Confirm shortcuts and icon assets are removed.
  - [ ] Confirm normal Codex configuration and other shared tools are unaffected.

- [ ] Slice 15: Public release
  - [ ] Tag the release only after the eight-path and installer gates pass.
  - [ ] Publish the EXE.
  - [ ] Publish the SHA-256 checksum.
  - [ ] Publish release notes.
  - [ ] Publish known compatibility limits.

## Defaults and deferred work

The repository and release assets are public. The initial fixture contains no company code. The installer handles setup and checks; the installed shortcut starts the regular work launcher, while `launch-agent.ps1` remains available for scripted launch and evaluation runs. OpenCode is evaluated natively on Windows for version 1. The gateway uses explicit routes and one local and one cloud model for the initial harness comparison. Later releases can add more models, tasks, richer model discovery, and a fuller launcher UI.

Do not add LiteLLM, a shared team gateway, LAN binding, Docker or Azure deployment, automatic provider fallback, centralized usage collection, dashboards, budgets, or automated scoring in version 1.

## Relevant documentation

- [Codex configuration](https://learn.chatgpt.com/docs/config-file/config-reference)
- [Ollama Codex integration](https://docs.ollama.com/integrations/codex)
- [Ollama Anthropic compatibility](https://docs.ollama.com/api/anthropic-compatibility)
- [OpenRouter Claude Code integration](https://openrouter.ai/docs/guides/coding-agents/claude-code-integration)
- [Copilot CLI custom models](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-byok-models)
- [OpenCode providers](https://opencode.ai/docs/providers/)
- [Inno Setup per-user installation](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)
