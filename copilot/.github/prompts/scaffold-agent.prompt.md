---
mode: 'agent'
description: 'Scaffold a new .NET AI agent with Progress Observability already wired up, from the dotnet-agent-starter template.'
---

Follow the **scaffold-agent** workflow in `.github/copilot-instructions.md`.

Agent to build (what it does, and the 2–4 actions it needs): ${input:goal}

Clone `observability-oss/dotnet-agent-starter` at its newest release tag (fall back to the default branch if it has no tags), drop the `.git` folder, and scaffold into a new folder named after the agent. Fill only the `scaffold:`-marked slots — app name, system prompt, tools, corpus — and leave the observability wiring, pinned package versions, and configuration layering exactly as they are. Tools get `[Description]` attributes and working stubs over plausible in-memory data, including at least one honest not-found path. Run `dotnet build` and make it pass before reporting success, then hand off with the `dotnet user-secrets` commands, `dotnet run`, and `/health-check`.

If the goal wasn't given, ask what the agent should do and which actions it needs before cloning.
