using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketTriage;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TriageRequest(string? TicketId, string? Question = null,
    Dictionary<string, JsonElement>? Scenario = null)
{
    public (string Id, string? Question, TicketStore Store) Validate(TicketStore store)
    {
        var id = TicketId?.Trim();
        if (!TicketStore.ValidId(id)) throw new ArgumentException("valid_ticket_id_required");
        var question = Question?.Trim();
        if (question?.Length > 1_000) throw new ArgumentException("question_too_long");
        return (id!, string.IsNullOrEmpty(question) ? null : question, store.WithScenario(id!, Scenario));
    }
}

public sealed record ScenarioInfo(bool Active, IReadOnlyList<Evidence> SuppliedFields);
