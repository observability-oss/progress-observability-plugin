using System.Text.Json;

namespace ReleaseEvidenceReviewer;

public sealed class SmokeRunner
{
    private readonly AgentRuntime _runtime;
    private readonly SmokeCase[] _cases;

    public SmokeRunner(AgentRuntime runtime, IConfiguration configuration)
    {
        _runtime = runtime;
        _cases =
        [
            new(
                "policy-markdown",
                RequirePrompt(configuration, "Smoke:KnowledgePrompt"),
                answer => Contains(answer, "Security approval") && Contains(answer, "Rollback owner"),
                "policy_requirements_missing"),
            new(
                "atlas-blocked",
                RequirePrompt(configuration, "Smoke:ToolPrompt"),
                answer => Contains(answer, "status=Blocked"),
                "blocked_status_missing"),
            new(
                "unknown-not-found",
                RequirePrompt(configuration, "Smoke:NotFoundPrompt"),
                answer => Contains(answer, "status=not_found"),
                "not_found_status_missing"),
        ];
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
            var passed = smokeCase.Passes(response.Answer);
            return new SmokeCaseResult(
                smokeCase.CaseId,
                passed ? "pass" : "fail",
                response.TraceId,
                passed ? "expected_behavior_observed" : smokeCase.FailureReason);
        }
        catch (AgentRunException ex)
        {
            return new SmokeCaseResult(
                smokeCase.CaseId,
                "fail",
                ex.TraceId,
                "agent_run_failed");
        }
    }

    private static bool Contains(string value, string expected)
        => value.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static string RequirePrompt(IConfiguration configuration, string key)
    {
        var prompt = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4_000)
            throw new InvalidOperationException($"{key} must contain a prompt of 1 to 4000 characters.");
        return prompt;
    }

    private sealed record SmokeCase(
        string CaseId,
        string Prompt,
        Func<string, bool> Passes,
        string FailureReason);

    private sealed record SmokeReport(
        string Status,
        IReadOnlyList<SmokeCaseResult> Cases);

    private sealed record SmokeCaseResult(
        string CaseId,
        string Status,
        string TraceId,
        string Reason);
}
