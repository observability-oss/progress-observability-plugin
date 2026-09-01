# progress-observability

A plugin for building, instrumenting, and working with AI systems on the
[Progress Observability Platform](https://www.telerik.com/ai-observability-platform)
from your coding agent. Builder workflows run locally; platform analysis uses
one read-only MCP connection.

> **Independent community project.** Not affiliated with, or endorsed by, Progress
> Software. It connects to the Progress Observability Platform through the
> platform's public MCP API; "Progress Observability Platform" is Progress's
> product, referenced here only to say what these tools work with.

Platform-reading workflows use the `progress-observability` MCP server; the
shared contract (tools, limits, and the untrusted-content rules) lives in
[`references/mcp.md`](./references/mcp.md). `build-template-agent` and
`build-custom-agent` do not call MCP. Their handoff reports
`Progress ingestion: unverified`.

## Skills

| Skill | Command | What it does |
|---|---|---|
| **instrument-agent** | `/instrument-agent` | Adds instrumentation to an **existing** agent - Python, TypeScript, or .NET - then hands off to `/health-check` to confirm traces arrive. |
| **build-template-agent** | `/build-template-agent` | Copies the finished .NET 10 **Release Evidence Reviewer**, builds it, runs three smoke cases, and starts its local chat UI. |
| **build-custom-agent** | `/build-custom-agent` | Turns a purpose into either a bounded local .NET 10 prototype or a no-write integration plan. |
| **scaffold-agent** | `/scaffold-agent` | Directly creates a new .NET agent from the general [dotnet-agent-starter](https://github.com/observability-oss/dotnet-agent-starter). |
| **health-check** | `/health-check` | Read-only setup check: connection, key scope, whether traces are flowing, and instrumentation depth. Run it first. |
| **trace-triage** | `/trace-triage` | Root-cause a failed or slow run by walking its span tree; returns a diagnosis + a fix. |
| **cost-report** | `/cost-report` | Spend by model/app/day, quota burn, spike explanation, cheaper-model hypotheses. |
| **coverage-gaps** | `/coverage-gaps` | Finds production behaviors with no evaluation and prioritizes which judges to build. |
| **generate-eval** | `/eval-from-trace`, `/eval-from-scratch` | Research-grounded LLM-as-a-Judge evaluator prompts, optionally grounded in real traces. See [`skills/generate-eval`](./skills/generate-eval/). |

Choose by starting point: **instrument-agent** for an existing app,
**build-template-agent** for a ready project, **build-custom-agent** for the
guided Purpose-to-prototype-or-plan path, or **scaffold-agent** for a direct
starter-based scaffold. These four can write project files; everything else is
strictly read-only.

Once traces are flowing, **health-check** confirms the wiring, and the four
workflow skills chain into one loop: **trace-triage** finds a failure →
**coverage-gaps** confirms nothing measures it → **generate-eval** builds the
judge → **cost-report** keeps the bill honest while you iterate.

## Setup

Two layers: the **workflows** (the skills/commands, packaged per tool) and an
**MCP connection** for workflows that read the platform. The two builders do not
need an MCP key. A generated agent sends traces with a separate Integration key
(`ac_p_...`), but the builder does not query the platform to confirm ingestion;
the copied project's README explains the app-only configuration.

### 1. Get an MCP API key (platform-reading workflows)

Progress Observability → [**API Keys → MCP API Keys**](https://observability.progress.com/api-keys) - note MCP API
keys are **not available on the free tier**; without one the platform-reading
skills can't run. Skip this step when using either builder. *Metadata only*
scope covers health-check, the instrumenter verification step, trace-triage,
cost-report, coverage-gaps, and from-scratch eval design; *With content* is needed only to read raw prompt/completion text (e.g.
few-shot examples in generate-eval). Then expose it where your tool can read it:

```bash
export OBSERVABILITY_MCP_API_KEY="acm_..."
```

### 2. Pick a setup

| Option | What you get |
|---|---|
| **Claude Code plugin** | All nine skills, their commands, and the MCP connection in one install. |
| **VS Code / GitHub Copilot** | Auto-discovers the CLI-installed plugin; or installs it via Agent Plugins (preview); classic `copilot/` bundle as fallback. |
| **GitHub Copilot CLI** | Installs this repo as a plugin from the same marketplace - all nine skills **and the MCP connection** in two commands. |
| **Any agent via skills.sh** | `npx skills add` installs the skills for ~20 coding agents (Claude Code, Codex, Cursor, Cline, Amp, …). Wire MCP only for platform-reading skills. |
| **A single skill** | One self-contained skill folder, copied anywhere. Wire MCP only if that skill reads the platform. |

**Claude Code plugin** - install and restart:

```
/plugin marketplace add observability-oss/progress-observability-plugin
/plugin install progress-observability@progress-observability
```

**GitHub Copilot CLI** (`npm install -g @github/copilot`) - Copilot's plugin
system reads the same marketplace as Claude Code, so this repo installs as a
plugin directly; `copilot mcp list` shows the `progress-observability` server
wired:

```bash
copilot plugin marketplace add observability-oss/progress-observability-plugin
copilot plugin install progress-observability@progress-observability
```

For either builder, start Copilot from the desired empty folder without the
paid MCP server, then paste the prompt supplied by the platform:

```bash
copilot --disable-mcp-server progress-observability
```

Before that first builder run, store the app settings once. The same local
Secret Manager ID is shared by prebuilt and custom agents:

```bash
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Deployment" "gpt-4.1"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "Progress:Observability:ApiKey" "ac_p_..."
```

`AzureOpenAI:ApiKey` is optional with configured Azure identity. Secret Manager
is for local development; use the deployment environment's secret store in
production. Do not put these values in Copilot chat or a project `.env` file.

(Copilot also discovers bare skills from `.github/skills/`, `.agents/skills/`
or `.claude/skills/` if you prefer copying a single folder - see
[Use one skill on its own](#use-one-skill-on-its-own).)

The [`copilot/`](./copilot/) bundle below is for **VS Code** Copilot, which is a
different surface - the CLI does not need it.

**VS Code** - three routes, newest first:

1. Already installed via Copilot CLI? VS Code auto-discovers CLI-installed
   plugins from `~/.copilot/installed-plugins/` - nothing to do. Verified:
   skills and the MCP server both appear, and the server authenticates using
   the `OBSERVABILITY_MCP_API_KEY` from VS Code's environment (launch VS Code
   from a shell that has it exported). For either builder, leave that server
   stopped; no MCP key is needed.
2. VS Code Agent Plugins (preview, needs `chat.plugins.enabled`): add
   `"chat.plugins.marketplaces": ["observability-oss/progress-observability-plugin"]`
   to settings.json, then Extensions → `@agentPlugins` → Install. VS Code reads
   this repo's `.claude-plugin/plugin.json` and root `.mcp.json` directly - the
   same plugin, all skills and the MCP connection.
3. Classic (older VS Code, or plugins disabled by your org): copy
   all of [`copilot/`](./copilot/)'s contents into your repo (including
   the two builder skills, `.vscode/mcp.json`, and `.github/`); full
   steps in [`copilot/README.md`](./copilot/README.md).

**Any agent via [skills.sh](https://skills.sh)** - from your project root:

```bash
npx skills add observability-oss/progress-observability-plugin
```

**A single skill** - see [Use one skill on its own](#use-one-skill-on-its-own) below.

For platform-reading workflows, verify the connection with the `curl` snippet
in [`references/mcp.md`](./references/mcp.md).

## Usage

The commands and natural-language triggers are the same across tools (Claude Code
slash commands and Copilot prompt files share names):

- `/instrument-agent` - add observability to the agent repo you're in, and prove traces arrive
- `/build-template-agent` - copy, build, smoke-test, and run the Release Evidence Reviewer template; reports `Progress ingestion: unverified`
- `/build-custom-agent` - describe a purpose, then build a safe local prototype or receive an integration plan; reports `Progress ingestion: unverified`
- `/scaffold-agent triage support tickets against our KB` - new instrumented agent from the template
- `/health-check` - verify your setup before anything else (no arguments)
- `/trace-triage <trace-id>` or `/trace-triage checkout-agent, timeouts, last hour`
- `/cost-report last 30 days, by model`
- `/coverage-gaps checkout-agent`
- `/eval-from-trace checkout-agent, wrong tool calls` · `/eval-from-scratch` (paste a prompt)
- Or just ask in natural language - *"why did this run stall?"*, *"what's driving my spend?"*, *"what should I evaluate next?"* - the skills trigger on intent.

The builders do not invoke `/health-check`. If you have an MCP key, run it
separately when you want to inspect platform data after a build.

## Use one skill on its own

Each skill folder is fully self-contained. Platform-reading skills carry a
generated `references/mcp.md`. Each builder instead carries its project asset
and package-free .NET helpers, makes no MCP calls, and reports Progress
ingestion as unverified. `python scripts/sync_skill_refs.py --check` keeps the
generated MCP references in sync.

- **Claude Code:** copy a skill into your skills directory -
  `cp -r skills/generate-eval ~/.claude/skills/` (personal) or `.claude/skills/`
  (project).
- **Any tool:** the skill folder works as standalone agent instructions.

Skills that read the platform still need the MCP server wired (step 2) and the key
set. Without the plugin you won't get the slash commands - copy `commands/` too, or
just ask in natural language.

## Notes

- The MCP server is **read-only** and enforces a **72-hour** data window, per-tool
  ID caps, and rate limiting - details in
  [`references/mcp.md`](./references/mcp.md).
- The MCP workflows do not write back to the platform. Generated agents can
  send traces with their separate Integration API key, but the builders do not
  query those traces back, so they report Progress ingestion as unverified.
- `build-template-agent` defaults to GitHub Copilot agent mode. Its copy helper
  needs the .NET 10 SDK; live smoke cases also need the model and Integration
  settings documented in the copied README. It returns the local app link,
  three smoke results, and one generic Tracing-page link without claiming that
  Progress ingested the traces.
- `build-custom-agent` asks for a short Purpose, uses only local or clearly
  simulated behavior for a prototype, and returns a no-write plan when live
  systems or real side effects are essential. Its prototype path also reports
  Progress ingestion as unverified.
- The plugin bundle format (`.claude-plugin/`, slash commands) is specific to
  Claude Code; the [`copilot/`](./copilot/) folder re-packages all nine skills for
  VS Code / Copilot - the classic path for setups without the plugin system.

## License

MIT - see [`LICENSE`](./LICENSE). Provided "as is", without warranty; the authors
accept no liability for use of this software.

---

For the [Progress Observability Platform](https://www.telerik.com/ai-observability-platform).
