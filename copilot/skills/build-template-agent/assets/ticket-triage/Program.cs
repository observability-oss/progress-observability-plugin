using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace TicketTriage;

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
        builder.Configuration.AddUserSecrets<AgentMarker>(optional: true).AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        ConfigureApi(builder.Services);

        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var appName = builder.Configuration["Progress:Observability:AppName"] ?? "ticket-triage";
        var observabilityKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(observabilityKey);
        var telemetryRecordInputs = builder.Configuration.GetValue("Progress:Observability:RecordInputs", true);
        var telemetryRecordOutputs = builder.Configuration.GetValue("Progress:Observability:RecordOutputs", true);
        if (smokeMode && !tracingEnabled)
        {
            Console.Error.WriteLine("Smoke tests require Progress:Observability:ApiKey (the Integration key). No secret value was printed.");
            return 2;
        }
        if (!tracingEnabled)
            Console.Error.WriteLine("Progress Observability tracing is disabled because its Integration key is not configured.");

        try
        {
            var store = TicketStore.Load(AppContext.BaseDirectory);
            var azureClient = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureKey));
            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled)
                chatClient = chatClient.AddObservability(options =>
                {
                    options.AppName = appName;
                    options.ApiKey = observabilityKey!;
                    options.RecordInputs = telemetryRecordInputs;
                    options.RecordOutputs = telemetryRecordOutputs;
                    options.AdditionalTags.Add("agent.template.id:ticket-triage");
                });
            var runtime = new AgentRuntime(chatClient, store, appName);
            if (smokeMode) return await new SmokeRunner(runtime, builder.Configuration).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            MapEndpoints(app, store, runtime, tracingEnabled, telemetryRecordInputs, telemetryRecordOutputs);
            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        finally { if (tracingEnabled) ObservabilityTracer.Shutdown(); }
    }

    public static void ConfigureApi(IServiceCollection services)
        => services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.AllowDuplicateProperties = false;
            options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
        });

    public static void MapEndpoints(
        WebApplication app,
        TicketStore store,
        AgentRuntime runtime,
        bool tracingEnabled,
        bool telemetryRecordInputs = true,
        bool telemetryRecordOutputs = true)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = store.Tickets.Count > 0 ? "ready" : "degraded",
            ticketsLoaded = store.Tickets.Count,
            documentsLoaded = 1,
            mockData = true,
            tracingEnabled,
            telemetryRecordInputs,
            telemetryRecordOutputs,
        }));
        app.MapGet("/api/tickets", () => Results.Ok(new { tickets = store.Tickets, mockData = true }));
        app.MapPost("/api/triage", async (TriageRequest? request, CancellationToken cancellationToken) =>
        {
            if (request is null) return Results.BadRequest(new { error = "valid_ticket_id_required" });
            try { return Results.Ok(await runtime.RunAsync(request, request.Question is null ? "triage" : "question", cancellationToken)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (AgentRunException ex)
            {
                return Results.Json(new { error = ex.Code, traceId = ex.TraceId },
                    statusCode: ex.Code == "agent_deadline_exceeded" ? 504 : 502);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { return Results.Json(new { error = "request_cancelled" }, statusCode: 499); }
        });
    }

    private static string Require(IConfiguration configuration, string key)
        => string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Missing configuration '{key}'. Use dotnet user-secrets or environment variables.")
            : configuration[key]!;
}

internal sealed class AgentMarker;
