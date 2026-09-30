# AI Harness Gateway: Windows v1 plan

## Objective and acceptance

Run a gateway on each developer's Windows 11 machine so coding harnesses, providers, and models can be evaluated separately. The gateway listens on `127.0.0.1` and maps explicit model aliases to either a local Ollama model or an OpenRouter cloud model. It records the selected harness, provider, model, and task without storing source content.

Version 1 supports these eight paths through the gateway:

| Harness | Ollama local model | OpenRouter cloud model |
| --- | --- | --- |
| Codex CLI | Required | Required |
| Claude Code | Required | Required |
| OpenCode | Required | Required |
| GitHub Copilot CLI | Required | Required |

Each path must complete the same included C# task: read files, make a change, use its tools, run the relevant check, and summarize the result. A text-only completion is insufficient. Start with `qwen3.5:9b` as the 16 GB RAM local candidate and `anthropic/claude-sonnet-4.6` as the cloud candidate. Record exact model tags, configured context, and tool failures. If a path fails, diagnose it before claiming support.

## Gateway and launch tasks

- [ ] Build a small C# gateway with configuration validation, route resolution, protocol forwarding, upstream authentication, and metadata-only diagnostics as separate components. Bind only to loopback and stop when the evaluation ends.
- [ ] Map stable aliases such as `local-qwen` and `cloud-claude` to exact upstream model IDs and fixed endpoints. Reject unknown aliases. Do not automatically switch models or providers.
- [ ] Support the minimum request paths the four harnesses require: OpenAI Responses for Codex, Anthropic Messages for Claude Code, and OpenAI Chat Completions for OpenCode and Copilot CLI. Preserve streaming, tool calls, cancellation, and upstream error status. Add discovery endpoints only when observed integration requires them.
- [ ] Add `launch-agent.ps1` to select a working harness, provider, and model, start the gateway, apply process-scoped harness settings, and open the included C# task. Clearly show LOCAL or CLOUD before launch.
- [ ] Give Codex an isolated user-level `CODEX_HOME` because project-level Codex configuration cannot define model providers. Keep normal user harness settings intact for all four CLIs.
- [ ] Read `OPENROUTER_API_KEY` from the launching process or a secure prompt for that run. Pass it only to the gateway process; remove it from the harness child environment. Do not put keys in config files, command arguments, or logs.
- [ ] Disable optional web features during the local evaluation and verify the Ollama tag is installed locally rather than an Ollama cloud tag.

## Installer, documentation, and security tasks

- [ ] Publish a single `win-x64` setup wizard EXE as a public GitHub Release asset. Bundle the self-contained gateway, launch and diagnostic scripts, and C# fixture so users can start by downloading one file.
- [ ] Use a per-user Inno Setup installation and Windows uninstaller. Install gateway-owned files under the user's application directory. Keep mutable logs, results, and configuration in separate per-user data directories.
- [ ] Check Windows version, available memory and disk, Ollama, its service and model, all four harness commands, and the .NET SDK needed by the sample. Offer each missing prerequisite installation separately after approval; provide manual instructions when an installer is unavailable. Never silently change execution policy or install model weights.
- [ ] Make setup safe to rerun. Uninstall gateway-owned files and generated state, with an explicit choice for retaining results. Leave independently installed harnesses, Ollama, and model weights in place and explain how to remove them separately.
- [ ] Add `diagnostics.ps1` and a gateway health check. Report versions, configured route names, provider reachability, and recent categorized errors. Log timestamps, route, provider, model, status, and duration; exclude prompts, responses, source text, headers, and credentials.
- [ ] Add a comprehensive `.gitignore` for keys, `.env`, local config, logs, results, generated state, build output, IDE files, and installer artifacts. Add a secret scan to the public release workflow and inspect release contents before publishing.
- [ ] Write a README covering one-file installation, source build, launch examples, route data flow, local/cloud privacy, prerequisites, result collection, diagnostics, and uninstall. Publish a SHA-256 checksum for the initially unsigned installer and document possible Windows SmartScreen prompts.

## Evaluation and release tasks

- [ ] Create one disposable C# fixture with a fixed prompt, expected observable result, and reset procedure. Start every comparison from the same fixture revision.
- [ ] Use a small structured result record: date, task revision, harness/version, provider, exact model/tag, gateway/version, context, success, tool behavior, check outcome, duration, intervention, and notes.
- [ ] Verify each of the eight paths on a Windows 11 prototype machine, including streamed tool calls, file edits, command execution, and the expected C# check. Confirm the cloud route in OpenRouter activity.
- [ ] Check missing-tool, stopped-Ollama, missing-model, missing-key, upstream-error, occupied-port, and interrupted-stream behavior. Confirm diagnostics remain useful without exposing content.
- [ ] Install the release on a second 16 GB Windows machine from the downloaded EXE, repeat all eight paths, then uninstall. Confirm the normal Codex configuration and other shared tools are unaffected.
- [ ] Tag and publish the release only after the eight-path and installer gates pass. Include the EXE, SHA-256 checksum, release notes, and known compatibility limits.

## Defaults and deferred work

The repository and release assets are public. The initial fixture contains no company code. The installer handles setup and checks; `launch-agent.ps1` starts evaluations. OpenCode is evaluated natively on Windows for version 1. The gateway uses explicit routes and one local and one cloud model for the initial harness comparison. Later releases can add more models and tasks.

Do not add LiteLLM, a shared team gateway, LAN binding, Docker or Azure deployment, automatic provider fallback, centralized usage collection, dashboards, budgets, or automated scoring in version 1.

## Relevant documentation

- [Codex configuration](https://learn.chatgpt.com/docs/config-file/config-reference)
- [Ollama Codex integration](https://docs.ollama.com/integrations/codex)
- [Ollama Anthropic compatibility](https://docs.ollama.com/api/anthropic-compatibility)
- [OpenRouter Claude Code integration](https://openrouter.ai/docs/guides/coding-agents/claude-code-integration)
- [Copilot CLI custom models](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-byok-models)
- [OpenCode providers](https://opencode.ai/docs/providers/)
- [Inno Setup per-user installation](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)
