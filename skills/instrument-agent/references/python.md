# Python — `progress-observability`

Verified against `progress-observability` 1.4.3 on PyPI (wraps
`traceloop-sdk`; OpenTelemetry underneath). Prefer the published package over
this file where they disagree.

```bash
pip install progress-observability httpx  # requires Python >= 3.10
```

`httpx` is not a typo: `traceloop-sdk` imports it at module scope without
declaring it, so in a project with no other source of httpx,
`from progress.observability import Observability` raises
`ModuleNotFoundError: No module named 'httpx'` (verified on a clean install).
Declare it every time — most LLM SDKs pull httpx in transitively, which is the
only reason this isn't universally fatal.

Match the project's manager: `uv add` · `poetry add` · `pipenv install`.
Current published version:
`https://pypi.org/pypi/progress-observability/json` → `.info.version`.

**Public PyPI blocked?** Use the org's internal mirror if one exists; otherwise
the standard pip offline flow: `pip download -r requirements.txt -d wheels/` on
a connected machine, then `pip install --no-index --find-links wheels/ -r
requirements.txt` on the target.

## Init — once, at process start, before any LLM client is constructed

```python
import os

from progress.observability import Observability

Observability.instrument(
    app_name=os.environ.get("OBSERVABILITY_APP_NAME", "my-app"),
    api_key=os.environ["OBSERVABILITY_API_KEY"],   # Integration key ac_p_… — [] so a missing key raises
    # endpoint=...   # has a default — omit
)
```

**Always pass `app_name`.** Omitting it is not an error: it defaults to
`sys.argv[0]`, so the service is named after the path that launched the
process and changes whenever that command does. The same app then reports
under several identities, and nothing about the run looks wrong.

Every option can instead come from the environment (`Observability.instrument()`
with no arguments):

| Env var | Meaning |
|---|---|
| `OBSERVABILITY_APP_NAME` | app name |
| `OBSERVABILITY_API_KEY` | Integration key (`ac_p_…`) |
| `OBSERVABILITY_ENDPOINT` | collector override (has a default — usually omit) |
| `OBSERVABILITY_TRACE_CONTENT` | `false` → stop sending prompt/completion text; metadata still flows |

Useful kwargs: `trace_content=False` (sensitive data),
`additional_tags=["release:2.4.1"]` (≤200 chars each), `instruments=` /
`block_instruments=` (a `set` of `ObservabilityInstruments`), `debug=True`
(verbose logging when nothing arrives).

## What auto-instruments

Anything in `ObservabilityInstruments` (1:1 with Traceloop instruments):

- **LLM providers:** OPENAI, ANTHROPIC, COHERE, BEDROCK, VERTEXAI, SAGEMAKER,
  OLLAMA, GROQ, MISTRAL, TOGETHER, REPLICATE, ALEPHALPHA, GOOGLE_GENERATIVEAI,
  TRANSFORMERS, WATSONX
- **Agent/chain frameworks:** LANGCHAIN, LLAMA_INDEX, CREW, HAYSTACK,
  OPENAI_AGENTS, MCP
- **Vector stores:** PINECONE, CHROMA, WEAVIATE, QDRANT, MILVUS, LANCEDB,
  MARQO, REDIS, PYMYSQL
- **HTTP:** REQUESTS, URLLIB3

`traceloop-sdk` also bundles instrumentors the enum doesn't name — `agno`
(verified capturing), `voyageai`, `writer`, `sqlalchemy` (unverified).

**Any OpenAI-compatible endpoint counts as OPENAI** (CI-verified): OpenRouter,
LiteLLM, vLLM, Together, Fireworks, most gateways — via
`OpenAI(base_url=..., api_key=...)`. The platform records the real provider.
Azure OpenAI likewise traces as OpenAI with `provider: azure`. So "my provider
isn't on the list" is usually wrong — check whether it speaks the OpenAI API.
One exception: **LlamaIndex validates model names** against a static table and
rejects gateway-style names (`ValueError: Unknown model 'openai/gpt-4o-mini'`);
use `OpenAILike` from `llama-index-llms-openai-like` instead of its `OpenAI`.

**LangGraph rides the LANGCHAIN instrumentor** — full graph topology
(`gen_ai.workflow.nodes`/`edges`), no extra package, no decorators.

**Not auto-instrumented:** DSPy, AutoGen, Pydantic AI, Semantic Kernel,
smolagents, Instructor. Say so, then offer decorators (below).

**Google ADK is partial, not absent.** No instrumentor covers ADK itself, but
`google-adk` depends on `google-genai`, so its model calls ride the GenAI
instrumentor: `llm_call` spans, no agent/session structure. Report it that way
and offer decorators for the structure. ADK also ships a *different* upstream
instrumentor, `opentelemetry-instrumentation-google-genai`, under its
`otel-gcp` extra — if that extra is installed, don't enable both, or every
model call is recorded twice.

## The gate: the installed distribution name decides

traceloop enables an instrumentor only if a **distribution of a specific name**
is installed. Check the dependency file, not the imports:

| Instrumentor | Gate accepts | Does **not** satisfy it |
|---|---|---|
| LangChain | `langchain` or `langgraph` | `langchain-core`, `langchain-openai` |
| LlamaIndex | `llama-index` or `llama_index` | `llama-index-core`, `llama-index-llms-*` |
| Haystack | `haystack` | `haystack-ai` — the real name, so this gate never passes |
| Google GenAI | `google-genai` ≥ 1.0 | `google-generativeai`, the legacy SDK — nothing instruments it |

An app on `-core` packages alone instruments cleanly, runs fine, emits LLM
spans, and produces **no structure** — no error, no warning. This is the most
common cause of "instrumentation works but the traces are flat", and adding
the meta package is a **required edit** (see SKILL.md Wire rules: add it
alongside the `-core` package, never in place of it).

**For Google, check the package name, not the enum.** The instrumentor ships as
`opentelemetry-instrumentation-google-generativeai` and the enum member is
`GOOGLE_GENERATIVEAI`, but it wraps `google.genai.models` and gates on the
`google-genai` distribution. An app on the legacy `google-generativeai` package
emits nothing and raises nothing. Vertex AI is a separate instrumentor again:
gated on `google-cloud-aiplatform` ≥ 1.38.1, wrapping
`vertexai.generative_models` and `vertexai.language_models`.

To check whether an instrumentor is actually on, use
`LangchainInstrumentor().is_instrumented_by_opentelemetry` — not `__wrapped__`
on a method, which LlamaIndex sets with its own dispatcher regardless.

## How deep each framework goes

Judge the verify step by **shape** (which kinds appear), not span count —
counts scale with how much the app does.

| Framework | Structure beyond the LLM call | Kinds you should see |
|---|---|---|
| llamaindex | query engine, retriever, splitter, refine steps | workflow, task, llm_call |
| langchain | `RunnableSequence.workflow` + `execute_task <Runnable>` per step | workflow, task, llm_call |
| langgraph | `invoke_agent <graph>`, `<graph>.workflow`, `execute_task <node>` per node | agent, workflow, task, llm_call |
| crewai | `crewai.workflow`, `<agent role>.agent`, `<task>.task` | workflow, agent, task, llm_call |
| haystack (2.x) | `haystack.pipeline.run`, `haystack.component.run` | llm_call |
| mcp | `initialize.mcp`, `tools/list.mcp`, `<tool>.tool` | tool, llm_call |
| openai-agents | `Agent Workflow`, `<agent>.agent`, `openai.response` | agent, llm_call |
| agno | `<agent>.agent` | agent, llm_call |
| google-genai | none — bare provider SDK | llm_call |

- **Tier 1 (full topology):** llamaindex, langchain, langgraph, crewai,
  haystack on 2.x. Tier-1 showing only `llm_call` is **broken** — almost always
  the gate above; diagnose, never report flat traces as the framework's depth.
- **Tier 2 (agent + LLM):** openai-agents, agno. Agent + llm_call is correct
  and complete; offer decorators only if the user wants step detail.
- **Bare provider SDKs** (`google-genai`, `openai`, `anthropic` called
  directly): a single `llm_call` per request is **correct and complete** —
  there is no framework to have structure. Don't apply the Tier-1 rule above
  and report it as broken. Google's span is named from OTel semconv, so it
  reads `generate_content <model>` (measured) rather than anything traceloop
  chose.
- **mcp** is orthogonal: it instruments the MCP protocol itself and composes
  with whichever framework calls it. **Requires `mcp` 1.x** (measured): 2.0
  removed `mcp.shared.session`, which the instrumentor patches, but its gate is
  `mcp >= 1.6.0` with no upper bound — so on 2.x it enables, raises
  `AttributeError`, and traceloop logs the error and carries on. The app starts
  clean and emits no MCP spans. If an app on `mcp` 2.x reports missing MCP
  spans, pin `mcp<2` rather than hunting the init order.

Names are patterns, not literals — they vary with the app's classes, nodes, and
index/pipeline types. (The rows are measured against the live platform and
re-asserted weekly by CI, so treat them as current rather than aspirational.)

Verification quirks: **CrewAI names task spans after the task's `description`
text** (verify on `crewai.workflow` or the `task` kind, never the task name);
some observations arrive with a kind but **no name** (treat kind as the
reliable signal).

## Haystack

Two upstream traceloop bugs mean its instrumentor never runs (the gate checks
`haystack`, the distribution is `haystack-ai`; and its `_instrument()` crashes
on 3.x anyway). Haystack's structure spans come from **Haystack's own OTel
tracing**, which auto-enables on 2.x when a provider exists and was
**deliberately removed in 3.0**. Options, in preference order:

1. **3.x, opt in:** `pip install opentelemetry-haystack` + the
   `OpenTelemetryConnector` component — Haystack's supported path. Not yet
   verified against the platform here: wire it, then check depth in verify.
2. **Pin `haystack-ai<3`:** keeps the auto-enabled tracer (measured:
   pipeline + component + LLM spans).

Either way, never report Haystack as lacking depth.

**Haystack is the one exception to init-before-the-framework-import.** On 2.x
its `tracer.py` runs `auto_enable_tracing()` at module scope; with a provider
already live, a circular import inside that call exports one **errored** span
(`haystack.tracing.auto_enable`) per run — tracing still works, but the error
count inflates forever. Correct order:

```python
import haystack.tracing          # module-scope auto_enable no-ops: no provider yet

from progress.observability import Observability
Observability.instrument(app_name=..., api_key=...)

haystack.tracing.auto_enable_tracing()   # now fully loaded and a provider exists

from haystack import Pipeline    # noqa: E402
```

Verified: default ordering emits one errored + one clean `auto_enable` span;
this ordering emits one, clean. Everywhere else, init-before-import holds.

## Decorators — structure, and spans without an LLM

```python
from progress.observability import workflow, task, agent, tool

@workflow(name="handle_ticket")        # top-level unit — one per user request
@task(name="summarize")                # in-process step inside a workflow
@agent(name="triage_agent")            # a unit that decides what to do next
@tool(name="kb_lookup")                # anything called out to
```

| Use | For |
|---|---|
| `@workflow` | The entry point. One per user request, wrapping everything below. |
| `@task` | An internal step that transforms data in-process — classify, summarise, route, parse. |
| `@agent` | A unit that *decides* what to do next, usually an LLM loop. |
| `@tool` | Anything the code **calls out to**: lookup, fetch, DB query, API or file read, retrieval. |

The `@task`/`@tool` split is the one that gets missed: transforms what it was
given → `@task`; goes and gets something or causes an external effect →
`@tool`. `fetch_…`/`lookup_…`/`get_…`/`search_…`/`send_…` is almost always a
`@tool`, even when the app calls it directly.

**Coverage: in an app with no LLM calls, decorate the whole pipeline.**
Decorators are the *only* span source there — the entry point (`@workflow`),
each step (`@task`), and every tool-like callable (`@tool`). A function left
bare emits nothing, and the resulting trace silently misrepresents the
pipeline as smaller than it is. When auto-instrumentation is doing the work,
the opposite holds: add no decorators at all.

Decorated functions emit spans with **no LLM call** — the keyless way to prove
the pipeline before model credentials exist. Decorators accept `tags=[…]`;
scoped tags via `with propagate_attributes(tags=["tenant:acme"]): …`.

## Flush on exit

The method is `Observability.shutdown()` — **there is no `flush()`**. The
[documented pattern](https://www.telerik.com/ai-observability-platform/documentation/sdk/python#shutdown)
is `try`/`finally`, which also covers the app raising:

```python
try:
    run_agent()
finally:
    Observability.shutdown()
```

`atexit.register(Observability.shutdown)` is also acceptable — it is the
smaller diff (no re-indenting the entry point) and mandatory when there is no
single entry point to wrap (server, library, several `main`s). Prefer
`try`/`finally` when the wrap is cheap; never invent a third pattern.

Spans are sent eagerly by default (`disable_batch=True`), so short scripts
usually deliver everything anyway — wire the handler regardless; a lost final
span looks exactly like "instrumentation doesn't work".

## Common failure modes

1. **Init after the LLM client import/construction** — move init to the top of
   the entry point. Whether a late init actually loses spans depends on the
   instrumentor: ones that patch a class still cover clients built earlier
   (`google-genai`, measured), ones that hold a bound reference do not. Init
   first regardless — but don't state in a comment that a pre-init client is
   untraced unless you have checked that SDK.
2. **Flat traces from a tier-1 framework** — the gate (meta package missing) or
   init ordering. Diagnose; never report LLM-only as the framework's depth.
3. **Wrong key type** — `acm_…` (MCP, read) in place of `ac_p_…` (Integration,
   write). The app needs the Integration key.
4. **Both `openai` and `langchain` installed but only one used** — pass an
   explicit `instruments={ObservabilityInstruments.OPENAI}` if spans don't
   appear; co-presence can confuse auto-detection.
5. **Silent misconfig** — re-run with `debug=True` (or
   `OBSERVABILITY_DEBUG=true`) to see exporter errors on stdout.
6. **`ModelFixProcessor._apply_attribute_fixes failed` on a Gemini span** — an
   SDK issue, not the app's wiring. It fires on every `google-genai` span and
   logs an empty message. The span still exports and is queryable; only that
   processor's attribute normalization is skipped. Say it's known and move on;
   `OBSERVABILITY_DEBUG=1` gets the traceback.
