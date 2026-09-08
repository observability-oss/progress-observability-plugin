using System.ComponentModel;
using System.Text.RegularExpressions;

namespace ReleaseEvidenceReviewer;

public sealed class AssistantTools(KnowledgeBase knowledgeBase, string? requestMessage = null, string? selectedProject = null)
{
    private readonly List<string> _toolsUsed = [];
    public IReadOnlyList<string> ToolsUsed => _toolsUsed.ToArray();
    public string? ReviewedProject { get; private set; }
    /// <summary>The typed verdict behind the answer, so the UI shows the same
    /// documented facts the model was given rather than re-deriving them.</summary>
    public ReadinessEvidence? Evidence { get; private set; }
    public bool RejectedCall { get; private set; }
    public string? SelectedProject => selectedProject is not null && AllowedProject(selectedProject)
        ? DisplayName(NormalizeProjectName(selectedProject)) : null;

    [Description("Search the local Markdown release policy and project evidence. Use this for questions about required release evidence.")]
    public string SearchKnowledgeBase(
        [Description("The release-policy or evidence question to search for.")] string query)
    {
        Record(nameof(SearchKnowledgeBase));
        if (string.IsNullOrWhiteSpace(query) || query.Length > 4_000)
        { RejectedCall = true; throw new ArgumentException("invalid_search_query"); }
        var sources = requestMessage is null ? null : knowledgeBase.ProjectNames
            .Where(AllowedProject).Select(name => "project-" + name)
            .Append("readiness-policy").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return knowledgeBase.Search(query, sources: sources);
    }

    [Description("Check whether a named project is Ready, Blocked, or not_found using only its local Markdown release evidence.")]
    public string CheckReleaseReadiness(
        [Description("The exact project name mentioned by the current user message or selected review context.")] string projectName)
    {
        Record(nameof(CheckReleaseReadiness));
        if (string.IsNullOrWhiteSpace(projectName) || projectName.Length > 100)
        { RejectedCall = true; throw new ArgumentException("invalid_project_name"); }
        if (!AllowedProject(projectName))
        { RejectedCall = true; throw new InvalidOperationException("project_not_requested"); }
        var slug = NormalizeProjectName(projectName);
        ReviewedProject = DisplayName(slug);
        if (slug.Length == 0 ||
            !knowledgeBase.TryRead($"project-{slug}", out var projectEvidence))
        {
            Evidence = new("not_found", DisplayName(slug), [], []);
            return $"status=not_found; project={DisplayName(slug)}; reason=no_release_evidence";
        }

        if (!knowledgeBase.TryRead("readiness-policy", out _))
        {
            Evidence = new("not_found", DisplayName(slug), [], []);
            return $"status=not_found; project={DisplayName(slug)}; reason=readiness_policy_missing";
        }

        var securityApproval = ReadField(projectEvidence, "Security approval");
        var rollbackOwner = ReadField(projectEvidence, "Rollback owner");
        var securityApproved = string.Equals(
            securityApproval,
            "Approved",
            StringComparison.OrdinalIgnoreCase);
        var ownerAssigned = !string.IsNullOrWhiteSpace(rollbackOwner) &&
                            !string.Equals(rollbackOwner, "Not assigned", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(rollbackOwner, "None", StringComparison.OrdinalIgnoreCase);

        var missing = new List<string>();
        if (!securityApproved) missing.Add("security_approval");
        if (!ownerAssigned) missing.Add("rollback_owner");
        var status = missing.Count == 0 ? "Ready" : "Blocked";
        RecordEvidence(status, slug, securityApproval, securityApproved, rollbackOwner, ownerAssigned);
        return $"status={status}; project={DisplayName(slug)}; " +
               $"security_approval={securityApproval ?? "Not documented"}; rollback_owner={rollbackOwner ?? "Not documented"}; " +
               (missing.Count == 0 ? "" : $"missing={string.Join(',', missing)}; ") +
               $"sources=[readiness-policy,project-{slug}]";
    }

    private void RecordEvidence(string status, string slug, string? securityApproval, bool securityApproved,
        string? rollbackOwner, bool ownerAssigned)
    {
        var source = $"project-{slug}";
        Evidence = new(status, DisplayName(slug),
            [new("Security approval", securityApproval, securityApproved, source),
             new("Rollback owner", rollbackOwner, ownerAssigned, source)],
            ["readiness-policy", source]);
    }

    private void Record(string tool)
    {
        if (_toolsUsed.Count >= 6)
        { RejectedCall = true; throw new InvalidOperationException("tool_call_limit_exceeded"); }
        _toolsUsed.Add(tool);
    }

    private bool AllowedProject(string name)
    {
        if (requestMessage is null || Mentions(name)) return true;
        var slug = NormalizeProjectName(name);
        if (selectedProject is null || slug != NormalizeProjectName(selectedProject)) return false;
        if (knowledgeBase.ProjectNames.Any(other => NormalizeProjectName(other) != slug && Mentions(other))) return false;
        // Strong unknown-name syntax only; ordinary follow-up words are not project names.
        return !Regex.Matches(requestMessage, @"\b[Pp]roject\s+[""']?([\p{Lu}\p{N}][\p{L}\p{N}-]{0,99})")
            .Select(match => NormalizeProjectName(match.Groups[1].Value))
            .Any(other => other != slug);
    }

    private bool Mentions(string name)
    {
        var slug = NormalizeProjectName(name);
        var words = Regex.Matches(requestMessage!, @"[\p{L}\p{N}]+").Select(match => match.Value.ToLowerInvariant()).ToArray();
        for (var start = 0; start < words.Length; start++)
        {
            var candidate = "";
            for (var end = start; end < words.Length && candidate.Length < slug.Length; end++)
            { candidate += words[end]; if (candidate == slug) return true; }
        }
        return false;
    }

    private static string NormalizeProjectName(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("project ", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[8..];

        return new string(normalized
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string DisplayName(string slug)
        => slug.Length == 0
            ? "unknown"
            : char.ToUpperInvariant(slug[0]) + slug[1..];

    private static string? ReadField(string markdown, string field)
    {
        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('-', '*').Trim();
            if (line.StartsWith(field + ":", StringComparison.OrdinalIgnoreCase))
                return line[(field.Length + 1)..].Trim();
        }

        return null;
    }
}

/// <summary>One release requirement, its documented value, and the file it came from.</summary>
public sealed record EvidenceCheck(string Requirement, string? DocumentedValue, bool Satisfied, string SourceId);
public sealed record ReadinessEvidence(string Status, string Project,
    IReadOnlyList<EvidenceCheck> Checks, IReadOnlyList<string> Sources);
