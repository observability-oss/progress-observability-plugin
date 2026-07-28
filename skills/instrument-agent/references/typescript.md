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
ESM, loader hooks must register *before the app loads*. Otherwise CommonJS, a
plain synchronous init. Getting this wrong is the #1 "no spans" cause in Node.

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

### Then add `instruments` — required for direct provider SDKs

**The 2.1.2 default init instruments LangChain and nothing else.** (SDK
defect: its consumer-LangChain probe resolves the `@langchain/core` shipped
inside the SDK itself, so it fires in every project and suppresses the other
instrumentors.) An app calling `openai`, `@anthropic-ai/sdk`, Bedrock or
Cohere directly emits **zero spans** — no error, clean shutdown. Name what the
app uses:

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

Run with `tsx bootstrap.ts` (or node with a TS loader). Alternative without a
bootstrap file: hooks import at the very top of the entry point, instrumented
libraries loaded via *dynamic* import after `instrument()` — LangChain in
particular:

```typescript
const { AzureChatOpenAI } = await import('@langchain/openai');
```

## CommonJS — synchronous init at the top of the entry point

```typescript
import { Observability } from '@progress/observability';

const apiKey = process.env.OBSERVABILITY_API_KEY;
if (!apiKey) throw new Error('OBSERVABILITY_API_KEY is not set');

Observability.instrument({
  appName: process.env.OBSERVABILITY_APP_NAME ?? 'my-app',
  apiKey,
});
// app code follows — dependencies are instrumented from here on
```

## Three rules that decide whether anything is captured

Get any of these wrong and the wiring silently does nothing:

1. **The hooks import executes first** — not "near the top", first. Anything
   imported ahead of it is already resolved and cannot be patched.
2. **ESM: LangChain is imported dynamically, after `instrument()`.** A static
   `import { ChatOpenAI } from '@langchain/openai'` resolves before init and
   yields flat or missing spans.
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
