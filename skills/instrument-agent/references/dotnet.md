# .NET — `Progress.Observability.Instrumentation`

Verified against `Progress.Observability.Instrumentation` 1.2.2 on NuGet, as
wired (and proven to build and trace) in
[`observability-oss/dotnet-agent-starter`](https://github.com/observability-oss/dotnet-agent-starter)
— that repo is the canonical example; when in doubt, read its `Program.cs`
rather than reconstructing.

```bash
dotnet add package Progress.Observability.Instrumentation
```

(Installs the latest by default.) Current published versions:
`https://api.nuget.org/v3-flatcontainer/progress.observability.instrumentation/index.json`.

**Public NuGet blocked?** Use the org's internal feed (Azure Artifacts /
Artifactory / Nexus) if one exists — add it to the repo's `nuget.config` rather
than to the machine, so the build is reproducible. Failing that, the standard
offline flow: `nuget install` / `dotnet restore --packages ./packages` on a
connected machine, ship the folder, then add it as a local source
(`dotnet nuget add source ./packages -n offline`) and restore with
`--source offline`.

## Wiring — two touch points

**1. Initialize the tracer before the agent/client pipeline is built, and shut
it down in `finally` to flush:**

```csharp
using Progress.Observability.Extensions.AI;

var observabilityKey = config["Progress:Observability:ApiKey"];   // Integration key, ac_p_…
var tracing = !string.IsNullOrWhiteSpace(observabilityKey);

if (tracing)
{
    ObservabilityTracer.Initialize(new ObservabilityOptions
    {
        AppName = appName,            // the platform identity — short stable slug
        ApiKey = observabilityKey!,
    });
}
// … app runs …
finally
{
    if (tracing) ObservabilityTracer.Shutdown();   // flushes pending spans
}
```

Make tracing optional like this (warn loudly when the key is absent) so the app
still runs before observability is configured — but never silently.

**Optional tracing has to guard both touch points, not just this one.** A bare
`.AddObservability()` builds its own options and initializes from them, falling
back to `PROGRESS__OBSERVABILITY__APIKEY`; with no key anywhere it throws. So an
app that guards `Initialize()` and leaves the client chain unguarded still dies
on a missing key — later, and somewhere less obvious. Gate the attach too:

```csharp
IChatClient client = azureClient.GetChatClient(deployment).AsIChatClient();
if (tracing) client = client.AddObservability();
```

In a hosted app the same gate goes inside the `AddChatClient` factory
(`return tracing ? client.AddObservability() : client;`), where an unguarded
call would throw on first resolve rather than at startup — and register the
`Shutdown` hook only when `tracing` as well.

**Do not add a `Console.CancelKeyPress` handler.** The `finally` above is the
whole pattern; a Ctrl+C handler is not needed for it and is very easy to get
wrong. Two shapes seen in practice, both worse than no handler at all:

- `e.Cancel = true` with nothing that exits — this *suppresses* termination,
  making Ctrl+C a no-op: the app can no longer be interrupted and simply runs
  on (indefinitely, if the work it is waiting on is stuck). `e.Cancel = true`
  does not make a `finally` run; it declines to die.
- `Environment.Exit(…)` inside the handler — `Exit` skips every `finally` on
  the stack, so any flush left to the `finally` is lost, while the handler
  reads as if it enabled it.

Doing it properly means a `CancellationTokenSource` the work actually observes,
which is app restructuring. Out of scope: wire the `finally`, stop there.

**2. Attach to the `IChatClient` chain** — this is the proven capture point for
LLM calls and every `AIFunction` (tool) invocation:

```csharp
var agent = azureClient
    .GetChatClient(deployment)
    .AsIChatClient()
    .AddObservability()               // ← the instrumentation
    .AsAIAgent(instructions: instructions, name: agentName, tools: tools);
```

For apps using `IChatClient` directly (no agent framework), `.AddObservability()`
goes in the same place — on the chat-client builder chain, before use.

`name:` here is the agent's own, whatever the app already called it — not
`appName`. The two are separate identities: `AppName` on `ObservabilityOptions`
is what the platform files spans under, while `name:` is the agent's, and
pointing it at the telemetry variable makes the app's behavior change with an
observability setting. Leave it as it is.

### Agents built from `AIProjectClient` (Azure AI Foundry)

An agent built straight from the project client (`Microsoft.Agents.AI.Foundry`
package) has no chat-client chain to attach to:

```csharp
AIAgent agent = new AIProjectClient(endpoint, credential)
    .AsAIAgent(model: "gpt-4o", instructions: instructions)
    .AsBuilder()
    .Build();
```

`Initialize()` alone exports nothing here — it only listens; nothing in this
pipeline emits. Add the emitter on the agent builder:

```csharp
    .AsBuilder()
    .UseOpenTelemetry(sourceName: ObservabilityTracer.SourceName)
    .Build();
```

**`sourceName:` is required.** Without it the wrapper emits on
`Experimental.Microsoft.Agents.AI`, which Progress does not listen to, and
still nothing exports. Wired this way each run produces one `invoke_agent`
span — agent name, instructions, response id, and
`gen_ai.usage.input_tokens` / `gen_ai.usage.output_tokens`. The usage
attributes are documented by Agent Framework, not measured here. There is no
separate `chat` span at this level.

What the two `AsAIAgent` overloads mean for capture:

- **`AsAIAgent(model:, instructions:)`** builds a local `ChatClientAgent`
  over the project's Responses endpoint — LLM calls do transit the process.
  Per-call `chat` and tool spans are reachable, but only by restructuring to
  an explicit chat client attached at the proven point:

  ```csharp
  var chatClient = projectClient.GetProjectOpenAIClient()
      .GetProjectResponsesClient()
      .AsIChatClient(deployment)
      .AddObservability();
  ```

  Default to the agent-level wrapper — it keeps the app's shape. Restructure
  only when the task asks for per-call LLM spans.
- **`AsAIAgent(agentRecord)`** (a versioned `FoundryAgent`, defined in the
  Foundry portal) executes server-side. The agent-level wrapper is the
  ceiling: LLM calls never transit the process, and no client-side wiring
  can add `chat` spans.

Don't enable both the agent-level wrapper and `.AddObservability()` on the
same pipeline with inputs/outputs recorded — both layers record the prompt
and response, duplicating content.

### Hosted apps (DI, `AddChatClient`, ASP.NET or a worker)

All three touch points move. Don't reach for the console shape:

```csharp
ObservabilityTracer.Initialize(new ObservabilityOptions { AppName = appName, ApiKey = key });

var builder = Host.CreateApplicationBuilder(args);      // or WebApplication.CreateBuilder

builder.Services
    .AddChatClient(sp => sp.GetRequiredService<OpenAIClient>()
        .GetChatClient(model).AsIChatClient()
        .AddObservability())      // ← inside the factory, on the IChatClient
    .UseFunctionInvocation();

var host = builder.Build();
host.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(ObservabilityTracer.Shutdown);
```

- **`Initialize()` before `Build()`** — it must be live before DI resolves any
  registered client.
- **`.AddObservability()` goes inside the `AddChatClient` factory, not on the
  builder.** It is an extension on `IChatClient`, *not* on `ChatClientBuilder`,
  so chaining it after `.UseFunctionInvocation()` is a compile error:
  `CS1929: 'ChatClientBuilder' does not contain a definition for
  'AddObservability'`. The factory is also the right place on the merits: it
  wraps the raw client and every builder middleware wraps that, which is
  **verified end-to-end** to yield `llm_call` *and* `tool` spans — observability
  sitting inside `UseFunctionInvocation` still captures the tool calls that
  middleware makes. Don't try to hoist it outward.
- **`Shutdown()` on `ApplicationStopping`** — a hosted app has no console
  `try`/`finally` to flush from.

**Attach it to every chain, not just the first.** An app that builds more than
one `IChatClient` — a cheap model for classification and a stronger one for
generation, say — needs `.AddObservability()` on each. Miss one and that model's
calls are simply absent from the traces, with everything else looking healthy,
which reads as a platform problem rather than a wiring gap.

**Tools outside function-invocation middleware need `AddToolObservability()`.**
Tool spans come free when `UseFunctionInvocation` (or an agent framework that
uses it) drives the calls. An app that dispatches tools itself gets none — the
package exposes `AddToolObservability()` for that case, as an extension on
`ChatOptions` and on `IList<AITool>`, which wraps each `AIFunction` so its
invocations are recorded. It is idempotent: already-instrumented functions are
left alone, so it is safe on a list you do not fully control. Reach for it only
when tool spans are genuinely missing — with function-invocation middleware in
the chain it is redundant.

**Errors are recorded already.** A failing call marks both the chat span and
its parent agent span as errored, attaches the exception, and rethrows. Don't
add `try`/`catch` to report failures to the platform, and don't swallow the
exception — that loses it from the app and records nothing extra.

**Provider and model are detected, not configured.** The wrapper reads them
from the client's own metadata, correcting Azure clients that report
themselves as OpenAI. There is no option to set, and a client whose model
cannot be determined still produces spans. It recognizes `openai`, `azure`,
`anthropic`, `google` and `ollama`; a client outside that set still traces but
may carry no provider name. Don't read that as a wiring problem.

## Configuration

Pass the key explicitly to `Initialize()`. The app reads it however its own
config does; two CI-verified patterns:

**Plain env (simplest for new wiring):**

```csharp
var key = Environment.GetEnvironmentVariable("OBSERVABILITY_API_KEY");
```

**The app's config layering** (what the starter does — user secrets in dev,
env vars in prod) reading `Progress:Observability:ApiKey`:

```bash
# dev
dotnet user-secrets set "Progress:Observability:ApiKey" "ac_p_001_..."
# prod (double underscore = nested key)
export PROGRESS__OBSERVABILITY__APIKEY="ac_p_001_..."
```

**The package also reads three env vars of its own:**

```
PROGRESS__OBSERVABILITY__APIKEY
PROGRESS__OBSERVABILITY__APPNAME
PROGRESS__OBSERVABILITY__ENDPOINT
```

These are the ASP.NET config-hierarchy spellings. `OBSERVABILITY_API_KEY` is
the app-side convention used above for the app to read and pass in; the SDK
does not recognize that name.

Each value resolves the same way: **the explicit `ObservabilityOptions`
property, then the env var above.** `ApiKey` is the only one with nothing
behind it — `Endpoint` and `AppName` both carry defaults — so a missing key is
the one thing that throws
`ArgumentException("Missing required observability options.")`.

**Always set `AppName` explicitly, even though the SDK does not require it.**
It defaults to `Agent`, so omitting it fails nothing: the app initializes,
exports, and lands on the platform under that name. Two services that both
omit it are indistinguishable there, and anything keyed on service name points
at the wrong thing.

## `Initialize()` — order and repeat calls

- **The first successful call wins.** `Initialize()` takes a lock and returns
  immediately if a provider already exists, so a later call with different
  options is a no-op. To reconfigure, call `Shutdown()` first — that disposes
  the provider and clears it, and a subsequent `Initialize()` then applies.
- **A failed call is recoverable.** The `ArgumentException` is thrown *before*
  the provider is assigned, so nothing is cached; fixing the options and
  calling again works.
- **`.AddObservability()` initializes too**, from options built only out of
  what it was handed. The no-arg form therefore has no key, and falls back to
  `PROGRESS__OBSERVABILITY__APIKEY`: with that set it self-initializes and the
  service is named `Agent`; without it, it throws.

For the documented pattern — explicit options, no env vars — that makes the
order load-bearing: `ObservabilityTracer.Initialize(…)` first,
`.AddObservability()` second. Backwards, the chat client's own `Initialize()`
runs first with empty options and throws before your call is ever reached.

Content capture (prompts/completions) is on by default —
`RecordInputs`/`RecordOutputs` on `ObservabilityOptions` are the off-switches
for sensitive systems. They gate `gen_ai.prompt`, `gen_ai.completion` and the
tool-level `gen_ai.tool.input` / `gen_ai.tool.output`. Everything else — model,
provider, token counts, temperature, the available-tools list — is recorded
regardless. Say so when a user asks what leaves the process: turning content
off removes the message bodies, it does not make the span anonymous.

Three more `ObservabilityOptions` settings. `Debug` turns on the SDK's own
logging through the default logger — try it first when init appears to succeed
but nothing arrives. `AdditionalAttributes` and `AdditionalTags` are stamped on
every span; the portal filters on **tags**, so put `customer.id:12345` and
anything else you will search by there rather than in attributes. Both are read
once at `Initialize()`, so changing them later needs `Shutdown()` first.

## The app already exports OpenTelemetry

Init ordering does not matter here. `Initialize()` builds its own
`TracerProvider`; a provider the app built with
`Sdk.CreateTracerProviderBuilder()` keeps working either way, before or
after, and its spans keep flowing (measured). There is no shared global
provider to lose a race over, so this is unlike Python — do not carry that
reference's ordering rule across.

**Do not stack `.AddObservability()` on a chain that already has
`.UseOpenTelemetry()`.** `Microsoft.Extensions.AI` ships its own
OpenTelemetry support, and an app that already exports OTel plausibly uses
it. Both on one client record every call twice — a `chat` span from
`Experimental.Microsoft.Extensions.AI` and a `gen_ai.chat` span from
Progress — so token counts, call counts and cost all double, and nothing
looks wrong. Stacking also detaches the app's `chat` span: it is created
inside the Progress wrapper, where the ambient activity is cleared, so it
lands in its own trace instead of nesting under the app's span (measured).
Grep for `UseOpenTelemetry` before wiring; if it is present, add
`.AddObservability()` and say in your report that the app now has two
instrumentation layers, that its `chat` span no longer nests under its own
activities, and which layer the user should keep. Removing the app's own
telemetry is their call, never yours.

**Progress spans land in their own trace, by design.** They do not nest
under the app's activities: `gen_ai.invoke_agent` starts a new trace even
when an app activity is current. Activities the app starts itself keep
their trace, and `Activity.Current` is restored after the call (measured).
The exception is spans produced by instrumentation inside the Progress
wrapper — the stacking rule above. Do not report the separate trace as
broken wiring, and do not try to re-parent it.

**The app's own backend does not see Progress spans by default.** They are
emitted on the `Progress.Observability.AgentMonitoring` source; a provider
only receives sources it subscribed to. If the user wants them in their own
backend too, that is one line on their existing builder:

```csharp
.AddSource("Progress.Observability.AgentMonitoring")
```

**The package floors OpenTelemetry at 1.15.3.** An app pinned lower fails
`dotnet add package` with
`NU1605: Detected package downgrade: OpenTelemetry`. The fix is raising the
app's `OpenTelemetry` reference to `>= 1.15.3` — say so rather than leaving
the restore error to explain itself.

## Verified against the platform

- `.AddObservability()` captures calls through **any** `IChatClient`
  implementation — including custom/test ones — so a stub client is a valid
  way to prove the pipeline before real model credentials exist (CI-verified).
- In a DI/hosted app, `.AddObservability()` inside the `AddChatClient` factory
  yields `llm_call` **and** `tool` spans even though `UseFunctionInvocation`
  wraps it — verified end-to-end against the platform, not inferred from the
  agent-framework case.
- Spans arrive with kind `llm_call`, and `AIFunction` invocations through the
  agent pipeline (`AsAIAgent`) arrive with kind `tool` — a single
  `.AddObservability()` between `AsIChatClient()` and `AsAIAgent()` captures
  both (CI-verified). **Verify by kind, not by span name.** Spans are named
  after the operation alone — `gen_ai.chat`, `gen_ai.invoke_agent`,
  `gen_ai.execute_tool` — so the name never carries a model. Don't read its
  absence as a wiring fault: model, provider and token counts are attributes.
- The OpenAI client with a custom `Endpoint` traces identically to Azure
  OpenAI (CI-verified with OpenRouter). **Any OpenAI-compatible endpoint** —
  OpenRouter, LiteLLM, vLLM, Together, most gateways — works the same way:

  ```csharp
  new OpenAIClient(new ApiKeyCredential(key),
      new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") });
  ```

  There is no per-provider integration to look up: if it speaks the OpenAI API
  and flows through `IChatClient`, `.AddObservability()` captures it.

## Common failure modes

1. **No `Shutdown()` on exit**, or **`AddObservability()` missing from a second
   client** — both covered in the wiring section above; they are the two that
   produce partial or empty traces from an otherwise healthy app.
2. **Wrong key type** — `acm_…` (MCP, read) instead of `ac_p_…` (Integration).
   A structurally valid but wrong key initializes fine and fails at export, so
   this one is invisible until you look for the spans.
3. **Framework-level alternatives** — only the `IChatClient` path above is
   verified end-to-end; if the user wants instrumentation at a different layer,
   test against the platform before declaring success.
