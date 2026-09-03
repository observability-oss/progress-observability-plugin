using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace ReleaseEvidenceReviewer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var smokeMode = args.Contains("--smoke", StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = "wwwroot",
        });

        // Standard .NET precedence: appsettings.json -> user secrets -> environment.
        builder.Configuration
            .AddUserSecrets<AgentMarker>(optional: true)
            .AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var azureEndpoint) ||
            azureEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        }

        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var appName = builder.Configuration["Progress:Observability:AppName"]
                      ?? "release-evidence-reviewer";
        var observabilityKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(observabilityKey);

        if (smokeMode && !tracingEnabled)
        {
            Console.Error.WriteLine(
                "Smoke tests require Progress:Observability:ApiKey (the Integration key). No secret value was read or printed.");
            return 2;
        }

        if (tracingEnabled)
        {
            ObservabilityTracer.Initialize(new ObservabilityOptions
            {
                AppName = appName,
                ApiKey = observabilityKey!,
                RecordInputs = false,
                RecordOutputs = false,
                AdditionalAttributes = new Dictionary<string, object>
                {
                    ["agent.template.id"] = "release-evidence-reviewer",
                },
            });
        }
        else
        {
            Console.Error.WriteLine(
                "Progress Observability tracing is disabled because Progress:Observability:ApiKey is not configured.");
        }

        try
        {
            var knowledgeBase = new KnowledgeBase("docs");
            var azureClient = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(azureEndpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(azureEndpoint, new AzureKeyCredential(azureKey));

            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();
            // SDK 1.2.2 captures prompts and tool arguments even with content recording
            // disabled. This wrapper records only provider-call timing, model and usage.
            if (tracingEnabled) chatClient = new MetadataOnlyChatClient(chatClient, deployment, appName);

            var runtime = new AgentRuntime(chatClient, knowledgeBase, appName);

            if (smokeMode)
                return await new SmokeRunner(runtime, builder.Configuration).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = knowledgeBase.DocumentCount > 0 ? "ready" : "degraded",
                documentsLoaded = knowledgeBase.DocumentCount,
                tracingEnabled,
                telemetryContentCaptureEnabled = false,
            }));

            app.MapPost("/api/chat", async (
                ChatRequest? request,
                CancellationToken cancellationToken) =>
            {
                string message;
                try { message = AgentRuntime.Validate(request?.Message, request?.Context); }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }

                try
                {
                    return Results.Ok(await runtime.RunAsync(message, "chat", cancellationToken, request?.Context));
                }
                catch (AgentRunException ex)
                {
                    return Results.Json(
                        new { error = ex.Code, traceId = ex.TraceId },
                        statusCode: ex.Code == "agent_deadline_exceeded" ? 504 : 502);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Results.Json(
                        new { error = "request_cancelled" },
                        statusCode: 499);
                }
            });

            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        finally
        {
            if (tracingEnabled) ObservabilityTracer.Shutdown();
        }
    }

    private static string Require(IConfiguration configuration, string key)
        => configuration[key]
           ?? throw new InvalidOperationException(
               $"Missing configuration '{key}'. Set it with dotnet user-secrets or an environment variable.");
}

public sealed record ChatRequest(string? Message, ReviewContext? Context = null);

internal sealed class AgentMarker;
