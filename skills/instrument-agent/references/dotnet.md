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
    .AsAIAgent(instructions: instructions, name: appName, tools: tools);
```

For apps using `IChatClient` directly (no agent framework), `.AddObservability()`
goes in the same place — on the chat-client builder chain, before use.

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

## Configuration

Pass the key explicitly to `Initialize()`. The app reads it however its own
config does; two CI-verified patterns:

**Plain env (matches the other languages — simplest for new wiring):**

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
does not recognise that name.

Each value resolves the same way: **the explicit `ObservabilityOptions`
property, then the env var above.** `Endpoint` also falls back to a built-in
default, so only `ApiKey` and `AppName` must come from somewhere. If either is
still empty, `Initialize()` throws
`ArgumentException("Missing required observability options.")`.

## `Initialize()` — order and repeat calls

- **The first successful call wins.** `Initialize()` takes a lock and returns
  immediately if a provider already exists, so a later call with different
  options is a no-op. To reconfigure, call `Shutdown()` first — that disposes
  the provider and clears it, and a subsequent `Initialize()` then applies.
- **A failed call is recoverable.** The `ArgumentException` is thrown *before*
  the provider is assigned, so nothing is cached; fixing the options and
  calling again works.
- **`.AddObservability()` initializes too.** It calls `Initialize()` with
  options built only from its own arguments — so the no-arg form supplies
  neither key nor app name and depends entirely on the env vars above. With
  them set it self-initializes; without them it throws.

For the documented pattern — explicit options, no env vars — that makes the
order load-bearing: `ObservabilityTracer.Initialize(…)` first,
`.AddObservability()` second. Backwards, the chat client's own `Initialize()`
runs first with empty options and throws before your call is ever reached.

Content capture (prompts/completions) is on by default —
`RecordInputs`/`RecordOutputs` on `ObservabilityOptions` are the off-switches
for sensitive systems; metadata keeps flowing either way.

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
  both (CI-verified). Verify by **kind**, not by span name — exact .NET span
  names are SDK-version-dependent and unpinned.
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
