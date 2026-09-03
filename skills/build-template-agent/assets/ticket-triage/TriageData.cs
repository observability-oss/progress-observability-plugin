using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketTriage;

public sealed record Ticket(string Id, string Title, string Description, string? IssueType,
    string? Environment, string? Impact, bool? WorkaroundAvailable);
public sealed record Evidence(string Field, string Value, string Source);
public sealed record Recommendation(string Status, string TicketId, string? SuggestedQueue,
    string? SuggestedPriority, IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> MissingFields, IReadOnlyList<string> PolicyRefs);
public sealed record TicketLookup(string Status, Ticket? Ticket, string? Source);
public sealed record PolicyDocument(string Source, string Markdown);

public sealed class TicketStore
{
    private static readonly string[] RuleIds =
        ["required-fields", "how-to", "production-outage", "other-outage", "defect"];
    private readonly Dictionary<string, Ticket> _tickets;
    public IReadOnlyList<Ticket> Tickets { get; }
    public string Policy { get; }
    public ScenarioInfo Scenario { get; }
    public Ticket? BundledTicket { get; }

    private TicketStore(Ticket[] tickets, string policy, Ticket? bundledTicket = null,
        IReadOnlyList<Evidence>? supplied = null)
    {
        Tickets = Array.AsReadOnly(tickets);
        _tickets = tickets.ToDictionary(ticket => ticket.Id, StringComparer.OrdinalIgnoreCase);
        Policy = policy;
        BundledTicket = bundledTicket;
        Scenario = new(supplied is { Count: > 0 }, supplied ?? []);
    }

    public static TicketStore Load(string root)
        => Parse(File.ReadAllText(Path.Combine(root, "data", "tickets.json")),
            File.ReadAllText(Path.Combine(root, "docs", "triage-policy.md")));

    public static TicketStore Parse(string json, string policy)
    {
        policy = policy.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(policy) ||
            RuleIds.Any(rule => !policy.Contains($"## {rule}\n", StringComparison.Ordinal)))
            throw new InvalidDataException("triage_policy_missing_rules");

        Ticket[] tickets;
        try
        {
            tickets = JsonSerializer.Deserialize<Ticket[]>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                AllowDuplicateProperties = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new InvalidDataException("tickets_required");
        }
        catch (JsonException ex) { throw new InvalidDataException("tickets_json_invalid", ex); }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticket in tickets)
        {
            if (ticket is null || !ValidId(ticket.Id) || !ids.Add(ticket.Id) ||
                string.IsNullOrWhiteSpace(ticket.Title) || ticket.Title.Length > 160 ||
                string.IsNullOrWhiteSpace(ticket.Description) || ticket.Description.Length > 2_000 ||
                !Allowed(ticket.IssueType, "outage", "defect", "howto") ||
                !Allowed(ticket.Environment, "production", "test") ||
                !Allowed(ticket.Impact, "multiple_customers", "single_customer"))
                throw new InvalidDataException("ticket_fixture_invalid");
        }
        if (tickets.Length > 100) throw new InvalidDataException("ticket_fixture_too_large");
        return new TicketStore(tickets, policy);
    }

    public static bool ValidId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 40 &&
           value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static bool Allowed(string? value, params string[] allowed)
        => value is null || allowed.Contains(value, StringComparer.Ordinal);

    public TicketLookup GetTicket(string id)
        => _tickets.TryGetValue(id.Trim(), out var ticket)
            ? new("found", ticket, $"tickets.json#{ticket.Id}")
            : new("not_found", null, null);

    // A request-local view. The original store and fixture records are never modified.
    public TicketStore WithScenario(string id, IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (values is null || values.Count == 0) return this;
        var original = GetTicket(id).Ticket ?? throw new ArgumentException("scenario_requires_known_ticket");
        if (values.Count > 4) throw new ArgumentException("scenario_fields_invalid");
        var effective = original;
        var supplied = new List<Evidence>();
        foreach (var (field, value) in values)
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            effective = field switch
            {
                "issueType" when text is "outage" or "defect" or "howto" => effective with { IssueType = text },
                "environment" when text is "production" or "test" => effective with { Environment = text },
                "impact" when text is "multiple_customers" or "single_customer" => effective with { Impact = text },
                "workaroundAvailable" when value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    => effective with { WorkaroundAvailable = value.GetBoolean() },
                _ => throw new ArgumentException("scenario_field_or_value_invalid"),
            };
            supplied.Add(new(field, text ?? value.GetBoolean().ToString().ToLowerInvariant(), $"temporary-scenario#{original.Id}"));
        }
        foreach (var field in values.Keys)
        {
            var permitted = field switch
            {
                "issueType" => original.IssueType is null,
                "environment" => original.Environment is null && effective.IssueType is "outage" or "defect",
                "impact" => original.Impact is null && effective.IssueType is "outage" or "defect",
                "workaroundAvailable" => original.WorkaroundAvailable is null && effective.IssueType == "defect",
                _ => false,
            };
            if (!permitted) throw new ArgumentException("scenario_only_missing_fields_allowed");
        }
        return new TicketStore(_tickets.Values.Select(ticket => ticket.Id == original.Id ? effective : ticket).ToArray(),
            Policy, original, supplied.OrderBy(fact => fact.Field, StringComparer.Ordinal).ToArray());
    }

    public Recommendation Suggest(string id)
    {
        var lookup = GetTicket(id);
        if (lookup.Ticket is not { } ticket)
            return new("not_found", id.Trim(), null, null, [], [], []);

        var evidence = new List<Evidence>();
        void Fact(string field, string? value)
        {
            if (value is not null) evidence.Add(new(field, value,
                BundledTicket?.Id == ticket.Id && Scenario.SuppliedFields.Any(fact => fact.Field == field)
                    ? $"temporary-scenario#{ticket.Id}" : lookup.Source!));
        }
        Fact("issueType", ticket.IssueType);
        if (ticket.IssueType != "howto")
        {
            Fact("environment", ticket.Environment);
            Fact("impact", ticket.Impact);
            if (ticket.IssueType == "defect")
                Fact("workaroundAvailable", ticket.WorkaroundAvailable?.ToString().ToLowerInvariant());
        }

        var missing = new List<string>();
        if (ticket.IssueType is null) missing.Add("issueType");
        if (ticket.IssueType is "outage" or "defect")
        {
            if (ticket.Environment is null) missing.Add("environment");
            if (ticket.Impact is null) missing.Add("impact");
            if (ticket.IssueType == "defect" && ticket.WorkaroundAvailable is null)
                missing.Add("workaroundAvailable");
        }
        if (missing.Count > 0)
            return new("needs_information", ticket.Id, null, null, evidence, missing,
                ["triage-policy#required-fields"]);

        var (queue, priority, rule) = ticket.IssueType switch
        {
            "howto" => ("Product Support", "P3", "how-to"),
            "outage" when ticket.Environment == "production" && ticket.Impact == "multiple_customers"
                => ("Operations", "P1", "production-outage"),
            "outage" when ticket.Environment == "production" => ("Operations", "P2", "other-outage"),
            "outage" => ("Engineering", "P3", "other-outage"),
            "defect" when ticket.Environment == "production" && ticket.WorkaroundAvailable == false
                => ("Engineering", "P2", "defect"),
            _ => ("Engineering", "P3", "defect"),
        };
        return new("proposed", ticket.Id, queue, priority, evidence, [], [$"triage-policy#{rule}"]);
    }
}
