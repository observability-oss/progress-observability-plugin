# progress-observability

A plugin for working with the
[Progress Observability Platform](https://www.telerik.com/ai-observability-platform)
from your coding agent - **observe, debug, cost-manage, and evaluate** your AI
systems, all over one read-only MCP connection.

> **Independent community project.** Not affiliated with, or endorsed by, Progress
> Software. It connects to the Progress Observability Platform through the
> platform's public MCP API; "Progress Observability Platform" is Progress's
> product, referenced here only to say what these tools work with.

Everything reads through the `progress-observability` MCP server; the shared
contract (tools, limits, and the untrusted-content rules) lives in
[`references/mcp.md`](./references/mcp.md).

## Skills

| Skill | Command | What it does |
|---|---|---|
| **instrument-agent** | `/instrument-agent` | Adds instrumentation to an **existing** agent - Python, TypeScript, or .NET - then verifies over MCP that traces arrive. |
| **scaffold-agent** | `/scaffold-agent` | Creates a new .NET agent project, already instrumented, from the [dotnet-agent-starter](https://github.com/observability-oss/dotnet-agent-starter) template. |
| **health-check** | `/health-check` | Read-only setup check: connection, key scope, whether traces are flowing, and instrumentation depth. Run it first. |
| **trace-triage** | `/trace-triage` | Root-cause a failed or slow run by walking its span tree; returns a diagnosis + a fix. |
| **cost-report** | `/cost-report` | Spend by model/app/day, quota burn, spike explanation, cheaper-model hypotheses. |
| **coverage-gaps** | `/coverage-gaps` | Finds production behaviors with no evaluation and prioritizes which judges to build. |
| **generate-eval** | `/eval-from-trace`, `/eval-from-scratch` | Research-grounded LLM-as-a-Judge evaluator prompts, optionally grounded in real traces. See [`skills/generate-eval`](./skills/generate-eval/). |

**Start with instrument-agent.** Nothing else here has anything to read until
your agent is sending traces, and it takes an existing Python, TypeScript, or
.NET app to that point in one pass — then verifies over MCP that the spans
actually arrived. **scaffold-agent** is its mirror image for a project that
doesn't exist yet. Those two write project files; everything else is strictly
read-only.

Once traces are flowing, **health-check** confirms the wiring, and the four
workflow skills chain into one loop: **trace-triage** finds a failure →
**coverage-gaps** confirms nothing measures it → **generate-eval** builds the
judge → **cost-report** keeps the bill honest while you iterate.

## Setup

Two layers: the **MCP connection** (universal - any MCP-capable client can read the
platform) and the **workflows** (the skills/commands, packaged per tool). Both use
the same API key.

### 1. Get an MCP API key

Progress Observability → [**API Keys → MCP API Keys**](https://observability.progress.com/api-keys) - note MCP API
keys are **not available on the free tier**; without one the platform-reading
skills can't run. *Metadata only*
scope covers health-check, instrument-agent's verify step, trace-triage,
cost-report, coverage-gaps, and from-scratch eval design; *With content* is needed only to read raw prompt/completion text (e.g.
few-shot examples in generate-eval). Then expose it where your tool can read it:

```bash
export OBSERVABILITY_MCP_API_KEY="acm_..."
```

### 2. Pick a setup

| Option | What you get |
|---|---|
| **Claude Code plugin** | All seven skills, their commands, and the MCP connection in one install. |
| **VS Code / GitHub Copilot** | Auto-discovers the CLI-installed plugin; or installs it via Agent Plugins (preview); classic `copilot/` bundle as fallback. |
| **GitHub Copilot CLI** | Installs this repo as a plugin from the same marketplace - all seven skills **and the MCP connection** in two commands. |
| **Any agent via skills.sh** | `npx skills add` installs the skills for ~20 coding agents (Claude Code, Codex, Cursor, Cline, Amp, …). MCP server wired yourself. |
| **A single skill** | One self-contained skill folder, copied anywhere. MCP server wired yourself. |

**Claude Code plugin** - install and restart:

```
/plugin marketplace add observability-oss/progress-observability-plugin
/plugin install progress-observability@progress-observability
```

**GitHub Copilot CLI** (`npm install -g @github/copilot`) - Copilot's plugin
system reads the same marketplace as Claude Code, so this repo installs as a
plugin directly (verified: "Installed 7 skills", and `copilot mcp list` shows
the `progress-observability` server wired):

```bash
copilot plugin marketplace add observability-oss/progress-observability-plugin
copilot plugin install progress-observability@progress-observability
```

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
   from a shell that has it exported).
2. VS Code Agent Plugins (preview, needs `chat.plugins.enabled`): add
   `"chat.plugins.marketplaces": ["observability-oss/progress-observability-plugin"]`
   to settings.json, then Extensions → `@agentPlugins` → Install. VS Code reads
   this repo's `.claude-plugin/plugin.json` and root `.mcp.json` directly - the
   same plugin, all three tools.
3. Classic (older VS Code, or plugins disabled by your org): copy
   [`copilot/`](./copilot/)'s contents into your repo (`.vscode/mcp.json`,
   `.github/copilot-instructions.md`, `.github/prompts/`); full steps in
   [`copilot/README.md`](./copilot/README.md).

**Any agent via [skills.sh](https://skills.sh)** - from your project root:

```bash
npx skills add observability-oss/progress-observability-plugin
```

**A single skill** - see [Use one skill on its own](#use-one-skill-on-its-own) below.

Verify the connection with the `curl` snippet in [`references/mcp.md`](./references/mcp.md).

## Usage

The commands and natural-language triggers are the same across tools (Claude Code
slash commands and Copilot prompt files share names):

- `/instrument-agent` - add observability to the agent repo you're in, and prove traces arrive
- `/scaffold-agent triage support tickets against our KB` - new instrumented agent from the template
- `/health-check` - verify your setup before anything else (no arguments)
- `/trace-triage <trace-id>` or `/trace-triage checkout-agent, timeouts, last hour`
- `/cost-report last 30 days, by model`
- `/coverage-gaps checkout-agent`
- `/eval-from-trace checkout-agent, wrong tool calls` · `/eval-from-scratch` (paste a prompt)
- Or just ask in natural language - *"why did this run stall?"*, *"what's driving my spend?"*, *"what should I evaluate next?"* - the skills trigger on intent.

## Use one skill on its own

Each skill folder is fully self-contained - the MCP contract it needs ships
inside it as `references/mcp.md` (a generated copy of the repo-root original;
`python scripts/sync_skill_refs.py --check` keeps them in sync).

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
- Nothing here writes back to the platform.
- The plugin bundle format (`.claude-plugin/`, slash commands) is specific to
  Claude Code; the [`copilot/`](./copilot/) folder re-packages all seven skills for
  VS Code / Copilot - the classic path for setups without the plugin system.

## License

MIT - see [`LICENSE`](./LICENSE). Provided "as is", without warranty; the authors
accept no liability for use of this software.

---

For the [Progress Observability Platform](https://www.telerik.com/ai-observability-platform).
