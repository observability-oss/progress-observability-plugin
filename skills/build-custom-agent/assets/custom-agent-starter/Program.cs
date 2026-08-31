using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace CustomAgent;

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

        var definition = AgentDefinition.Load(builder.Configuration);
        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var azureEndpoint) ||
            azureEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        }

        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
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
                AppName = definition.ServiceSlug,
                ApiKey = observabilityKey!,
                RecordInputs = false,
                RecordOutputs = false,
                AdditionalAttributes = new Dictionary<string, object>
                {
                    ["agent.template.id"] = "custom-agent-local-prototype",
                    ["agent.service.slug"] = definition.ServiceSlug,
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
            var knowledgeBase = new KnowledgeBase("docs", "data");
            var azureClient = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(azureEndpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(azureEndpoint, new AzureKeyCredential(azureKey));

            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled) chatClient = chatClient.AddObservability();

            var agent = chatClient.AsAIAgent(
                instructions: definition.Instructions,
                name: definition.ServiceSlug,
                tools: definition.CreateTools(knowledgeBase));
            var runtime = new AgentRuntime(agent, definition.ServiceSlug);

            if (smokeMode)
                return await new SmokeRunner(runtime, builder.Configuration).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/config", () => Results.Ok(new
            {
                displayName = definition.DisplayName,
                purpose = definition.Purpose,
                examples = definition.ExamplePrompts,
                prototype = true,
            }));

            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = knowledgeBase.SourceCount > 0 ? "ready" : "degraded",
                sourcesLoaded = knowledgeBase.SourceCount,
                tracingEnabled,
                telemetryContentCaptureEnabled = false,
                mode = "local_prototype",
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
