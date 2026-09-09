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
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1_024);

        // Standard .NET precedence: appsettings.json -> user secrets -> environment.
        builder.Configuration
            .AddUserSecrets<AgentMarker>(optional: true)
            .AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        var definition = AgentDefinition.Load(builder.Configuration);
        var presentation = AgentPresentation.Load(builder.Configuration);
        var knowledgeBase = new KnowledgeBase(
            AppContext.BaseDirectory,
            builder.Configuration.GetSection("Content:Sources"));
        var tools = definition.CreateTools(knowledgeBase);
        if (tools is null || tools.Count is < 1 or > 3 || tools.Any(tool => tool is not AIFunction))
            throw new InvalidOperationException("Custom agent must register one to three AIFunction tools.");

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

        if (!tracingEnabled)
        {
            Console.Error.WriteLine(
                "Progress Observability tracing is disabled because Progress:Observability:ApiKey is not configured.");
        }

        try
        {
            var azureClient = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(azureEndpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(azureEndpoint, new AzureKeyCredential(azureKey));

            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled)
            {
                chatClient = chatClient.AddObservability(options =>
                {
                    options.AppName = definition.ServiceSlug;
                    options.ApiKey = observabilityKey!;
                    options.RecordInputs = false;
                    options.RecordOutputs = false;
                    options.AdditionalAttributes = new Dictionary<string, object>
                    {
                        ["agent.template.id"] = "custom-agent-local-prototype",
                        ["agent.service.slug"] = definition.ServiceSlug,
                    };
                });
            }
            var boundedClient = new FunctionInvokingChatClient(chatClient)
            {
                MaximumIterationsPerRequest = 3,
                MaximumConsecutiveErrorsPerRequest = 0,
                AllowConcurrentInvocation = false,
                IncludeDetailedErrors = false,
            };
            var agent = boundedClient.AsAIAgent(new ChatClientAgentOptions
            {
                Name = definition.ServiceSlug,
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions
                {
                    Instructions = definition.Instructions + "\n\n" + AgentRuntime.ResponsePolicy,
                    Tools = tools.AddToolObservability(),
                    MaxOutputTokens = 800,
                    AllowMultipleToolCalls = false,
                },
            });
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
                uiPreset = presentation.Preset,
                inputPlaceholder = presentation.InputPlaceholder,
                prototype = true,
                chatHistory = new { maxTurns = ChatHistory.MaxTurns, maxCharacters = ChatHistory.MaxCharacters },
            }));

            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = knowledgeBase.SourceCount > 0 ? "ready" : "degraded",
                sourcesLoaded = knowledgeBase.SourceCount,
                tracingEnabled,
                telemetryRecordInputs = false,
                telemetryRecordOutputs = false,
                mode = "local_prototype",
            }));

            app.MapPost("/api/chat", async (
                ChatRequest? request,
                CancellationToken cancellationToken) =>
            {
                if (!ChatHistory.TryCreateMessages(request, out var messages, out var error))
                    return Results.BadRequest(new { error });

                try
                {
                    var response = await runtime.RunAsync(messages, "chat", cancellationToken);
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

internal sealed class AgentMarker;

internal sealed record AgentPresentation(string Preset, string InputPlaceholder)
{
    private static readonly HashSet<string> AllowedPresets =
        ["knowledge", "review", "workflow", "analysis"];

    public static AgentPresentation Load(IConfiguration configuration)
    {
        var preset = Require(configuration, "Agent:Ui:Preset", 20);
        if (!AllowedPresets.Contains(preset))
        {
            throw new InvalidOperationException(
                "Agent:Ui:Preset must be knowledge, review, workflow, or analysis.");
        }

        return new AgentPresentation(
            preset,
            Require(configuration, "Agent:Ui:InputPlaceholder", 140));
    }

    private static string Require(IConfiguration configuration, string key, int maxLength)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidOperationException($"{key} must contain 1 to {maxLength} characters.");
        return value;
    }
}
