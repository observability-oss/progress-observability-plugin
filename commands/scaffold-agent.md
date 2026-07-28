---
description: Scaffold a new .NET AI agent with Progress Observability already wired up, from the dotnet-agent-starter template.
argument-hint: [what the agent should do, e.g. "triage support tickets against our KB"]
---

Use the `scaffold-agent` skill.

Agent to build: $ARGUMENTS

Clone `observability-oss/dotnet-agent-starter` at its newest release tag (fall back to the default branch if it has no tags), drop the `.git` folder, and scaffold into a new folder named after the agent. Fill only the `scaffold:`-marked slots — app name, system prompt, tools, corpus — and leave the observability wiring, pinned package versions, and configuration layering exactly as they are. Tools get `[Description]` attributes and working stubs over plausible in-memory data, including at least one honest not-found path. Run `dotnet build` and make it pass before reporting success, then hand off with the `dotnet user-secrets` commands, `dotnet run`, and `/health-check`.

If the goal wasn't given, ask what the agent should do and which 2–4 actions it needs before cloning.
