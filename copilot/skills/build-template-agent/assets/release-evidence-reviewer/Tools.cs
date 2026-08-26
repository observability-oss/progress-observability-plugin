using System.ComponentModel;

namespace ReleaseEvidenceReviewer;

public sealed class AssistantTools(KnowledgeBase knowledgeBase)
{
    [Description("Search the local Markdown release policy and project evidence. Use this for questions about required release evidence.")]
    public string SearchKnowledgeBase(
        [Description("The release-policy or evidence question to search for.")] string query)
        => knowledgeBase.Search(query);

    [Description("Check whether a named project is Ready, Blocked, or not_found using only its local Markdown release evidence.")]
    public string CheckReleaseReadiness(
        [Description("The project name, for example Atlas or Orion.")] string projectName)
    {
        var slug = NormalizeProjectName(projectName);
        if (slug.Length == 0 ||
            !knowledgeBase.TryRead($"project-{slug}", out var projectEvidence))
        {
            return $"status=not_found; project={DisplayName(slug)}; reason=no_release_evidence";
        }

        if (!knowledgeBase.TryRead("readiness-policy", out _))
            return $"status=not_found; project={DisplayName(slug)}; reason=readiness_policy_missing";

        var securityApproval = ReadField(projectEvidence, "Security approval");
        var rollbackOwner = ReadField(projectEvidence, "Rollback owner");
        var securityApproved = string.Equals(
            securityApproval,
            "Approved",
            StringComparison.OrdinalIgnoreCase);
        var ownerAssigned = !string.IsNullOrWhiteSpace(rollbackOwner) &&
                            !string.Equals(rollbackOwner, "Not assigned", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(rollbackOwner, "None", StringComparison.OrdinalIgnoreCase);

        if (securityApproved && ownerAssigned)
        {
            return $"status=Ready; project={DisplayName(slug)}; " +
                   $"security_approval=Approved; rollback_owner={rollbackOwner}; " +
                   $"sources=[readiness-policy,project-{slug}]";
        }

        var missing = new List<string>();
        if (!securityApproved) missing.Add("security_approval");
        if (!ownerAssigned) missing.Add("rollback_owner");
        return $"status=Blocked; project={DisplayName(slug)}; " +
               $"missing={string.Join(',', missing)}; " +
               $"sources=[readiness-policy,project-{slug}]";
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
