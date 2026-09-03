# progress-observability for VS Code / GitHub Copilot

All nine skills from the Claude Code plugin, packaged for GitHub Copilot in
VS Code - instrument-agent, build-template-agent, build-custom-agent,
scaffold-agent, health-check, trace-triage, cost-report, coverage-gaps, and
generate-eval. Nothing here is Claude-specific.

> **Check the plugin install first - it is usually the better path.** Copilot
> CLI installs this repo as a plugin (`copilot plugin marketplace add
> observability-oss/progress-observability-plugin` then `copilot plugin install
> progress-observability@progress-observability`), and VS Code auto-discovers
> CLI-installed plugins - skills and the MCP server included, verified. Newer
> VS Code can also install it directly via Agent Plugins (preview). **This
> bundle is the classic path** for older VS Code, orgs with
> `chat.plugins.enabled` off, or setups without the plugin system.

## What's in this folder

Copy these into the repo where you build/debug your AI system (the paths below
are already where Copilot expects them):

| File | Role | Claude-plugin equivalent |
|---|---|---|
| `.github/copilot-instructions.md` | Always-on instructions: all nine workflows + the MCP contract for platform-reading workflows | the `skills/` + `references/mcp.md` |
| `.github/prompts/instrument-agent.prompt.md` | `/instrument-agent` | `commands/instrument-agent.md` |
| `.github/prompts/build-template-agent.prompt.md` | `/build-template-agent` | `commands/build-template-agent.md` |
| `.github/prompts/build-custom-agent.prompt.md` | `/build-custom-agent` | `commands/build-custom-agent.md` |
| `.github/prompts/scaffold-agent.prompt.md` | `/scaffold-agent` | `commands/scaffold-agent.md` |
| `.github/prompts/health-check.prompt.md` | `/health-check` | `commands/health-check.md` |
| `.github/prompts/trace-triage.prompt.md` | `/trace-triage` | `commands/trace-triage.md` |
| `.github/prompts/cost-report.prompt.md` | `/cost-report` | `commands/cost-report.md` |
| `.github/prompts/coverage-gaps.prompt.md` | `/coverage-gaps` | `commands/coverage-gaps.md` |
| `.github/prompts/eval-from-trace.prompt.md` | `/eval-from-trace` | `commands/eval-from-trace.md` |
| `.github/prompts/eval-from-scratch.prompt.md` | `/eval-from-scratch` | `commands/eval-from-scratch.md` |
| `skills/build-template-agent/` | Complete skill, template catalog, copy helper, and four finished agent sources | `skills/build-template-agent/` |
| `skills/build-custom-agent/` | Guided custom workflow, helpers, and bounded .NET 10 starter | `skills/build-custom-agent/` |
| `.vscode/mcp.json` | Connects to the Progress Observability MCP server | `.mcp.json` |

## Requirements

- VS Code with the **GitHub Copilot** and **Copilot Chat** extensions.
- Copilot Chat used in **Agent mode** - builders need file and terminal access,
  and MCP tools only appear in agent mode.
- For platform-reading workflows, a **paid Progress Observability plan** and an
  **MCP API key**
  (API Keys → MCP API Keys). Use a *With content* key only if you want Copilot to
  read raw prompt/completion text (trace-triage on a culprit span, or few-shot
  examples in generate-eval). The two builders do not need an MCP key.
- For either builder's live prototype path: the .NET 10 SDK plus the model and
  separate Progress Integration-key settings documented in the copied project.
  Builders do not query Progress afterward, so ingestion remains unverified.

## Setup

1. **Copy this folder's contents into your project root**, so you end up with:

   ```
   your-repo/
   ├── skills/
   │   ├── build-template-agent/    # catalog and finished prebuilt projects
   │   └── build-custom-agent/      # bounded custom starter
   ├── .vscode/mcp.json
   └── .github/
       ├── copilot-instructions.md
       └── prompts/
           ├── instrument-agent.prompt.md
           ├── build-template-agent.prompt.md
           ├── build-custom-agent.prompt.md
           ├── scaffold-agent.prompt.md
           ├── health-check.prompt.md
           ├── trace-triage.prompt.md
           ├── cost-report.prompt.md
           ├── coverage-gaps.prompt.md
           ├── eval-from-trace.prompt.md
           └── eval-from-scratch.prompt.md
   ```

   If you already have a `.github/copilot-instructions.md`, append this one's
   contents to it rather than overwriting.

2. **For platform-reading workflows, reload VS Code.** Open `.vscode/mcp.json`
   and click **Start** on the `progress-observability` server (or run *MCP: List
   Servers* → Start). On first start Copilot prompts for your MCP API key and
   stores it securely - the key is never written to the file. Skip this step
   for either builder.

3. **Open Copilot Chat, switch to Agent mode.** The `progress-observability`
   tools now show in the tools picker.

## Usage

- `/instrument-agent` - instrument an existing Python, TypeScript, or .NET agent and verify its traces.
- `/build-template-agent docs-qa` - copy, build, smoke-test, and run a finished .NET 10 template without MCP. IDs: `release-evidence-reviewer` (default), `docs-qa`, `operations-data-analyst`, `ticket-triage`. Reports `Progress ingestion: unverified`.
- `/build-custom-agent` - turn a short purpose into a safe local prototype or a no-write integration plan without MCP; reports `Progress ingestion: unverified`.
- `/scaffold-agent` - create a new .NET agent project, already instrumented, from the starter template.
- `/health-check` - verify the setup: connection, key scope, data flow, instrumentation depth. No arguments.
- `/trace-triage` - give it a trace id, or a service + symptom + window.
- `/cost-report` - optional date range / focus.
- `/coverage-gaps` - optional service to scope to.
- `/eval-from-trace` - service + symptom; builds a judge from recent traces.
- `/eval-from-scratch` - paste a system prompt; builds a judge with no trace data.
- Or just ask in natural language (*"why did this run stall?"*, *"what's driving my
  spend?"*, *"what should I evaluate next?"*, *"write a faithfulness eval for my RAG
  bot"*) - the instructions file routes to the right workflow.

The builders do not invoke `/health-check`. If you have an MCP key, run it
separately when you want to inspect platform data after a build.

The templates have distinct local UIs: contextual release-review chat, cited
document Q&A with follow-ups, a prompt-driven CSV dashboard, and a JSON ticket
inbox with questions and temporary missing-fact scenarios. Resets clear local
context; no template persists conversations or changes business-system records.
Each copies one finished project unchanged to `./<template-id>` (or your chosen
missing/empty target), then validates its own three catalogued smoke cases.
No live ticket writes, external business-data connections, or source generation.

## Verify the MCP connection (platform-reading workflows)

```bash
export OBSERVABILITY_MCP_API_KEY="acm_..."
curl -s https://mcp.observability.progress.com/mcp \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $OBSERVABILITY_MCP_API_KEY" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

Expected: 7 tools on a Metadata-only key, 9 on a With-content key - presence of `get_observation_details_with_content` confirms With-content scope.

## Keeping in sync (generated bundle)

`.github/copilot-instructions.md` and both asset-bearing builder skills are
**generated** - don't edit them by hand. Copilot can't include external files,
so the build inlines the plugin's instruction sources into one file and mirrors
both complete asset-bearing builder skills byte-for-byte. The single sources of
truth are `../references/mcp.md`, `../skills/*/SKILL.md`, and the eval frame in
`../skills/generate-eval/references/frame.md` (each exposes the shared region
between `<!-- copilot:start -->` / `<!-- copilot:end -->` markers).

After editing any source, regenerate:

```bash
python scripts/build_copilot.py          # rewrite copilot-instructions.md
python scripts/build_copilot.py --check   # verify it's in sync (used by CI)
```

A ready-to-enable CI workflow lives at [`ci/check-copilot.yml`](../ci/check-copilot.yml).
**Move it to `.github/workflows/check-copilot.yml`** to enable it - it then runs
`--check` on every push/PR so a stale file fails the build. (It ships under `ci/`
rather than `.github/workflows/` because the automation token that populated this
repo lacks GitHub's `workflow` scope; moving it is a one-time step.)
