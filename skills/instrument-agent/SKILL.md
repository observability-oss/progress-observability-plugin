---
name: instrument-agent
description: Add Progress Observability instrumentation to an existing AI agent or LLM app — Python, TypeScript/JavaScript, or .NET, including LangChain, LangGraph, LlamaIndex, CrewAI, OpenAI Agents, Haystack, MCP servers and Microsoft.Extensions.AI — with the smallest possible diff, then hand off with where to confirm the traces. Use when the user asks to "instrument my agent", "add observability", "add tracing/telemetry to this repo", "connect this to Progress Observability", or has an existing uninstrumented project they want on the platform. Not for creating a project from scratch — this only edits code that already exists.
license: MIT
compatibility: Edits files only — needs no MCP server and no MCP key. The instrumented app needs a Progress Observability Integration key (ac_p_…) at runtime, and the project's own package manager (pip/uv/poetry, npm/yarn/pnpm, or dotnet) to install the SDK.
metadata:
  author: observability-oss
  languages: "Python, TypeScript, .NET"
  source: observability-oss/observability-skills
  verified-against:
    pypi: progress-observability@1.4.3
    npm: "@progress/observability@2.1.2"
    nuget: Progress.Observability.Instrumentation@1.2.2
---

# Instrument an existing agent

Retrofit Progress Observability onto an existing codebase with the smallest
possible diff, then run it once so spans start flowing. For a project that
does not exist yet, this is the wrong skill — it only edits what is already
there.

**This skill writes code** — the instrumentation edits and nothing else. It never
restructures the app, and it never reads the platform back: confirming the traces
arrived is left to `/health-check`.

<!-- copilot:start -->
## 1 · Detect — before touching anything

Scan the repo and report what you found, then the diff you intend to make,
*before* editing:

- **Language & entry point** — `pyproject.toml`/`requirements.txt`,
  `package.json` (check `"type": "module"` — it decides the wiring path),
  `*.csproj`.
- **LLM SDKs and frameworks in use** — imports of `openai`, `anthropic`,
  `langchain`, `llama_index`, `@langchain/*`, `Microsoft.Extensions.AI`, etc.
  Check each against the supported-instruments list in the language reference.
  A client pointed at an **OpenAI-compatible endpoint** (OpenRouter, LiteLLM,
  vLLM, Together, most gateways) counts as OpenAI — see the reference.
- **Existing telemetry** — OpenTelemetry setup, Traceloop, or a previous
  Progress Observability init. Never stack a second tracer. If the app already
  exports OTel, add Progress as an **additional exporter** alongside it —
  don't replace what's there without saying so first.
- **Scope** — a monorepo, several services, or more than one agent needs a
  "which one?" question before you touch anything. Don't pick for the user.
- **Config style** — dotenv, user secrets, plain env — instrumentation config
  should arrive the same way the app's other secrets do.
- **Package manager** — lockfiles decide the install command: `uv.lock` /
  `poetry.lock` / `Pipfile` for Python, `yarn.lock` / `pnpm-lock.yaml` for
  Node (defaults: `pip` / `npm`); `.csproj` always means `dotnet add package`.
- **Meta package present? (Python)** — if the dependency list has
  `langchain-core` or `llama-index-core` but not the `langchain` /
  `llama-index` meta package, note it: adding it is a required Wire edit
  below. Check the dependency file, not the imports.

If a framework in use is *not* in the supported list — DSPy, AutoGen,
Pydantic AI, Semantic Kernel and Google ADK are the common ones — say so
plainly and offer the decorator / manual-span fallback from the reference.
Never imply auto-instrumentation covers a framework it doesn't. **LangGraph is
supported** in Python (it rides the LangChain instrumentor and produces full
graph topology); it is *not* supported in the JS SDK. Check the language
reference rather than assuming either way.

## 2 · Wire — follow the language reference exactly

Open the matching reference and use its snippets verbatim — they are verified
against the published packages, not reconstructed:

- `references/python.md` — `progress-observability` (PyPI)
- `references/typescript.md` — `@progress/observability` (npm)
- `references/dotnet.md` — `Progress.Observability.Instrumentation` (NuGet)

Rules that hold across all three:

- **Init at process start, before any LLM client exists.** In ESM Node that
  means the hooks import and `Observability.instrument()` run before the app is
  even imported; in Python, before clients are constructed; in .NET, before the
  agent is built.
- **Minimal diff.** Typically: one dependency, one import, one init call, env
  var wiring, and a flush-on-exit. If you find yourself moving app code around,
  stop and reconsider — including "just" exporting a module-scope script so you
  have something to wrap. That is restructuring, and it is never the answer.
- **App with no LLM calls: instrument every step, not the entry point alone.**
  Decorators (Python/.NET) and `wrapFunctionWithSpan` (TS) are the *only* span
  source in that app, so wrap the entry point **and** each internal step **and**
  every tool-like callable, on the functions the app already has. One span
  around the top is the failure mode to avoid: it produces a clean, plausible
  trace that misrepresents a multi-step pipeline as a single unit, and nothing
  in the output reveals it. Each reference has the kind-by-kind table. When
  auto-instrumentation is doing the work instead, add none of them.
- **Python + LangChain or LlamaIndex: the meta package is a REQUIRED edit.**
  `langchain-core` / `llama-index-core` alone leave the instrumentor off: the
  app runs, LLM spans arrive, and no structure is ever emitted — silently.
  (Measured: 2 spans → 8 for LangChain, 1 → 17 for LlamaIndex, from that one
  line.) Add `langchain` / `llama-index` to the dependency file **alongside the
  `-core` package, never in place of it** — the app imports `langchain_core` /
  `llama_index.core` by name, so those stay declared. Say in your report that
  you added it and why; gate table in `references/python.md`.
- **Python: declare `httpx` alongside the SDK.** `traceloop-sdk` imports it
  without declaring it, so importing `progress.observability` dies with
  `ModuleNotFoundError: No module named 'httpx'` in a project with no other
  source. Most LLM SDKs provide it transitively; add it every time regardless.
- **Never ask the user to paste a key into the chat.** Reference the env var
  or config entry by name and let them set it themselves, in their own shell,
  `.env`, or secret store. Read config to detect what exists; never echo a
  secret's value back.
- **A missing key must be loud.** In Python and TS, read it so an unset
  variable raises (`os.environ["OBSERVABILITY_API_KEY"]`, not `.get(...)`).
  In .NET, follow the reference's optional-tracing pattern instead: skip init
  with a **prominent warning** and keep the app running. Either way, the one
  forbidden outcome is silence — an app that runs, exports nothing, and gives
  no clue why.
- **Keys via config, never hardcoded.** The key here is the **Integration** key
  (`ac_p_…`) from Progress Observability → API Keys. It is not the MCP key
  (`acm_…`) — that one belongs to the *coding agent's* environment for the
  verify step, not to the app.
- **Declaring the dependency is a file edit, and it is never optional.** Add the
  SDK to `package.json` / `requirements.txt` / `pyproject.toml` / `.csproj`
  yourself, creating the `dependencies` section if the manifest has none. Do
  this even when you have been told not to run installs, and even when you
  cannot run them — an import of a package the manifest doesn't declare is
  `ERR_MODULE_NOT_FOUND` the moment anyone runs the app. Telling the user to
  run `npm install …` afterwards is **not** a substitute for the edit; running
  the installer is the separate, optional half.
- **Check before adding.** If the SDK is already in the manifest, don't re-add
  it — note its version and move on (suggest an update only if it's older than
  the reference's verified version). Otherwise add the latest, through the
  project's *own* package manager where you can run it (each reference lists
  the commands), after a quick registry check of the current version — if the
  registry is ahead of the reference's "verified against" version, say so in
  your report rather than assuming the reference still holds.
- **`app_name` is the platform identity** — a short stable slug. Everything in
  the platform (and every other skill in this plugin) filters by it, so confirm
  it with the user. **Read it from the environment with the agreed name as the
  fallback**, rather than hardcoding it outright:

  ```python
  app_name=os.environ.get("OBSERVABILITY_APP_NAME", "my-app")
  ```

  Same shape in TS (`process.env.OBSERVABILITY_APP_NAME ?? 'my-app'`) and .NET.
  The literal still documents the intent, but the same build can then report as
  a different service per environment — dev, staging, prod, CI — with no code
  change. Hardcoding it means every environment lands in one bucket.

  **Add a variable; don't repurpose an existing one.** Where the app already
  names itself for its own reasons — an agent's `name:`, a service
  registration, a CLI banner — leave that alone and introduce a separate
  `app_name`. Pointing an existing field at your new env var makes the app's
  behaviour change with a telemetry setting, which is app logic edited for an
  instrumentation task.
- **Content capture default is ON.** Prompts/completions are sent unless
  content tracing is disabled. For apps handling sensitive data, offer the
  content off-switch (each reference shows it) — metadata keeps flowing.

## 3 · Run once

Have the user run the app so it emits at least one trace (run it yourself if it
is runnable here). If LLM credentials aren't available, the decorator/manual
span path in each reference produces real spans with **no** LLM call — wire one
`workflow`-decorated function and run that, so the pipeline can be proven
end-to-end before the model keys exist.

## 4 · Confirm & hand off — no platform read

Instrumentation is finished once the edits are in and the app has emitted at
least one trace. **This skill does not read back over MCP.** Confirming that the
spans actually landed is a separate, read-only step the user runs when they want
it — never something this skill does automatically. Do **not** call
`list_observations`, `get_observation_details`, or any other platform tool here.

Report what you already know from the edits themselves — the language, the
framework, and whether auto-instrumentation or manual spans are carrying the
trace — then tell the user plainly that wiring is done, and hand off with where
to confirm the traces:

> Wiring is complete. Run your agent so it produces some traffic, then open
> **observability.progress.com** and confirm the traces are flowing in for
> service `<app_name>` — they should appear within a minute of the run.

An unverified wiring is a normal, healthy outcome — especially for a new user on
the free tier, who has no MCP key to read traces with. Treating it as a failure
is a bad first experience for exactly the people most likely to be trying the
product for the first time.

Never touch the platform from this skill — no reads, no writes. The only changes
it makes are the local instrumentation edits.
<!-- copilot:end -->
