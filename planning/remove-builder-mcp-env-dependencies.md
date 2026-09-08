> Preserved development history from `d2516e3`. File layouts, TODOs and test counts below are historical. Start with the [current handoff](./agent-builder-handoff-2026-09-08.md) and [planning index](./README.md).

# Builder credentials and verification simplification

## Goal

Make both builder paths work from a clean folder with:

- .NET 10;
- Azure OpenAI settings stored with `.NET user-secrets`;
- the Progress **Integration** key stored with `.NET user-secrets`.

They must not require `.env`, `OBSERVABILITY_MCP_API_KEY`, or a Progress MCP
connection. The MCP setup remains available for the plugin's other skills.

## Simple customer flow

1. In the platform, copy one `dotnet user-secrets` setup block. It stores the
   four app settings under one shared ID, `Progress.AgentBuilder.Mvp`:
   `AzureOpenAI:Endpoint`, `AzureOpenAI:Deployment`, optional
   `AzureOpenAI:ApiKey`, and `Progress:Observability:ApiKey`.
2. Open Copilot in the desired empty folder. Copilot CLI uses
   `copilot --disable-mcp-server progress-observability`; in VS Code, leave that
   MCP server stopped. Paste the prebuilt or custom builder prompt.
3. Copilot copies/builds the project, runs three smoke cases, starts the UI,
   and returns one Progress Tracing-page link.

Secret values stay out of Copilot chat and generated project files. The UI
setup text must warn that .NET Secret Manager is for local development, not a
production secret store.

## Honest success contract

Without MCP, the builder can prove that:

- the project validates and builds;
- exactly three agent smoke cases pass;
- tracing is enabled and each smoke case emits a trace ID;
- the local UI health check passes.

It cannot independently prove that Progress received those traces. The result
must therefore say `local build verified; telemetry ingestion not independently
verified` and return one Tracing-page link for the user to open. A future
platform-side ingestion check may upgrade this result without requiring an MCP
key from the user.

## Implementation TODOs

### 1. One app-secret contract

- [x] Give both starter projects the shared `UserSecretsId`
  `Progress.AgentBuilder.Mvp`.
- [x] Keep `AddUserSecrets(...).AddEnvironmentVariables()` so deployment
  environments still work; no `Program.cs` runtime rewrite is needed.
- [x] Replace `.env` setup in both starter READMEs and platform prompt copy with
  `dotnet user-secrets --id Progress.AgentBuilder.Mvp` commands.
- [x] Remove the custom starter's `.env.example` and its validator allowlist
  entry and `.gitignore` exception. Keep every `.env*` path ignored/rejected so
  secrets cannot enter generated output.

### 2. Prebuilt path

- [x] Remove the MCP reference, lookup, retries, and trace-verification gate
  from `skills/build-template-agent/SKILL.md`.
- [x] Gate success on unchanged-template copy, build, three passing smoke cases,
  and UI health.
- [x] Return the three smoke results and IDs, one Tracing-page link, and the
  explicit ingestion-verification limitation.

### 3. Custom path

- [x] Apply the same no-MCP verification contract to Mode 1 local prototypes in
  `skills/build-custom-agent/SKILL.md`.
- [x] Keep Mode 2 plan-only zero-write and credential-free.
- [x] Preserve the current scope router, editable-file allowlist, validator,
  three smoke cases, and local/simulated-data boundary.

### 4. Packaging and documentation

- [x] Remove only `build-template-agent` and `build-custom-agent` from
  `MCP_SKILLS` in `scripts/sync_skill_refs.py`, then delete their generated
  `references/mcp.md` files.
- [x] Update both builder commands, root README, Copilot instruction template,
  and generated Copilot mirrors. Keep the plugin-level `.mcp.json` and MCP docs
  for the other observability skills.
- [x] Keep `/health-check` as a separate optional follow-up for customers who
  have MCP access; neither builder invokes it or depends on it.
- [x] Update this feature's earlier planning text so it no longer calls MCP or
  `.env` a builder prerequisite.

### 5. Acceptance tests

- [x] Unit-test the shared secret ID, absence of `.env.example`, validator
  behavior, and the new result wording.
- [x] Add one small builder-contract test covering the two secret IDs, retained
  `AddUserSecrets`, absent builder MCP references, and custom `.env.example`
  removal. Keep the generic Copilot packager's `.env.example` behavior.
- [x] Run prebuilt and custom Mode 1 from fresh folders with no `.env` and
  `OBSERVABILITY_MCP_API_KEY` unset. Require build, three smoke passes, and a
  healthy UI; confirm that neither skill calls an MCP tool.
- [x] Run custom Mode 2 with no credentials. Require a chat-only plan and no
  created files.
- [x] Test full-plugin startup without an MCP key. Copilot CLI logs a non-blocking
  authentication warning unless the server is disabled, so the builder launch
  uses `--disable-mcp-server progress-observability`; no package split is needed.
- [x] Run copier/helper tests, both starter builds, Copilot mirror checks,
  reference-sync checks, and `git diff --check`.

## Explicitly unchanged

- Azure OpenAI remains the only model-provider adapter in this MVP.
- The Progress Integration key remains required for Mode 1 smoke tracing.
- Live external integrations and real side effects remain outside the custom
  prototype path.
- The plugin's paid MCP features and MCP-dependent observability skills remain
  available; they are simply not prerequisites for building an agent.
