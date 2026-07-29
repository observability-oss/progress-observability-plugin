# TypeScript / JavaScript — `@progress/observability`

Verified against `@progress/observability` 2.1.2 on npm (wraps
`@traceloop/node-server-sdk`; OpenTelemetry underneath). Prefer the published
package over this file where they disagree.

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

### Then add `instruments` — required for direct provider SDKs

**The 2.1.2 default init instruments LangChain and nothing else.** An app
calling `openai`, `@anthropic-ai/sdk`, `@google/genai`, Bedrock or Cohere
directly emits **zero spans** — no error, clean shutdown. Name what the app
uses:

```typescript
import { Observability, ObservabilityInstruments } from '@progress/observability';

await Observability.instrument({
  appName: process.env.OBSERVABILITY_APP_NAME ?? 'my-app',
  apiKey,
  instruments: new Set([ObservabilityInstruments.OPENAI]),   // every provider in use
});
```

Rules for the set, all measured:

- It is an **allow-list** — anything omitted is off. Include every provider
  *and* framework the app uses.
- App uses LangChain → `LANGCHAIN` must be in the set. `{OPENAI}` alone
  silently drops the Progress hierarchy patch; `{OPENAI, LANGCHAIN}` keeps
  both.
- LangChain-only app → **omit `instruments` entirely**. The default enables
  exactly that path; adding raw provider instrumentation on top double-counts
  each LLM call.
- Re-check this section against any release past 2.1.2 — it is a workaround,
  not intended API; once fixed, `instruments` is optional again.

**Why it happens.** The SDK looks for the app's own `@langchain/core`; finding
one, it takes over LangChain tracing and — unless `OPENAI` or `AZURE_OPENAI`
is named in `instruments` — disables the provider instrumentors to avoid
double-counting, narrowing the run to LangChain alone. The lookup succeeds
even when nothing in `package.json` mentions LangChain, because
`@langchain/core` arrives as a transitive dependency of the SDK and npm hoists
it to top level. Besides naming the provider, `blockInstruments` with
`ObservabilityInstruments.LANGCHAIN` also clears it, by stopping the takeover
before the provider instrumentors are disabled.

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
import { Observability, ObservabilityInstruments } from '@progress/observability';

const apiKey = process.env.OBSERVABILITY_API_KEY;
if (!apiKey) throw new Error('OBSERVABILITY_API_KEY is not set');

Observability.instrument({
  appName: process.env.OBSERVABILITY_APP_NAME ?? 'my-app',
  apiKey,
  instruments: new Set([ObservabilityInstruments.OPENAI]),   // required — see "Then add instruments"
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

## Env vars (same contract as Python)

`OBSERVABILITY_APP_NAME`, `OBSERVABILITY_API_KEY`, `OBSERVABILITY_ENDPOINT`
(has a default — usually omit), `OBSERVABILITY_TRACE_CONTENT=false` (stop
sending prompt/completion text; metadata still flows).

Useful `instrument()` options: `traceContent: false`, `additionalTags: […]`,
`instruments` / `blockInstruments` (a `Set` of `ObservabilityInstruments`),
`debug: true`.

## What auto-instruments

`ObservabilityInstruments` (from the package's types, v2.1.2): OPENAI,
ANTHROPIC, COHERE, BEDROCK, **AZURE_OPENAI**, VERTEXAI, SAGEMAKER, OLLAMA,
GROQ, MISTRAL, TOGETHER, REPLICATE, ALEPHALPHA, GOOGLE_GENERATIVEAI,
TRANSFORMERS, WATSONX, LANGCHAIN, LLAMA_INDEX, CREW, HAYSTACK, OPENAI_AGENTS,
MCP, PINECONE, CHROMA, WEAVIATE, QDRANT, MILVUS, LANCEDB, MARQO, REDIS, MYSQL,
REQUESTS, URLLIB.

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

**Not auto-instrumented: LangGraph.js, Mastra, the Vercel AI SDK.** The JS SDK
ships fewer instrumentors than Python's (where LangGraph rides the LangChain
instrumentor — not here). Say so plainly, then offer `wrapFunctionWithSpan` or
the decorators below for structure.

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
| `TASK` | An internal step that transforms data in-process — classify, summarise, route, parse. |
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
`exit` is the broken one; don't generalise the warning to both.

`SIGINT`/`SIGTERM` handlers *may* be added on top for a long-running server —
those callbacks can await before calling `process.exit()` themselves. They are
not a substitute for the `finally`.

## What arrives on the platform (verified)

JS span names are prefixed with their kind — `workflow <name>`, `chat <model>`
— unlike Python's `<name>` / `openai.chat`. When verifying, match on the kind
(`llm_call`, `workflow`) or a kind-safe substring, never on a model name.
LangChain under this SDK reports **full chain hierarchy** in a single trace
(`workflow RunnableSequence` → prompt/parser workflows + `chat <model>` with
provider, tokens, and cost) — report that depth; flat LLM-only spans instead
mean the callback wiring didn't attach (rule 2 above).

## Common failure modes

1. **ESM app, no hooks import**, or **LangChain imported statically before
   init** — rules 1 and 2; the two that produce "it ran but there are no
   spans".
2. **Direct provider SDK, no spans at all** — `instruments` was omitted; see
   the `instruments` section. The single most likely cause of "OpenAI app,
   clean run, nothing on the platform".
3. **Wrong key type** — `acm_…` (MCP, read) in place of `ac_p_…` (Integration).
4. **Missing or non-awaiting shutdown** — spans dropped on exit; see *Flush on
   exit*.
