# TypeScript / JavaScript — `@progress/observability`

Verified against `@progress/observability` 3.1.0 on npm (wraps
`@traceloop/node-server-sdk` 0.27.0; OpenTelemetry 2.x underneath). Prefer the
published package over this file where they disagree. 3.x changed the init
defaults: the `instruments` workaround that 2.1.2 needed is gone, and a
LangChain rule took its place — see *`instruments` is optional again*.

```bash
npm install @progress/observability
```

Match the project's manager: `yarn add` · `pnpm add`. Current published
version: `https://registry.npmjs.org/@progress/observability` →
`.dist-tags.latest`.

**Public npm blocked?** Use the org's internal mirror if one exists. Otherwise
the offline-cache flow works — verified: the SDK's ~233-package tree is pure
JS, no native binaries, so a cache built anywhere installs anywhere. Connected
machine: `npm install @progress/observability --cache ./npm-cache`; ship
`package-lock.json` + `npm-cache/`; target: `npm ci --offline --cache
./npm-cache`.

**First decision: module system.** `package.json` has `"type": "module"` →
ESM, and loader hooks must register *before the app loads*. Otherwise
CommonJS, where init is synchronous and the hooks import is not used. **Both
get a bootstrap file** — the difference is what goes in it, not whether you
need one. Getting this wrong is the #1 "no spans" cause in Node.

## ESM (`"type": "module"`) — bootstrap file (preferred)

Create `bootstrap.ts` **in the same directory as the `package.json` you are
editing** — in a monorepo the sub-project's, never the repo root. `npm start`
resolves the script path relative to that `package.json`; anywhere else is
`ERR_MODULE_NOT_FOUND` the moment the app runs. It becomes the new entry:

```typescript
import '@progress/observability/register/hooks';   // must be first

import { Observability } from '@progress/observability';

const apiKey = process.env.OBSERVABILITY_API_KEY;  // Integration key, ac_p_…
if (!apiKey) throw new Error('OBSERVABILITY_API_KEY is not set');

await Observability.instrument({
  appName: process.env.OBSERVABILITY_APP_NAME ?? 'my-app',   // platform identity
  apiKey,
});

await import('./src/app.js');                      // load the real app AFTER init
```

**Copy the block as-is**, including the checked const: `process.env.X` is
`string | undefined`, and an unset key passed through as `undefined` starts an
app that runs and exports nothing. Add `import 'dotenv/config';` after the
hooks import **only if `dotenv` is already in `package.json`** — a speculative
import is `ERR_MODULE_NOT_FOUND` at startup under ESM.

**Always set `appName`.** Omitting it is not an error: it falls back to
`process.argv[1]`, then to the literal string `app name`. Either way the
service arrives under a name nobody chose.

`OBSERVABILITY_APP_NAME` overrides whatever the code passes, so the `??` above
is documentation rather than mechanism. It also means a value in the
environment silently beats an explicit `appName`.

### `instruments` is optional again (3.x) — but LangChain must be *declared*

**The 3.x default init detects the provider SDKs the app has installed and
instruments them.** Measured on 3.1.0: a plain `openai` app with no
`instruments` option emits `chat <model>` spans, ESM and CommonJS alike. The
2.1.2 rule — name every provider or get zero spans — no longer applies; do
not add `instruments` to a plain-provider app.

Detection resolves each package from the SDK's own location, so it finds
whatever is installed at the consumer level: `openai`, `@anthropic-ai/sdk`,
`cohere-ai`, `@aws-sdk/client-bedrock-runtime`, `@google-cloud/vertexai`,
`@google-cloud/aiplatform`, `@google/genai`, `together-ai`,
`@modelcontextprotocol/sdk`, `llamaindex` (+ `@llamaindex/openai`),
`@pinecone-database/pinecone`, `chromadb`, `@qdrant/js-client-rest`.
Anything outside that table is left to traceloop's import-hook path, which is
not measured here — if such an app shows no spans, run once with
`debug: true` and look for `Detected consumer provider:`.

**LangChain: `@langchain/core` must be declared in the app's own
`package.json`.** The hierarchy patch now keys off that declaration — read
from the `package.json` in `process.cwd()` — not off whether the module
resolves. Measured on 3.1.0, same chain both ways:

- `@langchain/core` declared → `workflow RunnableSequence` →
  `workflow ChatPromptTemplate` / `workflow StrOutputParser` + `chat <model>`.
- only `@langchain/openai` declared (core arrives transitively) → the single
  `chat <model>` span and **nothing else**, no error. The debug line is
  `@langchain/core not found in consumer package.json — treating as absent`.

So for a LangChain app, adding `@langchain/core` to `dependencies` when it is
missing is a **required edit** — the JS counterpart of the Python meta-package
rule — and in a monorepo the process must start from the directory that holds
that `package.json`.

`instruments` / `blockInstruments` still exist and still mean what they did:

- `instruments` is an **allow-list** — pass it and anything omitted is off,
  LangChain included. Only pass it when the app genuinely needs narrowing.
- `blockInstruments` is the tool for **double counting**: a framework that
  reports its own model calls *and* drives an instrumented provider SDK
  records each call twice. Genkit on `@genkit-ai/compat-oai` is the measured
  case (see *Genkit*); block the provider, keep the framework.

Run with `tsx bootstrap.ts` (or node with a TS loader). Alternative without a
bootstrap file: hooks import at the very top of the entry point, instrumented
libraries loaded via *dynamic* import after `instrument()` — LangChain in
particular:

```typescript
const { AzureChatOpenAI } = await import('@langchain/openai');
```

## CommonJS — bootstrap file (no hooks import)

Create `bootstrap.ts` in the same directory as the `package.json` you are
editing and point the `start` script at it — same location rule as ESM. Do
**not** put `instrument()` at the top of the app file instead: TypeScript
hoists every `import` into a `require` above the first statement, so an
instrumented library is loaded and its methods resolved *before* `instrument()`
runs, and nothing is patched. Measured, same file both ways — hoisted import:
not patched; bootstrap: patched.

```typescript
// bootstrap.ts — `npm start` runs this, not the app
import { Observability } from '@progress/observability';

const apiKey = process.env.OBSERVABILITY_API_KEY;
if (!apiKey) throw new Error('OBSERVABILITY_API_KEY is not set');

Observability.instrument({
  appName: process.env.OBSERVABILITY_APP_NAME ?? 'my-app',
  apiKey,
});

// flush on beforeExit — require() returns before a floating main() finishes
let flushed = false;
const flush = () => (flushed ? undefined : ((flushed = true), Observability.shutdown()));
process.on('beforeExit', flush);

try {
  require('./src/app');          // AFTER init — a hoisted import is too early
} catch (err) {
  console.error(err);
  void Promise.resolve(flush()).finally(() => process.exit(1));
}
```

Three differences from the ESM bootstrap: no `register/hooks` import (those
intercept ESM module resolution), `require()` instead of `await import()` (no
top-level await), and the flush on `beforeExit` rather than in a `finally`.

**Flush on `beforeExit`, never straight after `require()`.** `require()`
returns as soon as the module body has run, and a CommonJS entry cannot
`await` — so it calls an async `main()` bare and is still in flight at that
point. Flushing there drops every span not yet produced. `beforeExit` fires
once the loop drains, and unlike `exit` it can await. Measured both ways.

**Keep the `catch`.** `beforeExit` does not fire when the process dies on a
throw, and `shutdown()` is async — rethrowing immediately kills the process
mid-flush, so report, flush, then exit non-zero.

Leave the app's own `import` statements alone, and do not restructure its entry
point to make it awaitable: the bootstrap handles a floating `main()` as it is.

**Ignore the `register/hooks was not loaded` warning.** The SDK prints it on
every CommonJS run — it fires whenever the hooks are absent, but its advice
applies only to ESM, and spans arrive normally (verified). Adding the hooks
import to silence it achieves nothing.

## Three rules that decide whether anything is captured

Get any of these wrong and the wiring silently does nothing:

1. **The hooks import executes first** — not "near the top", first. Anything
   imported ahead of it is already resolved and cannot be patched.
2. **ESM: import LangChain dynamically in the app, after `instrument()`.**
   Convert `import { ChatOpenAI } from '@langchain/openai'` to
   `const { ChatOpenAI } = await import('@langchain/openai')`. This is **in
   addition to** the bootstrap, not instead of it — it is the configuration
   verified end-to-end. A static top-of-file import can resolve before init and
   yield flat or missing spans. LangChain is the one framework for which
   touching the app's imports is expected.
3. **`await Observability.shutdown()` before the process exits**, in the
   `finally` under *Flush on exit* — never a handler assembled by hand.

## Env vars

`OBSERVABILITY_APP_NAME`, `OBSERVABILITY_API_KEY`, `OBSERVABILITY_ENDPOINT`
(has a default — usually omit), `OBSERVABILITY_TRACE_CONTENT=false` (stop
sending prompt/completion text; metadata still flows).

Useful `instrument()` options: `traceContent: false`, `additionalTags: […]`,
`instruments` / `blockInstruments` (a `Set` of `ObservabilityInstruments`),
`debug: true`.

## What auto-instruments

`ObservabilityInstruments` (from the package's types, v3.1.0): OPENAI,
ANTHROPIC, COHERE, BEDROCK, **AZURE_OPENAI**, VERTEXAI, SAGEMAKER, OLLAMA,
GROQ, MISTRAL, TOGETHER, REPLICATE, ALEPHALPHA, GOOGLE_GENERATIVEAI,
TRANSFORMERS, WATSONX, LANGCHAIN, LLAMA_INDEX, CREW, HAYSTACK, OPENAI_AGENTS,
MCP, PINECONE, CHROMA, WEAVIATE, QDRANT, MILVUS, LANCEDB, MARQO, REDIS, MYSQL,
REQUESTS, URLLIB, plus TASK and WORKFLOW (3.x; span kinds for the decorators,
not instrumentors). Genkit is not a member and needs no flag — 3.1.0 enriches
its spans through an always-on processor; see *Genkit* below.

**For Google, check which SDK the app imports.** `GOOGLE_GENERATIVEAI` hooks
**`@google/genai`** (≥1.0 <2.0), the unified SDK. `VERTEXAI` covers
`@google-cloud/vertexai` (≥1.1) and `@google-cloud/aiplatform` (≥3.10). The
enum member is named after a package it does not instrument, so read
`package.json`, not the enum. Name whichever applies in `instruments`, like any
other provider.

**Any OpenAI-compatible endpoint counts as OPENAI** (CI-verified): OpenRouter,
LiteLLM, vLLM, Together, Fireworks, most gateways — via `baseURL` in the
OpenAI SDK, `configuration: { baseURL }` in LangChain. The platform records
the real provider. So "my provider isn't on the list" is usually wrong — check
whether it speaks the OpenAI API.

**Not auto-instrumented: LangGraph.js, Mastra, the Vercel AI SDK.**
LangGraph.js does **not** ride the LangChain instrumentor — a LangGraph app
gets no graph structure from it. Say so plainly, then offer
`wrapFunctionWithSpan` or the decorators below for structure. Genkit is the
exception among the frameworks — covered from 3.1.0 through its own
OpenTelemetry spans, with an ordering rule; see *Genkit*.

## The app already sets up OpenTelemetry — its provider first, then `instrument()`

Grep for `NodeTracerProvider`, `NodeSDK`, `.register(`, or an
`@opentelemetry/sdk-*` dependency before writing anything. When the app owns a
`TracerProvider`, the order of the two inits decides who receives what.
Measured on 3.1.0 with `@opentelemetry/sdk-trace-node` 2.11 — an in-memory
exporter on the app side, an OTLP sink on ours, one `openai` call plus one
span from the global tracer and one from `provider.getTracer()`:

| Order | App's exporter | Progress |
|---|---|---|
| app `provider.register()` → `instrument()` | every span | every span |
| `instrument()` → app `provider.register()` | only `provider.getTracer()` spans | only global-tracer spans (the provider SDK's included) |

**Register the app's provider first, then call `instrument()`.** The SDK
detects the existing global provider, logs `A TracerProvider was already
registered before instrument() was called …`, and pushes its own OTLP
processor onto the app's provider — the app's exporter, Progress and the
enrichment processors all sit in one pipeline. Batched and unbatched both
flush on `Observability.shutdown()`; the app's own `provider.shutdown()`
flushes Progress as well, since our processor is in its list.

The other order is a documented limitation, not something to work around: the
app's later `register()` is refused by the OTel API (`Attempted duplicate
registration`), so its instance tracer and the global tracer diverge and the
telemetry splits down the middle. Move the init instead.

**Apps on OpenTelemetry 1.x: the recommended order silently sends nothing to
Progress.** Measured on `@opentelemetry/sdk-trace-node` 1.30.1: the attach
succeeds and the warning above prints, but the processor the SDK pushes is
built from its own 2.x exporter, whose serializer reads
`span.instrumentationScope` — a 1.x span only has `instrumentationLibrary` —
so every export throws `TypeError: Cannot read properties of undefined
(reading 'name')` inside `@opentelemetry/otlp-transformer` and is dropped.
The app's exporter is unaffected, and the error is visible **only** with
`debug: true`. Reported 6 Sep 2026. Until it is fixed: upgrade the app's
`@opentelemetry/*` to 2.x if that is on the table; otherwise say so plainly
and offer the trade — `instrument()` first keeps Progress fed (global-tracer
spans, the provider SDK's included) at the cost of the split above. Never
report an OTel-1.x app as wired on the strength of the attach warning.

Whether `appName` survives the attach (in Python it does not — spans carry the
app's resource) is not measured; confirm the service name in verify.

## Genkit (3.1.0+)

Genkit emits its own OpenTelemetry spans (`genkit:type`,
`genkit:metadata:subtype` = `flow` / `tool` / `model`), and 3.1.0 adds a
`GenkitSpanProcessor` that maps them onto what the platform reads: a flow
becomes kind `workflow`, a tool kind `tool`, and a model span gains
`gen_ai.request.model` / `gen_ai.response.model`, `gen_ai.provider.name`
(from the `plugin/model` name), `gen_ai.usage.*` tokens and the input/output
messages. No enum member, no flag — always on. Span names stay Genkit's
(`supportFlow`, `lookupOrder`), not the `workflow <name>` form traceloop uses.

**Call `instrument()` before the first flow runs.** Genkit starts its own
`NodeSDK` lazily, on the first span, and that SDK is OpenTelemetry 1.x — so a
flow that runs first puts you in the 1.x attach case above and Progress
receives nothing. Init first and Genkit's later registration is refused
(silently — Genkit swallows it), its spans go through the global tracer into
our provider, and everything arrives. Measured on genkit 1.41 with
`@genkit-ai/compat-oai`, one flow calling one tool and one model:
`supportFlow` (workflow), `lookupOrder` (tool), `generate`, and the model span
with provider, model and token counts. In this order the Genkit dev UI
(`genkit start`) receives no traces — say so if the user relies on it.

**Watch for the double count.** `@genkit-ai/compat-oai` drives the `openai`
package underneath and `@genkit-ai/vertexai` drives `@google-cloud/vertexai` —
both instrumented providers — so each model call is recorded twice: once as
Genkit's model span, once as the provider's `chat <model>`. Block the provider
and keep Genkit's:

```typescript
blockInstruments: new Set([ObservabilityInstruments.OPENAI]),   // VERTEXAI for the vertexai plugin
```

Measured: with the block, four spans and one model record.
`@genkit-ai/googleai` uses `@google/generative-ai`, which nothing instruments,
so it has no double and needs no block — Genkit's model span is the only
record. The `generate` wrapper span carries a provider name but no model —
the shape the collector currently misfiles as an `llm_call` against model
`unknown` (collector fix pending merge, Sep 2026) — so expect one phantom
LLM call per `generate` until that lands.

## Structure without a framework — spans with no LLM call

`wrapFunctionWithSpan` gives explicit trace boundaries (also the fix for flat
traces from older LangChain 0.3.x):

```typescript
import { ObservabilitySpanKind, wrapFunctionWithSpan } from '@progress/observability';

const tracedRun = wrapFunctionWithSpan(runAgent, 'MyAgent',
  { spanKind: ObservabilitySpanKind.AGENT }) as typeof runAgent;
await tracedRun(input);
```

Mechanics, verified against the package:

- Returns a bare `Function` — cast back (`as typeof fn`) to keep the signature.
- Sync and async both work; it awaits only a thenable.
- **`spanKind` defaults to `TASK`** — always pass it; the default is silently
  wrong for an entry point.
- Calls `fn.apply(null, …)`, dropping `this` — for class methods use the
  `@workflow` / `@task` / `@agent` / `@tool` decorators instead.

`ObservabilitySpanKind` has the same four members as the Python decorators,
with the same meanings:

| Kind | For |
|---|---|
| `WORKFLOW` | The entry point. One per user request, wrapping everything below. |
| `TASK` | An internal step that transforms data in-process — classify, summarize, route, parse. |
| `AGENT` | A unit that *decides* what to do next, usually an LLM loop. |
| `TOOL` | Anything the code **calls out to**: lookup, fetch, DB query, API or file read, retrieval. |

The `TASK`/`TOOL` split is the one that gets missed: transforms what it was
given → `TASK`; goes and gets something or causes an external effect → `TOOL`.
`fetch…`/`lookup…`/`get…`/`search…`/`send…` is almost always a `TOOL`, even
when the app calls it directly.

**Coverage — no-LLM app: wrap the whole pipeline.** Explicit spans are the
*only* span source there: the entry point (`WORKFLOW`), each step (`TASK`),
every tool-like callable (`TOOL`). A bare function emits nothing; wrapping only
the entry point yields a clean single-span trace that misreports a multi-step
pipeline, with nothing in the output to say so. When auto-instrumentation is
doing the work, the opposite holds — add no wrappers at all.

**Wrap in place, once, at module scope:**

- `const tracedX = wrapFunctionWithSpan(x, …)` beside the definition, then
  call `tracedX`. Never call `wrapFunctionWithSpan` inside the caller — that
  builds a new wrapper on every invocation.
- Re-point the calls *inside the original functions* to the traced consts.
  Never copy a body into a new function to re-point them — the untraced
  original stays behind as dead code and drifts.
- Never convert a module-scope script into an exported function to have
  something to wrap. `await import('./src/app.js')` already runs it; the
  wrappers around its internal functions produce the structure.

Scoped tags: `propagateAttributes(['tenant:acme'], () => { … })`.

## Flush on exit

In a bootstrap file, the flush is the `finally` — no handler machinery:

```typescript
try {
  await import('./src/app.js');
} finally {
  await Observability.shutdown();      // flush, even if the app threw
}
```

Short-lived scripts lose their last spans without it, which looks exactly like
"instrumentation doesn't work". **Never invent a third pattern**, and in
particular:

> `process.on('exit', async () => { await Observability.shutdown(); })` **does
> not flush.** Node's `exit` event is synchronous-only: the handler is entered,
> the first `await` yields, and the process is already gone. Nothing after that
> line ever runs and the exit code is still 0 — so it looks like it worked.

**`beforeExit` is a different event and is fine** — it fires while the loop can
still do work, so it can await. That is exactly what the CommonJS bootstrap
above uses, because `require()` returns before a floating `main()` finishes.
`exit` is the broken one; don't generalize the warning to both.

`SIGINT`/`SIGTERM` handlers *may* be added on top for a long-running server —
those callbacks can await before calling `process.exit()` themselves. They are
not a substitute for the `finally`.

## What arrives on the platform (verified)

Span names are prefixed with their kind — `workflow <name>`, `chat <model>`.
When verifying, match on the kind (`llm_call`, `workflow`) or a kind-safe
substring, never on a model name.
LangChain under this SDK reports **full chain hierarchy** in a single trace
(`workflow RunnableSequence` → prompt/parser workflows + `chat <model>` with
provider, tokens, and cost) — report that depth; flat LLM-only spans instead
mean the callback wiring didn't attach (rule 2 above).

## Common failure modes

1. **ESM app, no hooks import**, or **LangChain imported statically before
   init** — rules 1 and 2; the two that produce "it ran but there are no
   spans".
2. **Direct provider SDK, no spans at all** — on 2.1.2 this meant `instruments`
   was omitted; on 3.x it means detection missed the package. Run once with
   `debug: true`: `Detected consumer provider: openai` should appear.
   `No recognized AI provider packages detected` means the package does not
   resolve from the SDK's location — fix the install or layout, don't add
   `instruments`.
3. **Wrong key type** — `acm_…` (MCP, read) in place of `ac_p_…` (Integration).
4. **Missing or non-awaiting shutdown** — spans dropped on exit; see *Flush on
   exit*.
5. **LangChain app, a lone `chat` span per call** — `@langchain/core` is not in
   the app's `package.json`; see the LangChain rule under *`instruments` is
   optional again*.
6. **App owns a TracerProvider on OpenTelemetry 1.x, Progress empty** — the
   attach-export bug under *The app already sets up OpenTelemetry*; visible
   only under `debug: true`.
