using System.ComponentModel;
using System.Text.Json;

namespace DocsQa;

// One instance per question. Citation IDs must be discovered by search, then read.
public sealed class DocumentTools(DocumentStore store, string? sourceId = null)
{
    public const int MaxToolCalls = 6;
    public const int MaxAnswerChars = 4_000;
    private readonly string? _sourceId = store.ValidateSourceId(sourceId);
    private readonly HashSet<string> _retrieved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DocumentSection> _citations = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];
    public IReadOnlyList<string> Calls => _calls.ToArray();
    public IReadOnlyList<DocumentSection> Citations => _citations.Values.ToArray();
    public bool HasMatches => _retrieved.Count > 0;

    [Description("Search bundled documents with meaningful keywords from the resolved question. Returns up to four sections, restricted to the selected section when scoped. Never broaden an unrelated topic.")]
    public string SearchDocuments([Description("Specific keywords from the user's question.")] string query)
    {
        RecordCall(nameof(SearchDocuments));
        var matches = store.Search(query, sourceId: _sourceId);
        foreach (var match in matches) _retrieved.Add(match.SourceId);
        return JsonSerializer.Serialize(new { status = matches.Count > 0 ? "found" : "not_found", sections = matches });
    }

    [Description("Read an exact sourceId returned by SearchDocuments. Every section used in the answer must be read. Unknown IDs return not_found; do not invent source IDs.")]
    public string ReadSection([Description("The exact sourceId from SearchDocuments, including #section.")] string sourceId)
    {
        RecordCall(nameof(ReadSection));
        if (_sourceId is not null && sourceId != _sourceId)
            return "{\"status\":\"not_found\",\"reason\":\"outside_source_scope\"}";
        var section = store.Read(sourceId);
        if (section is null || !_retrieved.Contains(sourceId))
            return "{\"status\":\"not_found\",\"reason\":\"source_not_retrieved\"}";
        _citations.TryAdd(section.SourceId, section);
        return JsonSerializer.Serialize(new { status = "found", section });
    }

    public AgentReply Complete(string answer, string traceId)
    {
        if (!_calls.Contains(nameof(SearchDocuments))) throw new InvalidOperationException("retrieval_required");
        if (_citations.Count == 0)
        {
            if (HasMatches) throw new InvalidOperationException("source_read_required");
            return new("not_found", _sourceId is null
                ? "No matching information was found in the bundled documents. Try a question about workspaces, exports, audit history or support."
                : "No matching information was found in the selected section. Clear the source scope to search all bundled documents.", [], Calls, traceId);
        }
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("empty_agent_response");
        if (answer.Length > MaxAnswerChars) throw new InvalidOperationException("answer_too_long");
        return new("answered", answer.Trim(), Citations, Calls, traceId);
    }

    private void RecordCall(string name)
    {
        if (_calls.Count >= MaxToolCalls) throw new InvalidOperationException("tool_call_limit");
        _calls.Add(name);
    }
}

public sealed record AgentReply(string Status, string Answer, IReadOnlyList<DocumentSection> Citations, IReadOnlyList<string> ToolCalls, string TraceId);
