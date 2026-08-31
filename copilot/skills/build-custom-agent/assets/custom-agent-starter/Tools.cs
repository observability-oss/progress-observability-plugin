using System.ComponentModel;

namespace CustomAgent;

/// <summary>
/// Safe starter tools. They read only bundled local content and never call a
/// live business system or perform a side effect.
/// </summary>
public sealed class AssistantTools(KnowledgeBase knowledgeBase)
{
    [Description("Search local Markdown, text, JSON, and CSV prototype content. Results identify their source and whether they are simulated.")]
    public string SearchLocalContent(
        [Description("The question or keywords to find in local prototype content.")] string query)
        => knowledgeBase.Search(query);

    [Description("Read one known local prototype source by its source label, such as docs/sample-knowledge.md.")]
    public string ReadLocalSource(
        [Description("The source label returned by SearchLocalContent.")] string source)
        => knowledgeBase.Read(source);

    [Description("Look up a record only in synthetic local JSON or CSV data. This never contacts or updates a live system.")]
    public string LookupLocalRecord(
        [Description("A record identifier or keywords to find in synthetic local data.")] string query)
        => knowledgeBase.SearchData(query);
}
