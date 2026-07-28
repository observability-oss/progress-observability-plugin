---
name: scaffold-agent
description: Scaffold a new .NET AI agent project with Progress Observability already wired up, starting from the observability-oss dotnet-agent-starter template. Use when the user wants to create, scaffold, bootstrap, or start a new agent — "build me an agent that…", "scaffold a .NET agent", "new agent project with observability", "start an agent from the template".
---

# Scaffold an instrumented .NET agent

Create a working, already-instrumented .NET agent for the user's domain by
starting from the `observability-oss/dotnet-agent-starter` template and filling
in only its marked slots.

**This skill writes files.** Most of this plugin only reads the Progress
Observability Platform; this skill creates a project on disk and makes no MCP
calls at all. (Its sibling `instrument-agent` retrofits observability onto a
project that already exists.)

<!-- copilot:start -->
The template already contains the observability wiring, the pinned package
versions, and the configuration layering — all of it verified to build. Your job
is the domain layer: the agent's name, its instructions, its tools, and its
knowledge corpus. Everything else is copied through untouched.

## Workflow

1. **Gather what you need.** Ask for whatever the user hasn't already said:
   - **What the agent does** — one or two sentences. This becomes its instructions.
   - **Its actions** — 2–4 things it should be able to do (look something up,
     create a record, run a calculation). These become tools.
   - **Grounding documents** — does it answer from a body of knowledge? If so,
     what kind of documents.
   - **A short name** — used for the project, the app name, and the folder.
     Slugify it (lowercase, hyphens) for the app name.

   Don't over-interview. Two exchanges is plenty; sensible defaults beat a
   questionnaire.

2. **Fetch the template.** Prefer the newest release tag; fall back to the
   default branch when the repo has no tags yet:

   ```bash
   REPO=https://github.com/observability-oss/dotnet-agent-starter.git
   TAG=$(git ls-remote --tags --refs --sort=-v:refname "$REPO" 'v*' | head -n1 | sed 's|.*refs/tags/||')
   git clone --depth 1 ${TAG:+--branch "$TAG"} "$REPO" <target-folder>
   rm -rf <target-folder>/.git
   ```

   Scaffold into a new folder named after the agent, never into a folder that
   already has files in it. **If the clone fails** (no network, no git), stop and
   give the user the clone command to run themselves — do not reconstruct the
   template from memory. Getting the observability wiring or the package
   versions subtly wrong is worse than not scaffolding.

3. **Rename the project** to the user's agent (skip if they want to keep
   `StarterAgent`): rename `src/StarterAgent/` and its `.csproj`, then update
   `<RootNamespace>` and every `namespace StarterAgent;` declaration. Mechanical —
   the build in step 5 catches any miss.

4. **Fill the marked slots, and only those.** The template marks each one with a
   `scaffold:` comment:

   | Marker | Where | What to write |
   |---|---|---|
   | `scaffold:app-name` | `Program.cs`, `appsettings.json` | The slugified agent name |
   | `scaffold:system-prompt` | `Program.cs` | Instructions derived from what the user described |
   | `scaffold:tools` | `Program.cs` + `Tools.cs` | One method per action, registered in the tools list |
   | `scaffold:corpus` | `KnowledgeBase.cs` + `docs/` | Their grounding documents |

   - **Tools**: one public method per action on the tools class, each with a
     `[Description]` attribute on the method and on every parameter (the model
     picks tools by those descriptions), returning `string`. Implement them as
     stubs over plausible in-memory data — not `throw new NotImplementedException()`
     — so the agent actually runs and produces real tool-call spans on day one.
     Leave a `// TODO:` marking where the user's real system connects. Give at
     least one tool an honest failure path (unknown id → a clear "not found"
     message), which is what makes `trace-triage` demonstrable.
   - **Corpus**: replace `docs/*.md` with a few short markdown documents for
     their domain. Keep `observability-basics.md` — it stays accurate and useful.
   - **Instructions**: tell the agent to ground answers in the knowledge base
     when a corpus exists, to use its tools rather than guess, and to say so
     plainly when something isn't found.

5. **Never touch:** the `ObservabilityTracer.Initialize` / `AddObservability()` /
   `Shutdown()` block, the pinned `PackageReference` versions, the
   `ConfigurationBuilder` layering, or the `UserSecretsId`. If the user asks to
   change one, do it — but tell them what it affects first.

6. **Verify.** Run `dotnet build` in the new project. It must succeed before you
   report success. Fix what you broke; if the failure is in untouched template
   code, say so rather than papering over it.

7. **Hand off.** Tell the user, in this order:
   - the `dotnet user-secrets set` commands for their Azure OpenAI settings and
     the Progress **Integration** key (`ac_p_…`), run from the project folder;
   - `dotnet run`, and a couple of example prompts that exercise their tools;
   - then **`/health-check`** to confirm traces are flowing — after which
     `trace-triage`, `cost-report`, `coverage-gaps`, and `eval-from-trace` work
     against their own agent.

   Two things that trip people up on the first run, so state both:
   - The MCP key (`acm_…`) is a **different key** from the Integration one, and it
     is read by the coding agent rather than by their app — so it goes in the
     environment (`OBSERVABILITY_MCP_API_KEY`), not user secrets, and must be set
     before the agent starts.
   - The scaffolded folder carries its own `.mcp.json`, which only applies when
     that folder is the project root. If you scaffolded into a subfolder, tell
     them to open it as its own project before running `/health-check`.
<!-- copilot:end -->
