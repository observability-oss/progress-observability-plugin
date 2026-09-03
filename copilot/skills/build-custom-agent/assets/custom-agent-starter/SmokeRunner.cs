using System.Text.Json;

namespace CustomAgent;

public sealed class SmokeRunner
{
    private static readonly string[] RequiredCaseIds = ["knowledge", "tool", "not-found"];
    private readonly AgentRuntime _runtime;
    private readonly SmokeCase[] _cases;

    public SmokeRunner(AgentRuntime runtime, IConfiguration configuration)
    {
        _runtime = runtime;
        _cases = configuration.GetSection("Smoke:Cases")
            .GetChildren()
            .Select(ReadCase)
            .ToArray();
        if (_cases.Length != RequiredCaseIds.Length ||
            !_cases.Select(item => item.CaseId).SequenceEqual(RequiredCaseIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Smoke:Cases must contain exactly knowledge, tool, and not-found in that order.");
        }
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<SmokeCaseResult>(_cases.Length);
        foreach (var smokeCase in _cases)
            results.Add(await RunCaseAsync(smokeCase, cancellationToken));

        var status = results.All(result => result.Status == "pass") ? "pass" : "fail";
        var report = new SmokeReport(status, results);
        Console.WriteLine("SMOKE_REPORT=" + JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
        return status == "pass" ? 0 : 1;
    }

    private async Task<SmokeCaseResult> RunCaseAsync(
        SmokeCase smokeCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _runtime.RunAsync(
                smokeCase.Prompt,
                smokeCase.CaseId,
                cancellationToken);
            var passed = smokeCase.ExpectedMarkers.All(marker =>
                response.Answer.Contains(marker, StringComparison.OrdinalIgnoreCase));
            return new SmokeCaseResult(
                smokeCase.CaseId,
                passed ? "pass" : "fail",
                response.TraceId,
                passed ? "expected_content_observed" : "expected_markers_missing");
        }
        catch (AgentRunException ex)
        {
            return new SmokeCaseResult(smokeCase.CaseId, "fail", ex.TraceId, "agent_run_failed");
        }
    }

    private static SmokeCase ReadCase(IConfigurationSection section)
    {
        var caseId = Require(section, "Id", 32);
        var prompt = Require(section, "Prompt", 4_000);
        var expectedMarkers = section.GetSection("ExpectedMarkers")
            .GetChildren()
            .Select(item => item.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        if (expectedMarkers is not { Length: >= 1 and <= 4 } || expectedMarkers.Any(marker => marker.Length > 120))
            throw new InvalidOperationException($"Smoke case '{caseId}' requires one to four markers of at most 120 characters.");
        return new SmokeCase(caseId, prompt, expectedMarkers);
    }

    private static string Require(IConfigurationSection section, string key, int maxLength)
    {
        var value = section[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidOperationException($"Smoke case {key} must contain 1 to {maxLength} characters.");
        return value;
    }

    private sealed record SmokeCase(
        string CaseId,
        string Prompt,
        IReadOnlyList<string> ExpectedMarkers);

    private sealed record SmokeReport(
        string Status,
        IReadOnlyList<SmokeCaseResult> Cases);

    private sealed record SmokeCaseResult(
        string CaseId,
        string Status,
        string TraceId,
        string Reason);
}
