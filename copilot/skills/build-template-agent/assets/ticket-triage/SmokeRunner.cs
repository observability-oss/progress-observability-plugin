using System.Text.Json;

namespace TicketTriage;

public sealed class SmokeRunner(AgentRuntime runtime, IConfiguration configuration)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var cases = new[]
        {
            new SmokeCase("clear-routing", RequireId("Smoke:ClearTicketId"), reply =>
                AnswerContains(reply, "operations", "p1") &&
                reply.Recommendation.Status == "proposed" &&
                reply.Recommendation.SuggestedQueue == "Operations" && reply.Recommendation.SuggestedPriority == "P1" &&
                reply.Recommendation.MissingFields.Count == 0 &&
                reply.Recommendation.PolicyRefs.Contains("triage-policy#production-outage") &&
                HasFact(reply, "environment", "production") && HasFact(reply, "impact", "multiple_customers")),
            new SmokeCase("missing-information", RequireId("Smoke:MissingTicketId"), reply =>
                AnswerContains(reply, "environment", "impact", "workaround") &&
                reply.Recommendation.Status == "needs_information" &&
                reply.Recommendation.SuggestedQueue is null && reply.Recommendation.SuggestedPriority is null &&
                reply.Recommendation.MissingFields.Order().SequenceEqual(new[] { "environment", "impact", "workaroundAvailable" }.Order()) &&
                reply.Recommendation.PolicyRefs.Contains("triage-policy#required-fields")),
            new SmokeCase("unknown-not-found", RequireId("Smoke:UnknownTicketId"), reply =>
                AnswerContains(reply, "no matching") &&
                reply.Recommendation.Status == "not_found" &&
                reply.Recommendation.SuggestedQueue is null && reply.Recommendation.SuggestedPriority is null &&
                reply.Recommendation.Evidence.Count == 0 && reply.Recommendation.PolicyRefs.Count == 0),
        };
        var results = new List<SmokeResult>();
        foreach (var smokeCase in cases)
        {
            try
            {
                var reply = await runtime.RunAsync(smokeCase.TicketId, smokeCase.CaseId, cancellationToken);
                var passed = reply.Recommendation.TicketId == smokeCase.TicketId &&
                             reply.ToolsUsed.Contains(nameof(AssistantTools.GetTicket)) &&
                             reply.TraceId.Length == 32 && smokeCase.Passes(reply);
                results.Add(new(smokeCase.CaseId, passed ? "pass" : "fail", reply.TraceId,
                    passed ? "typed_evidence_and_tools_verified" : "expected_triage_behavior_missing"));
            }
            catch (AgentRunException ex)
            { results.Add(new(smokeCase.CaseId, "fail", ex.TraceId, ex.Code)); }
        }
        var status = results.All(result => result.Status == "pass") ? "pass" : "fail";
        Console.WriteLine("SMOKE_REPORT=" + JsonSerializer.Serialize(new { status, cases = results },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return status == "pass" ? 0 : 1;
    }

    private string RequireId(string key)
    {
        var id = configuration[key]?.Trim();
        return TicketStore.ValidId(id) ? id! : throw new InvalidOperationException($"{key} requires a valid mock ticket ID.");
    }
    private static bool HasFact(AgentReply reply, string field, string value)
        => reply.Recommendation.Evidence.Any(item => item.Field == field && item.Value == value &&
            item.Source == $"tickets.json#{reply.Recommendation.TicketId}");
    private static bool AnswerContains(AgentReply reply, params string[] expected) =>
        expected.All(value => reply.Answer.Contains(value, StringComparison.OrdinalIgnoreCase));
    private sealed record SmokeCase(string CaseId, string TicketId, Func<AgentReply, bool> Passes);
    private sealed record SmokeResult(string CaseId, string Status, string TraceId, string Reason);
}
