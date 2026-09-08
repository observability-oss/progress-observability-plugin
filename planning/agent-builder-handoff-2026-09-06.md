> Historical checkpoint. See the [8 September handoff](./agent-builder-handoff-2026-09-08.md) for the current implementation and validation status. Original local evidence links and server URLs below refer to that earlier session.

**Agent Builder handoff — 6 September 2026**

The two new skills are implemented in the Progress Observability plugin and available for internal testing. The working tree is clean and the testing branch is pushed. There is no PR yet. The immediate next stage is colleague testing, integration with current `main`, and PR review.

**Repository and branch state**

- Repository: https://github.com/observability-oss/progress-observability-plugin
- Local checkout: `/Users/dipeykov/repos/progress-observability-plugin-build-template-agent`
- Testing/PR branch: [agent-builder-clear-pr](https://github.com/observability-oss/progress-observability-plugin/tree/agent-builder-clear-pr)
- Current pushed HEAD: `d34f734` — `chore: fix stalling when waiting for shell output`.
- Main implementation/cleanup commit: `5160cb7` — `chore: harden and streamline agent builders`. This builds on all earlier builder commits; the branch contains the complete package. The later stall fix must also be included when sharing it.
- Preserved development branch: `build-custom-agent`, at `d2516e3`. It retains the pre-cleanup planning and development material. It does not yet include the two cleanup/stall commits.
- Actual upstream integration branch is `main`, not `master`. After fetching on 6 September, the testing branch has 15 unique commits and current `main` has 7 unique commits. A `git merge-tree` simulation found no conflicts. No merge or rebase was performed.
- No PR or GitHub Actions run was found for this branch. The relevant workflow runs on pull requests and pushes to `main`, so an ordinary feature-branch push does not establish CI success.

**What the skills do**

`build-template-agent` copies a finished standalone .NET 10 application unchanged. The selected catalog entry controls the project and its three smoke cases. There is no design interview or source generation. It builds, runs the three live-model smokes, starts the UI on an OS-assigned loopback port, checks that exact process's health and root page, and returns the project path, local URL, smoke results/trace IDs, and one Progress Tracing-page link.

| Template ID | Implemented experience |
| --- | --- |
| `release-evidence-reviewer` | Markdown release evidence and policy; Ready, Blocked, or not-found outcomes; contextual follow-ups and New review. |
| `docs-qa` | Local lexical retrieval over synthetic Markdown documents; source-scoped Q&A, citations, previews, and follow-ups. |
| `operations-data-analyst` | Synthetic CSV metrics; deterministic calculations, comparisons and outliers; interactive charts, KPIs, and dashboard views. |
| `ticket-triage` | Synthetic JSON tickets plus Markdown policy; queue/priority recommendations, missing-information handling, and temporary intake scenarios. |

Ticket Triage replaced the earlier Access Request Reviewer idea. Release does not approve releases; Ticket does not update a ticketing system. Keep all four tested UI experiences and their execution behavior intact during further cleanup.

`build-custom-agent` starts with one short Purpose, asking for it only when absent. It infers Name, Knowledge, and at most three Actions, then follows one of these paths:

- **Local prototype:** show the proposal and require `Build proposed agent` or `Revise proposed agent`. An approved build customizes a separate fixed .NET 10 starter.
- **Complex/live request:** offer `Show implementation plan` and, when a meaningful local slice exists, `Propose a simplified mock PoC`. Otherwise offer plan/revise.
- **Simplified mock PoC:** selecting it produces a reduced proposal with explicit exclusions. A separate Build/Revise selection is still required before generating files.
- **Implementation plan:** return developer steps in chat, with no project, files, builds, smoke runs, server, or business-system calls.

Customizable surfaces are `AgentDefinition.cs`, up to three deterministic local/mock tools in `Tools.cs`, supported content under `docs/` and `data/`, bounded configuration, and an applicable `INTEGRATION_PLAN.md`. Runtime, packages, HTTP/UI, telemetry wrappers, and smoke engine remain fixed. UI settings choose knowledge, review, workflow, or analysis presets. Every content file is explicitly labeled `mock` or `supplied`.

The custom build validates before compilation and again after smoke against a frozen baseline. Required smoke IDs are `knowledge`, `tool`, and `not-found`. Its optional try-and-refine phase defaults to on: four sampled interactions covering task, follow-up, fresh chat, and boundary behavior, under one five-minute deadline. It permits at most one small evidenced repair followed by required revalidation. `Try-and-refine: off` skips only this optional phase.

**Architecture and cleanup completed**

- Both builders are independent skills inside the existing plugin, not separate plugin installations. They use neither `scaffold-agent` nor `dotnet-agent-starter`. Existing applications continue through `instrument-agent`.
- Builder workflows no longer need MCP, MCP credentials, or platform trace readback. Generated apps still use configured Azure OpenAI and Progress Integration settings.
- Metadata-only tracing explicitly omits prompts, answers, tool arguments/results, and raw exception text. Custom runtime execution and history are bounded.
- Content loading and provenance checks were tightened; undeclared or invalid content fails validation/startup. The validator checks fixed/editable boundaries and obvious prohibited capabilities, but is explicitly not a sandbox for arbitrary C#.
- Custom instructions and duplicate command/prompt surfaces were streamlined. Try-and-refine and its transport helper remain because they support actual customer behavior.
- Maintainer tests were moved out of distributed skill payloads into `scripts/tests`. They remain on the PR branch for CI; they were not all left exclusively on the development branch. Cloning the repository includes them, but generated customer apps and mirrored skill payloads exclude them.
- Development planning documents, redundant copies, the obsolete CI file, and the tracked `.DS_Store` were removed from the clean branch. The active workflow is `.github/workflows/check-copilot.yml`.
- Release fixtures were corrected, smoke checks strengthened, unused Operations fields/conversions removed, and formatting issues addressed. Canonical sources and generated Copilot payloads were synchronized.
- Verified again on 6 September: all four canonical prebuilt UI files are byte-identical to `d2516e3`.

**Latest issue: Copilot stalled after launching the app**

Copilot launched the custom app through detached shell `4`, then ended its turn saying it was waiting for the assigned loopback URL. The app was already running. A user follow-up caused Copilot to read the shell output, discover the URL, complete health/final validation, and finish with three passing smokes and 4/4 behavior checks.

Commit `d34f734` updates both skills to retain detached output, immediately read the returned shell ID in the same turn unless the URL is already available, poll for at most 30 seconds total, and stop on process exit. It prohibits waiting for server completion, a passive waiting handoff, and restarting a healthy server. Copilot mirrors and instruction-contract coverage were updated. This addresses the observed sequencing failure; a new full CLI run of the patched instruction is still the useful acceptance check. Static tests cannot prove model adherence.

**Validation evidence and its limits**

Freshly verified on 6 September at `d34f734`: 15 Python tests passed; Copilot generation and MCP-reference sync checks passed; `git diff --check` passed; the worktree is clean and remote HEAD matches local HEAD. The prebuilt UI comparison and non-mutating merge simulation also passed.

The updated Jira specifications record earlier implementation acceptance, not a fresh rerun on 6 September:

- Four prebuilt builds; 541 offline runtime assertions and 67 UI-state assertions.
- Three passing live-model smoke cases per template. Operations passed three consecutive final-code runs after its brittle prose assertion was corrected while retaining typed numeric/outlier checks.
- Copier, catalog, payload mirroring, and formatting checks passed.
- Five accepted custom CLI scenarios: missing-Purpose Benefits Q&A with 4/4 refinement checks; Jira advisor with a name-only revision; approved Kubernetes mock advisor; SAP/bank zero-write plan; and supplied-file On-call Q&A with exact provenance. All four built prototypes passed required gates. The preliminary case-01 was excluded in favor of accepted case-01b.
- No fresh browser click-through was performed after the final hardening adjustment; the prebuilt UI sources matched the previously tested versions.

Backend trace ingestion remains independently unverified. Local trace IDs, passing smokes, and sampled answers do not prove production integration behavior.

**Documentation and team handoff**

The Jira descriptions have already been updated and were read live on 6 September:

- [WAIF-415 — Create technical specification](https://progresssoftware.atlassian.net/browse/WAIF-415): status Committed.
- [WAIF-426 — Specify prebuilt agent path](https://progresssoftware.atlassian.net/browse/WAIF-426): status Under Consideration.
- [WAIF-427 — Specify custom agent path](https://progresssoftware.atlassian.net/browse/WAIF-427): status Under Consideration.

These descriptions cover the shipped catalog, routing, provenance, telemetry, validation, and accepted scenarios. They do not yet describe the later same-turn shell-output fix. WAIF-415 also retains the original technical-brief attachment; reconcile it before treating it as the current specification.

A Bulgarian Teams message was prepared with the branch link, clone/start commands, .NET user-secrets setup, four template prompts, and two custom prompts. The simplified mock PoC option is part of the complex path; selecting it proposes the reduced scope before Build approval. The message has not been sent by this agent. Progress Observability UI card/prompt text was prepared, but implementation in the external UI has not been verified.

Testing uses GitHub Copilot CLI, with `gpt-5.6-terra`, medium effort, and `--allow-all`. That flag grants broad tool/path/URL permission; the test folder is a working directory, not a security sandbox. App credentials are stored under .NET user-secrets ID `Progress.AgentBuilder.Mvp`: Azure endpoint/deployment, optional API key when Azure identity is configured, and a Progress Integration key. Copilot's model and the generated app's Azure deployment are separate settings.

**Next actions**

1. Gather colleague results for all four templates and both custom outcomes; include one fresh CLI acceptance run of the shell-output fix and the simplified-PoC proposal/Build sequence.
2. Integrate current `main`, preserve the tested UI behavior, regenerate derived Copilot/MCP files as needed, and rerun the workflow's checks. The clean merge simulation is not a completed integration or validation of a merged tree.
3. Align Jira with the launch fix and testing status; confirm the Progress UI uses the final four cards and simple prompts.
4. Open the PR from `agent-builder-clear-pr` to `main`, run CI, and obtain the two .NET reviews. Keep follow-up work focused on concrete defects and avoid broad orchestration or UI redesign.

Entry points in the checkout: `skills/build-template-agent/SKILL.md` and `templates.json`; `skills/build-custom-agent/SKILL.md`, `references/scope-routing.md`, and `references/try-and-refine.md`; `scripts/tests/`; `.github/workflows/check-copilot.yml`; `README.md`. Edit canonical skill sources and run `python3 scripts/build_copilot.py`; do not hand-edit generated mirrors.
