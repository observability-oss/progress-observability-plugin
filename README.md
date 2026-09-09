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
| **build-template-agent** | `/build-template-agent` | Copies a selected finished .NET 10 agent, builds it, runs its three smoke cases, and starts its local UI. |
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

For either builder, start Copilot from the desired empty folder with the
server-disable flag, then paste the prompt supplied by the platform:

```bash
copilot --disable-mcp-server progress-observability
```

Copilot CLI 1.0.82 may probe the plugin's MCP server before applying this flag,
so an MCP authentication warning can still appear at startup. The builder
skills do not call MCP or require an MCP API key; do not configure one to
resolve this startup warning.

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
- `/build-template-agent docs-qa` - copy, build, smoke-test, and run the Docs Q&A template; omit the ID for Release Evidence Reviewer. Reports `Progress ingestion: unverified`.
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

### Ready agent templates

Each selection copies one finished standalone project unchanged. No domain
interview, generated source, frontend build, live business-system integration,
or separate plugin install is needed. All use .NET 10 and the same model and
Integration settings above; the sample business data is bundled locally.

| Template ID | Bundled data | Local UI |
|---|---|---|
| `release-evidence-reviewer` (default) | Markdown policy and release evidence | Contextual review chat with explicit reset |
| `docs-qa` | Short synthetic product/policy Markdown documents | Source-scoped Q&A and grounded follow-ups |
| `operations-data-analyst` | Synthetic daily service metrics in CSV | Questions, clickable KPIs, trends and breakdowns |
| `ticket-triage` | Synthetic JSON tickets plus Markdown routing policy | Inbox questions, missing-fact intake and temporary scenarios |

The analyst computes metrics in C#; the model explains them. Ticket Triage
suggests a queue and priority or requests missing information; it never assigns,
updates, or sends a ticket. Docs Q&A returns no-match when its local corpus
cannot support the question. All three new UIs render structured tool evidence,
not fields extracted from generated prose.

The [catalog](./skills/build-template-agent/templates.json) defines project
filenames and each template's three smoke cases. Default destination:
`./<template-id>`. A target must be missing or empty; unknown IDs are rejected.
For a platform-to-IDE handoff, supply the selected ID explicitly, for example:
`Use build-template-agent with template "ticket-triage".`

The catalog and each copied project's README define the maintained template
contract. Packaging and credential-free runtime coverage live under
[`scripts/tests`](./scripts/tests/).

### Custom prototypes

Use interactive Copilot for the guided custom build. Noninteractive `copilot -p`
returns a proposal until that displayed scope is explicitly approved;
`--allow-all` grants tool permission, not scope approval.

After the custom app passes its required checks, Copilot normally tries four
chat interactions: a real task, an exact-history follow-up, a fresh-chat check,
and a missing-evidence or live-action boundary. It may make one small, verified
repair. A package-free .NET helper handles requests/history/timeouts; Copilot
judges the answers. To skip
this extra work, add `Try-and-refine: off` to your build prompt; use `on` to
enable it explicitly. This adds no intake question and does not skip build,
the three smoke tests, or the running UI's health check. It never runs for
plan-only or prebuilt templates.

The [optional phase](./skills/build-custom-agent/references/try-and-refine.md)
is isolated from the starter/runtime. Maintainers can disable it globally by
changing the single default in `skills/build-custom-agent/SKILL.md`. To remove
it, remove that skill's preference paragraph and optional-phase section, this
note, the reference and `scripts/check-behavior.cs` (and its maintainer coverage
under `scripts/tests/custom-agent/`),
then regenerate the Copilot bundle with
`python3 scripts/build_copilot.py`. No app code or required checks depend on it.

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
- Both builders use standard SDK instrumentation; function-invocation middleware
  already traces tools. `Progress:Observability:RecordInputs` and
  `Progress:Observability:RecordOutputs` default to `true` for the local demo,
  recording LLM inputs/outputs when tracing is enabled. The copied README shows
  process-specific `false` overrides for disabling that LLM message recording.
  These flags do not independently control native tool contents or fully exclude
  exception text in SDK 1.2.2, so neither setting guarantees content-free telemetry.
- `build-template-agent` defaults to GitHub Copilot agent mode. Its copy helper
  needs the .NET 10 SDK; live smoke cases also need the model and Integration
  settings documented in the copied README. It returns the local app link,
  three smoke results, and one generic Tracing-page link without claiming that
  Progress ingested the traces.
- `build-custom-agent` asks for a short Purpose, uses only local or clearly
  simulated behavior for a prototype, and offers a no-write implementation plan
  for complex/live requests. When meaningful, it can also propose a simplified
  mock PoC, with reduced scope confirmed before building. It never implements
  live business-system actions. Its prototype path reports Progress ingestion
  as unverified.
- The plugin bundle format (`.claude-plugin/`, slash commands) is specific to
  Claude Code; the [`copilot/`](./copilot/) folder re-packages all nine skills for
  VS Code / Copilot - the classic path for setups without the plugin system.

## License

MIT - see [`LICENSE`](./LICENSE). Provided "as is", without warranty; the authors
accept no liability for use of this software.

---

For the [Progress Observability Platform](https://www.telerik.com/ai-observability-platform).
