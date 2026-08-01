# Plan — .NET existing-OpenTelemetry: skill changes and a fixture

Written 1 Aug after probing the .NET SDK. Everything in "Measured" below is
established; do not re-derive it. The work items are what remains.

## Measured — treat as established

Probe: a console app with its own `TracerProvider` (in-memory exporter),
`Progress.Observability.Instrumentation` 1.2.2, `Microsoft.Extensions.AI`
10.8.1, `EchoChatClient` stub, no network.

1. **No ordering hazard.** `ObservabilityTracer.Initialize()` called *after* the
   app has already built its own `TracerProvider` returns OK, and the app's
   exporter keeps receiving its spans. .NET has no global provider to lose a
   race over — providers coexist, each with its own `ActivityListener`
   subscribing to sources by name. The Python `existing-otel` fixture must NOT
   be ported.

2. **The app's own tracing is untouched.** With `request` → `before_llm` →
   LLM call → `after_llm`, all three app spans stay in one trace and
   `Activity.Current` is correctly restored to `request` after the call.

3. **Progress emits into its own trace, deliberately.** `gen_ai.invoke_agent`
   is a root in a different trace from the app's activity, even with
   `Activity.Current` confirmed set. This is intentional: when the FICC
   activity has a non-Progress ancestor, the listener finds the nearest
   Progress ancestor, sets `Activity.Current = null`, and starts the agent span
   as a root — to avoid emitting a `parent_span_id` pointing at a span that
   lives in the customer's TracerProvider and never reaches the platform.

   Sound for .NET's two-provider architecture. Contrast Python, where Progress
   attaches to the app's provider, so the parent *does* reach the platform and
   nesting is safe — measured: `openai.chat` nests under the app's span, one
   trace. The two SDKs differ because the architectures differ, not because one
   is broken. **Do not file this as a bug.**

4. **Double instrumentation is real.** An app with `.UseOpenTelemetry()`
   already on the chat client, plus `.AddObservability()`, yields **two** chat
   spans for one call: `chat` from `Experimental.Microsoft.Extensions.AI` and
   `gen_ai.chat` from `Progress.Observability.AgentMonitoring`. Token counts,
   call counts and cost all double. The inner `chat` span is additionally
   orphaned into a third trace, because it runs while `Activity.Current` is
   nulled.

   This is not an SDK defect — the SDK does what it is told. It is the skill's
   job not to stack them.

5. **Source name** is `Progress.Observability.AgentMonitoring`. An app's own
   provider sees Progress spans only if it calls `.AddSource(...)` with that
   name. By default it will not.

6. **`OpenTelemetry >= 1.15.3` floor.** `Progress.Observability.Instrumentation`
   1.2.2 requires it; an app pinned lower fails `dotnet add package` with
   `NU1605` (observed with 1.9.0). Not in `dotnet.md`.

## Work item 1 — `references/dotnet.md` (canonical repo first)

Add, in house style — imperative, then fact, then consequence; no provenance,
no editorial framing, 79-char wrap:

- **A section on apps that already export OpenTelemetry.** Init ordering does
  not matter; providers coexist; the app's own tracing is unaffected. State
  plainly that Progress spans land in their own trace by design so nobody
  reports it as broken wiring, and that an app wanting them in its own backend
  adds `.AddSource("Progress.Observability.AgentMonitoring")`.
- **Do not stack `.UseOpenTelemetry()` and `.AddObservability()`.** Name the
  symptom: two chat spans per call, doubled tokens and cost. Include the
  detection grep.
- **The `OpenTelemetry >= 1.15.3` floor**, with `NU1605` named so the symptom
  is matchable.

## Work item 2 — `SKILL.md`

- Detect: add `.UseOpenTelemetry()` on a chat-client chain as a thing to look
  for, in the same slot as the Python meta-package check.
- Existing-telemetry rule: replace "TypeScript and .NET: not measured" with the
  measured .NET answer. TypeScript stays unverified-and-broken (see the Node
  bug report) until that SDK is fixed.

## Work item 3 — the fixture, and an open design question

Proposed: `agents/double-otel` in `instrument-test-dotnet` — an app with its
own `TracerProvider`, a console exporter, and `.UseOpenTelemetry()` already on
the chat client.

**Decide first what the correct answer is.** This is not obvious and should not
be settled in code before it is settled in the skill:

- (a) Add `.AddObservability()` anyway and accept the doubling — wrong, it
  inflates every metric.
- (b) Remove the app's `.UseOpenTelemetry()` — violates "never replace what is
  there without saying so first", and deletes telemetry the customer chose.
- (c) Wire Progress, leave the app's instrumentation alone, and report the
  overlap prominently so the user decides.

(c) matches the skill's existing principles, but a fixture cannot easily assert
on a *report*. Options: assert the diff leaves `.UseOpenTelemetry()` in place
AND adds `.AddObservability()`, and treat the doubling as expected-and-declared;
or make the fixture's app NOT have `.UseOpenTelemetry()` and instead test the
plain existing-provider case, keeping double instrumentation as a documented
Detect item only.

Resolve this before writing `expected/`.

**Assertion shape, if the fixture is built:** a **count**, not a presence check
— exactly one chat span per call. Nothing in the estate does counting
assertions today; every existing one checks that a span name appeared. A
presence check cannot catch doubling.

## Work item 4 — plumbing, once the fixture exists

- CI job in `instrument-test-dotnet/.github/workflows/e2e.yml` (needs a human
  commit; workflow files cannot be pushed from the agent session).
- Row in `NET_FIXTURES` in `scripts/skill_e2e_all.sh`. **This is the step that
  was missed for the Python fixture** — the harness silently ran eleven and
  reported a clean sweep. `TS_FIXTURES` and `NET_FIXTURES` have the same
  failure mode: a fixture with no row is invisible, not skipped.
- `routines/weekly-instrument-skill-e2e.md`: fixture count, a section for the
  new fixture, expected job counts per repo.

## Not in scope

- **Node.** Separate, real, and confirmed: `@progress/observability` 2.1.2
  attaches enrichment processors to an app's existing provider but never its
  exporter, so nothing reaches the platform. Bug report written. The TypeScript
  `existing-otel` fixture waits on that fix — building it now would encode the
  bug as the answer key.
- **Span links.** Worth suggesting to Progress as an enhancement: they already
  compute the ancestor's trace and span IDs, so an OTel `Link` would preserve
  correlation without dangling parents. Not a defect, do not file as one.
