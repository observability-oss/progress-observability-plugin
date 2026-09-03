namespace CustomAgent;

/// <summary>
/// Small deterministic retriever over bounded local prototype content.
/// Production data-source adapters are intentionally outside this starter.
/// </summary>
public sealed class KnowledgeBase
{
    private const int MaxFiles = 10;
    private const long MaxFileBytes = 1_048_576;
    private const long MaxTotalBytes = 5_242_880;
    private const int MaxChunkCharacters = 1_200;
    private const int MaxReadCharacters = 8_000;

    private readonly Dictionary<string, SourceDocument> _sources =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ContentChunk> _chunks = [];

    public KnowledgeBase(string docsFolder, string dataFolder)
    {
        LoadFolder(docsFolder, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".txt" }, false);
        LoadFolder(dataFolder, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".csv" }, true);
        if (_sources.Count > MaxFiles)
            throw new InvalidOperationException($"Local content may contain at most {MaxFiles} files.");
        if (_sources.Values.Sum(source => source.Size) > MaxTotalBytes)
            throw new InvalidOperationException("Local content exceeds the 5 MiB total limit.");
    }

    public int SourceCount => _sources.Count;

    public string Search(string query, int topK = 3)
        => SearchChunks(query, _chunks, "local_content", topK);

    public string SearchData(string query, int topK = 3)
        => SearchChunks(query, _chunks.Where(chunk => chunk.IsSyntheticData), "simulated_data", topK);

    public string Read(string sourceLabel)
    {
        var normalized = NormalizeLabel(sourceLabel);
        var source = _sources.Values.FirstOrDefault(item =>
            string.Equals(item.Label, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(item.Label), normalized, StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return "status=not_found; reason=local_source_missing";

        var mode = source.IsSyntheticData ? "simulated" : "local_prototype";
        var content = source.Content.Length <= MaxReadCharacters
            ? source.Content
            : source.Content[..MaxReadCharacters] + "\n[truncated]";
        // Keep original headings with the full document so the agent can verify
        // exact citations and surrounding qualifications after a search.
        return $"status=found; mode={mode}; source={source.Label}; section=full_document; " +
               $"content_complete={source.Content.Length <= MaxReadCharacters}\n{content}";
    }

    private void LoadFolder(string folder, HashSet<string> allowedExtensions, bool isSyntheticData)
    {
        var directory = ResolveContentDirectory(folder);
        if (!Directory.Exists(directory)) return;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };
        foreach (var path in Directory.EnumerateFiles(directory, "*", options)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!allowedExtensions.Contains(Path.GetExtension(path))) continue;
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (relative.Split('/').Any(segment => segment.StartsWith(".", StringComparison.Ordinal)))
                continue;

            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
                throw new InvalidOperationException($"Local content file '{relative}' exceeds the 1 MiB limit.");

            var label = $"{folder}/{relative}";
            var content = File.ReadAllText(path);
            var source = new SourceDocument(label, content, info.Length, isSyntheticData);
            _sources.Add(label, source);
            foreach (var chunk in SplitIntoChunks(content))
                _chunks.Add(new ContentChunk(label, chunk.Section, chunk.Content, isSyntheticData));
        }
    }

    private static string ResolveContentDirectory(string folder)
    {
        var outputPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, folder));
        if (Directory.Exists(outputPath)) return outputPath;
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), folder));
    }

    private static IEnumerable<SectionChunk> SplitIntoChunks(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var section = "(no heading)";
        var paragraph = new List<string>();
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim();
            var headingLength = trimmed.TakeWhile(character => character == '#').Count();
            var isHeading = headingLength is >= 1 and <= 6 &&
                            (trimmed.Length == headingLength || char.IsWhiteSpace(trimmed[headingLength]));
            if (isHeading || trimmed.Length == 0)
            {
                foreach (var chunk in SplitParagraph(section, paragraph)) yield return chunk;
                paragraph.Clear();
                if (isHeading) section = trimmed[headingLength..].Trim();
            }
            else paragraph.Add(line);
        }
        foreach (var chunk in SplitParagraph(section, paragraph)) yield return chunk;
    }

    private static IEnumerable<SectionChunk> SplitParagraph(string section, List<string> lines)
    {
        var paragraph = string.Join('\n', lines).Trim();
        for (var offset = 0; offset < paragraph.Length; offset += MaxChunkCharacters)
            yield return new SectionChunk(section, paragraph.Substring(
                offset, Math.Min(MaxChunkCharacters, paragraph.Length - offset)));
    }

    private static string SearchChunks(
        string query,
        IEnumerable<ContentChunk> candidates,
        string emptyReason,
        int topK = 3)
    {
        var terms = QueryTerms(query);
        if (terms.Length == 0)
            return "status=not_found; reason=query_has_no_search_terms";

        var hits = candidates
            .Select(item => new
            {
                Chunk = item,
                Score = terms.Count(term =>
                    item.Content.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (item.Section != "(no heading)" &&
                     item.Section.Contains(term, StringComparison.OrdinalIgnoreCase))),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.Source, StringComparer.Ordinal)
            .Take(Math.Clamp(topK, 1, 3))
            .ToArray();
        if (hits.Length == 0)
            return $"status=not_found; reason=no_matching_{emptyReason}";

        return string.Join("\n\n", hits.Select(hit =>
        {
            var mode = hit.Chunk.IsSyntheticData ? "simulated" : "local_prototype";
            return $"status=found; mode={mode}; source={hit.Chunk.Source}; section={hit.Chunk.Section}\n{hit.Chunk.Content}";
        }));
    }

    private static string[] QueryTerms(string query)
    {
        char[] separators =
        [
            ' ', '\t', '\r', '\n', '.', ',', ':', ';', '?', '!', '(', ')', '[', ']', '{', '}', '/', '-', '_', '"', '\'',
        ];
        return query
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeLabel(string value)
        => value.Trim().Replace('\\', '/').TrimStart('.', '/');

    private sealed record SourceDocument(string Label, string Content, long Size, bool IsSyntheticData);
    private sealed record SectionChunk(string Section, string Content);
    private sealed record ContentChunk(string Source, string Section, string Content, bool IsSyntheticData);
}
