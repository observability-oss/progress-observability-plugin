using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
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
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

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
            var assistantTools = new AssistantTools(knowledgeBase);
            List<AITool> tools =
            [
                AIFunctionFactory.Create(assistantTools.SearchKnowledgeBase),
                AIFunctionFactory.Create(assistantTools.CheckReleaseReadiness),
            ];

            var azureClient = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(azureEndpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(azureEndpoint, new AzureKeyCredential(azureKey));

            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled) chatClient = chatClient.AddObservability();

            const string instructions = """
                You are the Release Evidence Reviewer. You are read-only: you assess documented
                release evidence but never approve or perform a release. For policy questions,
                call SearchKnowledgeBase. For a named project, always call
                CheckReleaseReadiness and preserve its exact machine-readable status token:
                status=Ready, status=Blocked, or status=not_found.
                A release is Ready only when security approval and a rollback owner are both
                documented. Never invent missing evidence. Be concise.
                """;

            var agent = chatClient.AsAIAgent(
                instructions: instructions,
                name: appName,
                tools: tools);
            var runtime = new AgentRuntime(agent);

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
                var message = request?.Message?.Trim();
                if (string.IsNullOrWhiteSpace(message))
                    return Results.BadRequest(new { error = "message_required" });
                if (message.Length > 4_000)
                    return Results.BadRequest(new { error = "message_too_long" });

                try
                {
                    var response = await runtime.RunAsync(message, "chat", cancellationToken);
                    return Results.Ok(new { answer = response.Answer, traceId = response.TraceId });
                }
                catch (AgentRunException ex)
                {
                    return Results.Json(
                        new { error = "agent_run_failed", traceId = ex.TraceId },
                        statusCode: StatusCodes.Status502BadGateway);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Results.Json(
                        new { error = "request_cancelled" },
                        statusCode: 499);
                }
            });

            app.MapFallbackToFile("index.html");
            Console.WriteLine($"Local UI: {PublicListenUrl(builder.Configuration)}");
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

    private static string PublicListenUrl(IConfiguration configuration)
        => configuration["urls"]
           ?? configuration["ASPNETCORE_URLS"]
           ?? "http://127.0.0.1:5078";
}

public sealed record ChatRequest(string? Message);

internal sealed class AgentMarker;
