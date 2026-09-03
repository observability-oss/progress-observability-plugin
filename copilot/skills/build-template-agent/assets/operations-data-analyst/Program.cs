using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace OperationsDataAnalyst;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var smokeMode = args.Contains("--smoke", StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args, ContentRootPath = AppContext.BaseDirectory, WebRootPath = "wwwroot",
        });
        builder.Configuration.AddUserSecrets<AgentMarker>(optional: true).AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var observabilityKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(observabilityKey);
        var appName = builder.Configuration["Progress:Observability:AppName"] ?? "operations-data-analyst";
        if (smokeMode && !tracingEnabled)
        {
            Console.Error.WriteLine("Smoke tests require Progress:Observability:ApiKey (the Integration key). No secret values are printed.");
            return 2;
        }
        if (tracingEnabled)
            ObservabilityTracer.Initialize(new ObservabilityOptions
            {
                AppName = appName, ApiKey = observabilityKey!, RecordInputs = false, RecordOutputs = false,
                AdditionalAttributes = new Dictionary<string, object> { ["agent.template.id"] = "operations-data-analyst" },
            });
        else Console.Error.WriteLine("Progress Observability tracing is disabled: Progress:Observability:ApiKey is not configured.");

        try
        {
            // The CSV shipped with the template, copied beside the binary on every build.
            var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "operations.csv");
            var metrics = MetricsStore.Load(dataPath);
            Console.WriteLine($"Analysing {metrics.RowCount} rows from {metrics.SourcePath} " +
                $"(sha256:{metrics.Fingerprint}, {metrics.Start:yyyy-MM-dd} to {metrics.End:yyyy-MM-dd}, " +
                $"services: {string.Join(", ", metrics.Services)}).");
            var azure = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureKey));
            IChatClient client = azure.GetChatClient(deployment).AsIChatClient();
            // SDK 1.2.2 captures tool arguments even with content recording disabled.
            // This wrapper records only actual provider-call timing, model and usage.
            if (tracingEnabled) client = new MetadataOnlyChatClient(client, deployment, appName);
            var runtime = new AgentRuntime(client, metrics, appName);
            var workflow = new ViewWorkflow(runtime, metrics);
            if (smokeMode) return await new SmokeRunner(workflow).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = "ready", templateId = "operations-data-analyst", rowsLoaded = metrics.RowCount,
                dataSource = metrics.SourcePath, dataFingerprint = metrics.Fingerprint,
                datasetStart = metrics.Start, datasetEnd = metrics.End, services = metrics.Services,
                syntheticData = true, tracingEnabled, telemetryContentCaptureEnabled = false,
            }));
            app.MapGet("/api/data", (string? service, string? start, string? end) =>
            {
                var result = metrics.Dashboard(service, start, end);
                return result.Status == "invalid" ? Results.BadRequest(result) : Results.Ok(result);
            });
            app.MapPost("/api/analyze", async (AnalysisRequest? request, CancellationToken cancellationToken) =>
            {
                if (request is null) return Results.BadRequest(new { error = "question_required" });
                try
                {
                    return Results.Ok(await workflow.AskAsync(request, cancellationToken));
                }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
                catch (AgentRunException error)
                {
                    return Results.Json(new { error = "agent_run_failed", traceId = error.TraceId }, statusCode: 502);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Results.Json(new { error = "request_cancelled" }, statusCode: 499);
                }
            });
            app.MapPost("/api/analyze/stream", async (AnalysisRequest? request, HttpContext context) =>
            {
                context.Response.ContentType = "application/x-ndjson";
                context.Response.Headers.CacheControl = "no-store";
                var cancellationToken = context.RequestAborted;
                var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
                async Task Send(object message)
                {
                    await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(message, json) + "\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
                try
                {
                    if (request is null) throw new ArgumentException("question_required");
                    var reply = await workflow.AskAsync(request, cancellationToken,
                        onView: data => Send(new { type = "view", data }),
                        onText: text => Send(new { type = "text", text }));
                    await Send(new { type = "done", data = reply });
                }
                catch (ArgumentException error) { await Send(new { type = "error", error = error.Message }); }
                catch (AgentRunException error)
                {
                    await Send(new { type = "error", error = "agent_run_failed", traceId = error.TraceId });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            });
            app.MapPost("/api/view", (ViewSpec? view) =>
            {
                try { return Results.Ok(workflow.Rules.Data(view ?? workflow.Rules.Default)); }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            });
            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        finally { if (tracingEnabled) ObservabilityTracer.Shutdown(); }
    }

    private static string Require(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Missing configuration '{key}'. Set it with dotnet user-secrets or an environment variable.")
            : configuration[key]!;
}

internal sealed class AgentMarker;
