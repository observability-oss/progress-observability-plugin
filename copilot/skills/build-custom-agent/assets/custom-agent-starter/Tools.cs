using System.ComponentModel;

namespace CustomAgent;

/// <summary>
/// Safe starter tools. They read only bundled local content and never call a
/// live business system or perform a side effect.
/// </summary>
public sealed class AssistantTools(KnowledgeBase knowledgeBase)
{
    [Description("Search bundled Markdown, text, JSON, and CSV prototype content. Results identify the source and whether it is mock or supplied.")]
    public string SearchLocalContent(
        [Description("The question or keywords to find in local prototype content.")] string query)
        => knowledgeBase.Search(query);

    [Description("Read one known local prototype source using the exact source label returned by search.")]
    public string ReadLocalSource(
        [Description("The source label returned by SearchLocalContent.")] string source)
        => knowledgeBase.Read(source);

    [Description("Look up a record only in bundled JSON or CSV data. Results identify whether the source is mock or supplied; this never contacts or updates a live system.")]
    public string LookupLocalRecord(
        [Description("A record identifier or keywords to find in bundled structured data.")] string query)
        => knowledgeBase.SearchData(query);
}
