# Weekly instrument-agent E2E check — steps 1-4

The scheduled routine carries only step 0 (repo access) inline and then reads
this file. Keeping the body here means the check is version-controlled,
reviewable in a diff, and editable without touching the trigger — which matters,
because a trigger's prompt can only be edited from the session it fires into.

Step 0 has already run by the time you are reading this: the five repos are
confirmed reachable and their paths are in `REPO_PLUGIN`, `REPO_SKILLS`,
`REPO_PY`, `REPO_TS`, `REPO_DOTNET`.

## Repo layout

Skill content is canonical in `observability-oss/observability-skills`;
`progress-observability-plugin` carries a synced copy plus the packaging and the
harness. A daily job (07:23 UTC) opens a PR from canonical into the plugin.

**Test the plugin's copy** — that is what users install — and separately report
if the two have drifted (`scripts/sync_canonical_skills.sh --check`; non-zero
means an unmerged sync PR or an edit made in the wrong repo).

## 1 · Skill structural test

Refresh each clone (`git -C $REPO_X pull`). Read the current
`skills/instrument-agent/SKILL.md` + references **from the plugin repo**, then
produce, for all nineteen fixtures, the diff the skill implies, and score each
against `expected/<fixture>`. PASS/FAIL with a one-line reason each.

### Delegate the applying — not optional

**Do not produce the diffs yourself.** This routine fires into a persistent
session, so by the second run your own context holds previous runs' `expected/`
contents, verdicts and reasoning. Any diff you write after that is not blind, no
matter how carefully you try to ignore what you remember.

For each fixture spawn a subagent (Task tool) whose entire input is: the skill
text (`SKILL.md` + the relevant language reference), the fixture path, and the
instruction to apply detect+wire and report the resulting file contents. A
subagent starts with no conversation history — that is what makes the
application blind. Then **you** score the returned diffs against `expected/`:
seeing the answer key is correct at scoring time and wrong at applying time.
Batch however you like; one subagent per fixture or per language both work.

Each subagent's prompt must forbid reading `expected/<fixture>` and the fixture
repo's `README.md`. Both contain the answers — the READMEs document the wiring,
the expected span names and the service-name convention, which is why the local
harness moves `README.md` out of the tree alongside `expected/` for the agent's
turn. A Copilot run on `agents/hosted` read the README's "The trap in hosted"
section and cited it, so that run proved nothing.

**Read the diff, not just the outcome.** Every criterion below was found in a run
that would otherwise have scored a clean pass — the app ran, spans arrived, the
assertion matched, and the produced code was still wrong. A verdict cannot catch
these; only the diff can.

### The nineteen

- **Python (11)** — keyless, openai, langchain, langgraph, crewai,
  openai-agents, llamaindex, agno, haystack, mcp, googlegenai
- **TypeScript (4)** — keyless, commonjs, openai, langchain
- **.NET (4)** — keyless, openrouter, agent, hosted

Baseline criteria: init/bootstrap ordering, env-based keys,
decorators/wrappers/`AddObservability` placement, `httpx` for Python, dynamic
langchain imports for TS ESM, `.AddObservability()` between `AsIChatClient()`
and `AsAIAgent()` for dotnet-agent, app logic untouched.

### Module-scope frameworks

langgraph, crewai, openai-agents, llamaindex, agno, mcp each build their objects
at **module scope** (compiled graph, module-level `Agent`/`Crew`/`Task`, global
`Settings.llm`, decorator-registered MCP tools). The pass criterion is that
`Observability.instrument()` lands **before the framework import** — not merely
before a client call — with `noqa: E402` on the moved imports, `httpx` declared,
no unnecessary decorators added, and app logic untouched.

### Haystack is the one exception to init-before-import

Do not score it by the rule above. `haystack/tracing/tracer.py` calls
`auto_enable_tracing()` at module scope while `haystack/tracing/__init__.py` is
still on its first import line; if a real OTel provider already exists it probes
with a live span named `haystack.tracing.auto_enable`, then imports from the
half-initialised module, raises `ImportError`, records it on that span and
exports it. The app still works (`haystack/__init__.py` calls
`auto_enable_tracing()` again once loaded) but you get one bogus ERROR trace per
run forever.

The correct pattern, which `expected/haystack/app.py` encodes: `import
haystack.tracing` **first**, then `Observability.instrument()`, then an explicit
`haystack.tracing.auto_enable_tracing()`, then the `from haystack import …`
lines. Plain init-before-all-haystack-imports is a FAIL.

### googlegenai is not an ordering test

Measured 29 Jul: the instrumentor patches `google.genai.models` on the **class**,
and `CLIENT.models.generate_content` resolves through the class at call time, so
a `Client` built at module scope before `instrument()` is still traced. Do not
fail this fixture for init-after-import, and do not accept a code comment
claiming a pre-init client goes untraced — that claim is false and a test agent
has already copied it once.

What it actually tests: the dependency file keeps `google-genai` (the legacy
`google-generativeai` distribution satisfies no instrumentor and is a FAIL if
substituted), `progress-observability` and `httpx` are added, and `shutdown()`
runs in a `finally`.

### commonjs tests flush timing

`agents/commonjs` is the only non-ESM TS fixture. Pass requires: a bootstrap file
with **no** register/hooks import (those intercept ESM resolution only),
`require('./src/app')` **after** `instrument()`, the flush registered on
`process.on('beforeExit')` and **not** called straight after `require()`, and a
catch that reports, flushes, then exits non-zero via a deferred exit. Flushing
after `require()` drops every span from a floating `main()` and still passes a
bare `llm_call` assertion. `commonjs-pipeline` exists as of 30 Jul but asserts
`llm_call` only, which by the above cannot catch this — it proves the CommonJS
bootstrap path, nothing about flush timing. The diff is still the only thing
checking the trap.

### hosted tests DI placement

`agents/hosted` uses `Host.CreateApplicationBuilder` + `AddChatClient` +
`UseFunctionInvocation`. Pass requires `.AddObservability()` **inside the
`AddChatClient` factory**, wrapping the `IChatClient`, with `Shutdown` registered
on `IHostApplicationLifetime.ApplicationStopping`. On the builder it is a compile
error (CS1929); anywhere else that compiles it still yields `llm_call` and
silently loses every tool span.

`hosted-pipeline` was added on 30 Jul and asserts `llm_call,tool` — the `tool`
half is what makes it catch a misplaced `.AddObservability()`, since the wrong
placement still yields `llm_call`. It passed first run. If that job is ever
reduced to `--expect llm_call`, the fixture stops testing anything and the diff
becomes the only check again.

### Seven diff-level criteria

Each came from a real miss. Score every fixture against the ones that apply and
report which failed by name.

**(a) Meta package present.** traceloop enables a framework instrumentor only
when a distribution of a specific name is installed: LangChain needs `langchain`
or `langgraph`; LlamaIndex needs `llama-index`; Haystack checks `haystack` but
the real distribution is `haystack-ai`, so that gate can never pass. An app on
`langchain-core` or `llama-index-core` alone instruments cleanly, runs fine,
emits LLM spans and **no structure**, silently. Measured: adding it moved
langchain from 2 spans to 8 and llamaindex from 1 to 17. For `agents/langchain`
and `agents/llamaindex` the skill must add the meta package to
`requirements.txt`. Omitting it is a FAIL even if everything else is perfect.

**(b) Added alongside, not swapped.** One run replaced `langchain-core` with
`langchain` rather than adding it. That passes the assertion, because the
umbrella pulls core in transitively — but the app imports `langchain_core`
directly and must keep declaring it. Removing a directly-imported package is a
FAIL.

**(c) Missing key fails loudly — Python and TS only.** They must read the key so
an unset variable raises: `os.environ["OBSERVABILITY_API_KEY"]`, not
`os.environ.get(...)`. One run used `.get()`, which passes `None`: the app
starts, runs, exports nothing, and gives no error explaining why. This never
shows up in a verdict because the harness always sets the variable.

.NET is the deliberate exception — the optional-tracing pattern (skip init, warn
loudly, keep running) is correct there, and as of 30 Jul all four .NET
`expected/` fixtures encode it. It gates **both** touch points: a bare
`.AddObservability()` self-initializes from `PROGRESS__OBSERVABILITY__APIKEY` and
throws when that is unset, so `if (tracing)` guards the attach and the `Shutdown`
registration as well as `Initialize()`. A .NET diff that gates only
`Initialize()` is a FAIL — it still dies on a missing key, inside the DI factory
for hosted. Model credentials (`OPENROUTER_API_KEY`) still throw and should.

All four rewritten fixtures built and traced green on 30 Jul — `hosted` on
`llm_call,tool`, `agent` on `llm_call,tool`, `openrouter` and `keyless` on
`llm_call`. The gating is CI-verified, not inferred from the assembly.

**(d) `@tool` versus `@task`.** Functions that fetch something or cause an
external effect are `@tool`; functions that transform data in-process are
`@task`. `agents/keyless` has `fetch_ticket`, which must be `@tool`. One run made
it `@task`, which still emits a span and still passes the assertion, so only the
diff catches it.

**(e) `app_name` from env with fallback.**
`os.environ.get("OBSERVABILITY_APP_NAME", "<slug>")` — not a hardcoded literal,
so one build can report as different services per environment. This is the one
place `.get()` is correct. All three SDKs default it to something nobody chose
when omitted (`sys.argv[0]` in Python, `process.argv[1]` or the literal
`app name` in Node, `Agent` in .NET), so an omitted `app_name` is a FAIL, not a
style point.

**(f) No repurposed identity field.** Where the app already names itself for its
own reasons — an agent's `name:`, a service registration, a CLI banner — the
skill adds a variable rather than redirecting the existing one. Pointing the
Agent Framework's `name:` at the observability app name makes the app's identity
follow a telemetry env var: app logic edited for an instrumentation task, a FAIL.

History worth knowing: `references/dotnet.md`'s own `AsAIAgent` example used
`name: appName`, `expected/agent/Program.cs` copied it, and so the answer key
encoded the very defect this criterion forbids — a correct diff scored a mismatch
there. Both fixed 30 Jul (observability-skills#2, instrument-test-dotnet#1). If
`name: appName` reappears in either place, that is a regression, not the
reference.

**(g) No gratuitous `AddToolObservability()`.** The .NET reference documents it
for apps that dispatch tools without function-invocation middleware. All four
.NET fixtures use `UseFunctionInvocation` or `AsAIAgent`, where it is redundant.
Adding it there is over-application — flag it.

### Also flag

A subagent running git commands or committing (the harness manages version
control and the prompt forbids it), or a diff that adds MCP verification code or
calls the platform — the skill does not read the platform back, confirming
traces is `/health-check`'s job.

Reset `agents/` (`git checkout -- agents/`) in each repo when done, and do not
commit or push fixture edits. `collector.observability.progress.com` and
`mcp.observability.progress.com` are proxy-blocked — never run the apps or the
harness locally.

## 2 · CI results

Via the GitHub MCP tools, read the most recent run conclusion for: `e2e` in all
three fixture repos (crons Monday 06:17 python / 06:23 ts / 06:29 dotnet UTC),
and the frameworks matrix in `instrument-test-python` (cron 06:41 UTC). The
matrix has nine jobs — langchain, langgraph, crewai, openai-agents, llamaindex,
agno, haystack, mcp, googlegenai — and fail-fast is off, so report per-job
status. Each asserts its **measured** span shape, not a bare `llm_call`, so a
failure means depth was lost.

Also read the plugin's `sync-skills` workflow (daily 07:23 UTC): a failed `drift`
job means someone edited a skill in the plugin instead of canonical.

**Coverage — closed 30 Jul.** All nineteen fixtures now have a CI job.
`hosted-pipeline` (dotnet, `llm_call,tool`), `openai-pipeline` and
`commonjs-pipeline` (ts, `llm_call`) were added that day and all passed first
run. `dotnet/e2e.yml` should show four jobs and `ts/e2e.yml` four; fewer means
something was reverted, and that is worth reporting. Note the TS pair gates on
`ENABLE_LANGCHAIN_E2E`, which now controls three jobs despite the name — if it
is ever renamed, the new variable has to be set *before* the rename lands or
all three skip silently.

**Known process gap — confirm, don't rediscover.** The sync PRs are opened by
`github-actions[bot]` on `sync/canonical-skills` and have been merging within a
minute of creation with their `pull_request` checks in `action_required`, i.e.
`drift` and `check-copilot-instructions` never ran. The PR body promises a human
reads the diff and nothing enforces that. Flag if still true.

`actions_list` on these workflows can return a payload too large to read — parse
the saved tool-result file with python rather than retrying. On failure pull the
failing step's log excerpt (`get_job_logs`, `failed_only`). Transient OpenRouter
502s at the "Run instrumented `<fw>` fixture" step are provider noise, not a
regression — `rerun_failed_jobs` once and say so. If a workflow or its
secrets/variables are missing (`ENABLE_FRAMEWORK_PROBES`,
`ENABLE_LANGCHAIN_E2E`, `ENABLE_OPENROUTER_E2E` unset — a weekly run that
silently skips most of its matrix is worse than none), report SETUP NEEDED.

Dispatch via `actions_run_trigger` only if no run exists today; on an
off-schedule manual fire, do not spend four workflows' worth of live LLM calls —
say you skipped it. A 404 on a repo confirmed OK in step 0 means the GitHub MCP
scope and the git-proxy scope disagree; report that rather than calling the
workflow missing.

## 3 · Drift check

- **pypi.org** — `progress-observability` latest + its `traceloop-sdk` pin
- **registry.npmjs.org** — `@progress/observability` latest + its
  `@traceloop/node-server-sdk` dep
- **api.nuget.org** — `Progress.Observability.Instrumentation` latest

Compare against **both** the "Verified against" line in the plugin's
`skills/instrument-agent/references/*.md` **and** the `metadata.verified-against`
block in `SKILL.md`'s frontmatter — they must agree with each other as well as
with the registries. Report newer releases as drift needing re-verification.
`progress-observability` pins `traceloop-sdk` exactly, so no upstream fix reaches
users without a new Progress release.

Five upstream bugs we work around — re-check each on a relevant bump, since a fix
means we can simplify:

1. the Haystack gate looking for `haystack` rather than `haystack-ai`
2. `HaystackInstrumentor._instrument()` walking its wrap list with no
   try/except, with the 3.x-removed `OpenAIGenerator` first
3. the module-scope `auto_enable_tracing()` call in `haystack/tracing/tracer.py`
   — if upstream drops it, the haystack fixture's import dance can revert to the
   normal rule
4. whether the LangChain/LlamaIndex gates still require the meta package — if
   they accept `-core`, criteria (a) and (b) can be relaxed
5. the Node SDK's LangChain takeover — it resolves the consumer's
   `@langchain/core`, which npm hoists to top level as a transitive dep of the
   SDK itself, so it fires in projects with no LangChain and disables the
   provider instrumentors unless `OPENAI`/`AZURE_OPENAI` is named in
   `instruments`. If a release past 2.1.2 fixes this, the whole `instruments`
   workaround in `references/typescript.md` becomes optional.

Three Progress bugs to re-check on a version bump. Two reported:
`progress-observability` is unimportable after a clean install because
`traceloop-sdk` imports `httpx` without declaring it; and
`ModelFixProcessor._apply_attribute_fixes` throws on every google-genai span,
logging an empty message because the handler formats `{e}` with no exception
type. One unreported **and never verified by execution**: a no-arg
`.AddObservability()` appears to throw when no key is reachable, which is why the
.NET optional-tracing pattern gates the attach — if a release makes that a no-op,
criterion (c)'s .NET clause can be simplified.

`mcp` is pinned `<2` in the mcp fixture — mcp 2.0 removed `mcp.shared.session`,
which the instrumentor patches while its gate stays `mcp >= 1.6.0`, so on 2.x it
enables, throws, gets swallowed, and emits nothing. Unpin when the instrumentor
supports 2.x.

Flag if any framework fixture's job failed on a dependency/API break rather than
an instrumentation problem — agent frameworks churn fast and that is expected
noise, not a skill regression.

## 4 · Report + notify

Open with step 0's OK/DENIED table, then the compact summary: nineteen
structural verdicts (grouped — Python 11, TypeScript 4, .NET 4), the seven
diff-level criteria called out separately with any fixture that failed each, CI
results (three e2e + the frameworks matrix per-job, nine jobs, + the sync drift
job), canonical-vs-plugin alignment, drift list or none. **Lead with failures** —
a repo denial or a missing GitHub MCP is the headline if either happened. Keep it
under ~50 lines.

State plainly that the diffs came from subagents. If for any reason you produced
one yourself, say which, and treat its verdict as unproven.

The README depth matrix in `instrument-test-python` is fully populated and every
row is pinned as a CI assertion, so the job is to **confirm** it: if a probe or
assert reveals a span shape contradicting a recorded row, say so loudly — that is
either a regression or a genuine framework change.

One check CI cannot do, because the harness matches span names and not status:
for service `ins-test-py-haystack-ci`, look for any observation named
`haystack.tracing.auto_enable` with a non-success status in the run window. Its
return means the fixture's import ordering regressed. If no platform MCP server
is available this turn, mark it NOT CHECKED rather than skipping it silently.

Then send a one-line PushNotification: all-green
(`weekly skill e2e: all green`) or the failure headline.

**Do not create issues, PRs, or push anything anywhere.**
