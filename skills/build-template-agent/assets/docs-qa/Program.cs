using System.Diagnostics;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace DocsQa;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var smoke = args.Contains("--smoke", StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args, ContentRootPath = AppContext.BaseDirectory, WebRootPath = "wwwroot",
        });
        builder.Configuration.AddUserSecrets<AgentMarker>(optional: true).AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        if (!Uri.TryCreate(builder.Configuration["AzureOpenAI:Endpoint"], UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https")
            throw new InvalidOperationException("AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        var deployment = builder.Configuration["AzureOpenAI:Deployment"];
        if (string.IsNullOrWhiteSpace(deployment)) throw new InvalidOperationException("AzureOpenAI:Deployment is required.");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var tracingKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(tracingKey);
        var appName = builder.Configuration["Progress:Observability:AppName"] ?? "docs-qa";
        if (smoke && !tracingEnabled)
        {
            Console.Error.WriteLine("Smoke requires Progress:Observability:ApiKey (Integration key). No secret value was printed.");
            return 2;
        }
        if (tracingEnabled)
            ObservabilityTracer.Initialize(new ObservabilityOptions
            {
                AppName = appName, ApiKey = tracingKey!, RecordInputs = false, RecordOutputs = false,
                AdditionalAttributes = new Dictionary<string, object> { ["agent.template.id"] = "docs-qa" },
            });
        else Console.Error.WriteLine("Progress tracing is disabled: Integration key is not configured.");
        try
        {
            var store = new DocumentStore(Path.Combine(AppContext.BaseDirectory, "docs"));
            var azure = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureKey));
            IChatClient chatClient = azure.GetChatClient(deployment).AsIChatClient();
            // SDK 1.2.2 captures prompts and tool arguments even with content recording
            // disabled. This wrapper records only provider-call timing, model and usage.
            if (tracingEnabled) chatClient = new MetadataOnlyChatClient(chatClient, deployment, appName);
            var runtime = new AgentRuntime(chatClient, store, appName);
            if (smoke) return await new SmokeRunner(runtime, builder.Configuration).RunAsync();
            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = store.Sections.Count > 0 ? "ready" : "degraded", documentsLoaded = store.DocumentCount,
                sectionsLoaded = store.Sections.Count, tracingEnabled, telemetryContentCaptureEnabled = false,
            }));
            app.MapGet("/api/documents", () => Results.Ok(store.Sections));
            app.MapPost("/api/ask", async (AskRequest? request, CancellationToken token) =>
            {
                AskRequest validated;
                try { validated = (request ?? new AskRequest(null)).Validate(store); }
                catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
                try { return Results.Ok(await runtime.RunAsync(validated, "ask", token)); }
                catch (AgentRunException ex)
                {
                    return Results.Json(new { error = ex.Code, traceId = ex.TraceId }, statusCode: ex.Code == "agent_timeout" ? 504 : 502);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return Results.Json(new { error = "request_cancelled" }, statusCode: 499);
                }
            });
            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        finally { if (tracingEnabled) ObservabilityTracer.Shutdown(); }
    }
}

internal sealed class AgentMarker;
