namespace ReleaseEvidenceReviewer;

/// <summary>
/// Small keyword retriever over the Markdown files in docs/. Replace this with
/// your production store when the example grows beyond a local corpus.
/// </summary>
public sealed class KnowledgeBase
{
    private readonly Dictionary<string, string> _documents;
    private readonly List<(string Source, string Paragraph)> _paragraphs = [];

    public KnowledgeBase(string docsFolder)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, docsFolder);
        var sourcePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), docsFolder));
        var directory = Directory.Exists(outputPath) ? outputPath : sourcePath;

        _documents = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.md")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path),
                    File.ReadAllText,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (source, markdown) in _documents)
        {
            foreach (var paragraph in markdown
                         .Replace("\r\n", "\n", StringComparison.Ordinal)
                         .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _paragraphs.Add((source, paragraph));
            }
        }
    }

    public int DocumentCount => _documents.Count;

    public bool TryRead(string documentId, out string markdown)
        => _documents.TryGetValue(documentId, out markdown!);

    public string Search(string query, int topK = 3)
    {
        if (_paragraphs.Count == 0)
            return "status=not_found; reason=release_evidence_corpus_empty";

        var separators = new[]
        {
            ' ', '\t', '\r', '\n', '.', ',', ':', ';', '?', '!', '(', ')', '[', ']', '/', '-', '_',
        };
        var terms = query
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var hits = _paragraphs
            .Select(item => new
            {
                item.Source,
                item.Paragraph,
                Score = terms.Count(term =>
                    item.Paragraph.Contains(term, StringComparison.OrdinalIgnoreCase)),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .Take(topK)
            .ToArray();

        return hits.Length == 0
            ? "status=not_found; reason=no_matching_release_evidence"
            : string.Join("\n\n", hits.Select(hit => $"[{hit.Source}] {hit.Paragraph}"));
    }
}
