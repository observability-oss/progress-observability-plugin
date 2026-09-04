using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

return ProjectValidator.Run(args);

static class ProjectValidator
{
    private const string Usage = "Usage: dotnet run --file validate-project.cs -- [--target <directory>] [--smoke-baseline <appsettings-snapshot>]";
    private const int MaxContentFiles = 10;
    private const long MaxContentFileBytes = 1_048_576;
    private const long MaxContentTotalBytes = 5_242_880;

    private static readonly HashSet<string> FixedFiles = new(StringComparer.Ordinal)
    {
        ".gitignore",
        "AgentRuntime.cs",
        "ChatHistory.cs",
        "CustomAgent.csproj",
        "KnowledgeBase.cs",
        "MetadataOnlyChatClient.cs",
        "MetadataOnlyTool.cs",
        "Program.cs",
        "README.md",
        "SmokeRunner.cs",
        "wwwroot/index.html",
    };

    private static readonly HashSet<string> RequiredEditableFiles = new(StringComparer.Ordinal)
    {
        "AgentDefinition.cs",
        "Tools.cs",
        "appsettings.json",
    };

    private static readonly (string Label, string Pattern)[] ForbiddenCodePatterns =
    [
        ("network access", @"\bSystem\s*\.\s*Net\b|\b(?:HttpClient|WebRequest|WebClient|Socket|TcpClient)\b"),
        ("process execution", @"\bSystem\s*\.\s*Diagnostics\s*\.\s*Process\b|\bnew\s+Process\s*\(|\bProcess\s*\.\s*Start\b|\bProcessStartInfo\b"),
        ("direct filesystem access", @"\bSystem\s*\.\s*IO\b|\b(?:File|Directory)\s*\.|\b(?:FileInfo|DirectoryInfo|FileStream|StreamWriter|StreamReader)\b"),
        ("environment or secret access", @"\bEnvironment\s*\.|\b(?:ConnectionString|ApiKey|Password|Credential)\b"),
        ("reflection or native code", @"\bDllImport\b|\bSystem\s*\.\s*Reflection\b|\bAssembly\s*\.\s*Load\b|\bType\s*\.\s*GetType\b|\b(?:Activator|Marshal|NativeLibrary)\s*\."),
        ("additional model client", @"\b(?:AzureOpenAIClient|OpenAIClient|IChatClient)\b"),
        ("alias or unsafe syntax", @"\busing\s+(?:static\b|[A-Za-z_][A-Za-z0-9_]*\s*=)|\bextern\s+alias\b|\bglobal\s*::|\bunsafe\b|\bstackalloc\b"),
    ];

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            var (targetArgument, smokeBaseline) = ParseOptions(args);
            var asset = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(CurrentFile())!, "..", "assets", "custom-agent-starter"));
            var target = ValidateTarget(asset, targetArgument);
            var files = CollectFiles(target);
            ValidateFixedFiles(asset, target, files);
            ValidateRequiredEditableFiles(files);
            var content = ValidateAllowedFiles(target, files);
            ValidateEditableCode(target, files);
            ValidateSettings(asset, target, files, smokeBaseline);
            Console.WriteLine(
                $"VALIDATION_OK target={target} content_files={content.Count} content_bytes={content.TotalBytes}");
            return 0;
        }
        catch (Exception error) when (error is ArgumentException
                                      or IOException
                                      or UnauthorizedAccessException
                                      or InvalidDataException
                                      or JsonException)
        {
            Console.Error.WriteLine($"validate-project: {error.Message}");
            return 2;
        }
    }

    private static (string Target, string? SmokeBaseline) ParseOptions(string[] args)
    {
        string? target = null, baseline = null;
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException(Usage);
            switch (args[index])
            {
                case "--target" when target is null: target = args[index + 1]; break;
                case "--smoke-baseline" when baseline is null: baseline = args[index + 1]; break;
                default: throw new ArgumentException(Usage);
            }
        }
        return (target ?? "custom-agent", baseline);
    }

    private static string ValidateTarget(string asset, string targetArgument)
    {
        if (targetArgument.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
            throw new ArgumentException($"Target must not contain '..': {targetArgument}");

        var target = Path.GetFullPath(targetArgument);
        RejectSymlinkPathComponents(target, "Target");
        if (!Directory.Exists(target))
            throw new ArgumentException($"Target must be a real directory: {targetArgument}");

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var assetPrefix = asset.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target.Equals(asset, comparison) || target.StartsWith(assetPrefix, comparison))
            throw new ArgumentException("Target must be outside the bundled starter asset.");
        return target;
    }

    private static Dictionary<string, string> CollectFiles(string target)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(target, "", files);
        return files;

        static void Walk(string directory, string relativeDirectory, Dictionary<string, string> result)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(value => value, StringComparer.Ordinal))
            {
                if (IsLink(entry))
                    throw new InvalidDataException($"Symlinks are not allowed: {Relative(entry)}");

                var name = Path.GetFileName(entry);
                var relative = string.IsNullOrEmpty(relativeDirectory)
                    ? name
                    : $"{relativeDirectory}/{name}";
                if (Directory.Exists(entry))
                {
                    if (relative is "bin" or "obj") continue;
                    if (!IsAllowedDirectory(relative))
                        throw new InvalidDataException($"Unexpected directory: {relative}");
                    Walk(entry, relative, result);
                }
                else
                {
                    result.Add(relative, entry);
                }
            }

            string Relative(string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');
        }
    }

    private static bool IsAllowedDirectory(string relative)
        => relative is "docs" or "data" or "wwwroot" ||
           relative.StartsWith("docs/", StringComparison.Ordinal) ||
           relative.StartsWith("data/", StringComparison.Ordinal);

    private static void ValidateFixedFiles(
        string asset,
        string target,
        IReadOnlyDictionary<string, string> files)
    {
        foreach (var relative in FixedFiles)
        {
            if (!files.TryGetValue(relative, out var targetPath))
                throw new InvalidDataException($"Required fixed file is missing: {relative}");
            var assetPath = Path.Combine(asset, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.ReadAllBytes(assetPath).SequenceEqual(File.ReadAllBytes(targetPath)))
                throw new InvalidDataException($"Fixed file was modified: {relative}");
        }
    }

    private static void ValidateRequiredEditableFiles(IReadOnlyDictionary<string, string> files)
    {
        foreach (var relative in RequiredEditableFiles)
        {
            if (!files.ContainsKey(relative))
                throw new InvalidDataException($"Required customization file is missing: {relative}");
        }
    }

    private static ContentSummary ValidateAllowedFiles(
        string target,
        IReadOnlyDictionary<string, string> files)
    {
        var contentCount = 0;
        long contentBytes = 0;
        foreach (var (relative, fullPath) in files)
        {
            if (FixedFiles.Contains(relative) || RequiredEditableFiles.Contains(relative)) continue;
            if (relative == "INTEGRATION_PLAN.md")
            {
                if (new FileInfo(fullPath).Length > MaxContentFileBytes)
                    throw new InvalidDataException("INTEGRATION_PLAN.md exceeds the 1 MiB limit.");
                continue;
            }

            if (relative == ".env" || relative.StartsWith(".env.", StringComparison.Ordinal))
                throw new InvalidDataException($"Secret environment files are not allowed: {relative}");
            if (!IsSupportedContent(relative))
                throw new InvalidDataException($"Unexpected file: {relative}");
            if (relative.Split('/').Any(segment => segment.StartsWith(".", StringComparison.Ordinal)))
                throw new InvalidDataException($"Hidden content paths are not allowed: {relative}");

            var length = new FileInfo(fullPath).Length;
            if (length > MaxContentFileBytes)
                throw new InvalidDataException($"Content file exceeds the 1 MiB limit: {relative}");
            contentCount++;
            contentBytes += length;
        }

        if (contentCount is < 1 or > MaxContentFiles)
            throw new InvalidDataException($"Local content must contain 1 to {MaxContentFiles} files.");
        if (contentBytes > MaxContentTotalBytes)
            throw new InvalidDataException("Local content exceeds the 5 MiB total limit.");
        return new ContentSummary(contentCount, contentBytes);
    }

    private static bool IsSupportedContent(string relative)
    {
        var extension = Path.GetExtension(relative);
        if (relative.StartsWith("docs/", StringComparison.Ordinal))
            return extension is ".md" or ".txt";
        if (relative.StartsWith("data/", StringComparison.Ordinal))
            return extension is ".json" or ".csv";
        return false;
    }

    private static void ValidateEditableCode(string target, IReadOnlyDictionary<string, string> files)
    {
        foreach (var relative in new[] { "AgentDefinition.cs", "Tools.cs" })
        {
            var code = File.ReadAllText(Path.Combine(target, relative));
            var codeWithoutComments = StripComments(code);
            ValidateLocalSourceReferences(codeWithoutComments, relative, files);
            var executableCode = MaskNonCode(code);
            foreach (var (label, pattern) in ForbiddenCodePatterns)
            {
                if (Regex.IsMatch(
                        executableCode,
                        pattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    throw new InvalidDataException($"Forbidden capability '{label}' in {relative}.");
                }
            }
        }

        var definition = File.ReadAllText(Path.Combine(target, "AgentDefinition.cs"));
        var definitionWithoutComments = StripComments(definition);
        const string signature = @"public\s+IList\s*<\s*AITool\s*>\s+CreateTools\s*\(\s*KnowledgeBase\s+knowledgeBase\s*\)";
        if (Regex.Matches(definitionWithoutComments, signature, RegexOptions.CultureInvariant).Count != 1)
            throw new InvalidDataException("AgentDefinition.cs must contain exactly one bounded CreateTools method.");

        var method = Regex.Match(
            definitionWithoutComments,
            signature + @"\s*\{\s*var\s+tools\s*=\s*new\s+AssistantTools\s*\(\s*knowledgeBase\s*\)\s*;\s*return\s*\[(?<entries>.*?)\]\s*;\s*\}",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!method.Success)
            throw new InvalidDataException("CreateTools must use the bounded direct-registration form from the starter.");

        var entries = method.Groups["entries"].Value;
        const string registration = @"AIFunctionFactory\s*\.\s*Create\s*\(\s*tools\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*\)\s*,?";
        var registrations = Regex.Matches(entries, registration, RegexOptions.CultureInvariant).Count;
        var remainder = Regex.Replace(entries, registration, "", RegexOptions.CultureInvariant);
        if (!string.IsNullOrWhiteSpace(remainder))
            throw new InvalidDataException("CreateTools may contain only direct AssistantTools registrations.");
        if (registrations is < 1 or > 3)
            throw new InvalidDataException("AgentDefinition.cs must register one to three tools.");
    }

    private static void ValidateLocalSourceReferences(
        string text, string relative, IReadOnlyDictionary<string, string> files)
    {
        // Catch invented literal citation paths; dynamic tool behavior still needs review.
        foreach (Match match in Regex.Matches(text,
                     @"\b(?:docs|data)/[^\s""'`<>;:\\]+",
                     RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            // Match the whole token before checking its extension: policy.md-v2.md
            // is one filename, not a reference to policy.md. Strip prose/link suffixes.
            var source = match.Value.Split('#', '?')[0].TrimEnd('.', ',', ')', ']', '}');
            if (Path.GetExtension(source).ToLowerInvariant() is not (".md" or ".txt" or ".json" or ".csv"))
                continue;
            if (!files.ContainsKey(source))
                throw new InvalidDataException($"Unknown local source '{source}' in {relative}. Use the actual bundled source label.");
        }
    }

    private static void ValidateJsonSourceReferences(
        JsonElement value, IReadOnlyDictionary<string, string> files)
    {
        if (value.ValueKind == JsonValueKind.String)
            ValidateLocalSourceReferences(value.GetString()!, "appsettings.json", files);
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) ValidateJsonSourceReferences(property.Value, files);
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateJsonSourceReferences(item, files);
    }

    private static void ValidateSettings(
        string asset, string target, IReadOnlyDictionary<string, string> files, string? smokeBaseline)
    {
        var targetPath = Path.Combine(target, "appsettings.json");
        if (new FileInfo(targetPath).Length > 65_536)
            throw new InvalidDataException("appsettings.json exceeds the 64 KiB limit.");

        using var targetDocument = JsonDocument.Parse(File.ReadAllText(targetPath));
        using var assetDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(asset, "appsettings.json")));
        var root = targetDocument.RootElement;
        ValidateJsonSourceReferences(root, files);
        RequireObjectProperties(root, "root", "Urls", "AzureOpenAI", "Content", "Agent", "Smoke");

        var expectedUrls = RequireString(assetDocument.RootElement, "Urls", 200);
        if (!string.Equals(RequireString(root, "Urls", 200), expectedUrls, StringComparison.Ordinal))
            throw new InvalidDataException("Urls is fixed by the starter.");

        var azure = RequireObject(root, "AzureOpenAI");
        RequireObjectProperties(azure, "AzureOpenAI", "Deployment");
        var expectedDeployment = RequireString(
            RequireObject(assetDocument.RootElement, "AzureOpenAI"), "Deployment", 100);
        if (!string.Equals(RequireString(azure, "Deployment", 100), expectedDeployment, StringComparison.Ordinal))
            throw new InvalidDataException("AzureOpenAI:Deployment is fixed in this MVP.");

        ValidateContentSources(root, files);

        var agent = RequireObject(root, "Agent");
        RequireObjectProperties(
            agent,
            "Agent",
            "DisplayName",
            "ServiceSlug",
            "Purpose",
            "Ui",
            "Instructions",
            "Examples");
        RequireString(agent, "DisplayName", 80);
        var slug = RequireString(agent, "ServiceSlug", 64);
        if (!Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Agent:ServiceSlug must be a lowercase-hyphen slug.");
        RequireString(agent, "Purpose", 500);
        var ui = RequireObject(agent, "Ui");
        RequireObjectProperties(ui, "Agent:Ui", "Preset", "InputPlaceholder");
        var preset = RequireString(ui, "Preset", 20);
        if (preset is not ("knowledge" or "review" or "workflow" or "analysis"))
            throw new InvalidDataException("Agent:Ui:Preset must be knowledge, review, workflow, or analysis.");
        RequireString(ui, "InputPlaceholder", 140);
        RequireString(agent, "Instructions", 4_000);
        ValidateStringArray(RequireArray(agent, "Examples"), "Agent:Examples", 1, 4, 500);

        var smoke = RequireObject(root, "Smoke");
        RequireObjectProperties(smoke, "Smoke", "Cases");
        var cases = RequireArray(smoke, "Cases").EnumerateArray().ToArray();
        var expectedIds = new[] { "knowledge", "tool", "not-found" };
        if (cases.Length != expectedIds.Length)
            throw new InvalidDataException("Smoke:Cases must contain exactly three cases.");
        for (var index = 0; index < cases.Length; index++)
        {
            var smokeCase = cases[index];
            if (smokeCase.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Smoke:Cases:{index} must be an object.");
            RequireObjectProperties(smokeCase, $"Smoke:Cases:{index}", "Id", "Prompt", "ExpectedMarkers");
            if (!string.Equals(RequireString(smokeCase, "Id", 32), expectedIds[index], StringComparison.Ordinal))
                throw new InvalidDataException("Smoke case IDs must be knowledge, tool, and not-found in that order.");
            RequireString(smokeCase, "Prompt", 4_000);
            var markers = ValidateStringArray(
                RequireArray(smokeCase, "ExpectedMarkers"),
                $"Smoke:Cases:{index}:ExpectedMarkers",
                1,
                4,
                120);
            ValidateSmokeMarkers(expectedIds[index], markers);
        }
        if (smokeBaseline is not null) ValidateSmokeBaseline(target, smoke, smokeBaseline);
    }

    private static void ValidateContentSources(
        JsonElement root,
        IReadOnlyDictionary<string, string> files)
    {
        var content = RequireObject(root, "Content");
        RequireObjectProperties(content, "Content", "Sources");
        var configured = RequireObject(content, "Sources");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in configured.EnumerateObject())
        {
            if (!declared.Add(property.Name) || !IsSupportedContent(property.Name) ||
                !files.ContainsKey(property.Name))
            {
                throw new InvalidDataException(
                    $"Content:Sources contains an unknown, duplicate, or unsupported path: {property.Name}");
            }
            if (property.Value.ValueKind != JsonValueKind.String ||
                property.Value.GetString() is not ("mock" or "supplied"))
            {
                throw new InvalidDataException(
                    $"Content source '{property.Name}' must have provenance 'mock' or 'supplied'.");
            }
        }

        var actual = files.Keys.Where(IsSupportedContent).ToHashSet(StringComparer.Ordinal);
        if (declared.Count is < 1 or > MaxContentFiles || !declared.SetEquals(actual))
            throw new InvalidDataException("Content:Sources must declare every local content file exactly once.");
    }

    private static void ValidateSmokeBaseline(string target, JsonElement smoke, string baselineArgument)
    {
        var baseline = Path.GetFullPath(baselineArgument);
        if (IsWithin(target, baseline))
            throw new InvalidDataException("Smoke baseline must be outside the generated project.");
        RejectSymlinkPathComponents(baseline, "Smoke baseline");
        if (!File.Exists(baseline) || new FileInfo(baseline).Length > 65_536)
            throw new InvalidDataException("Smoke baseline must be an existing appsettings snapshot of at most 64 KiB.");
        using var document = JsonDocument.Parse(File.ReadAllText(baseline));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Smoke baseline must be an appsettings JSON object.");
        var expected = RequireObject(document.RootElement, "Smoke");
        if (!JsonElement.DeepEquals(smoke, expected))
            throw new InvalidDataException(
                "Smoke expectations changed after the baseline was captured. Restore the original Smoke section; do not replace the baseline to make tests pass.");
    }

    private static void ValidateSmokeMarkers(string caseId, IReadOnlyList<string> markers)
    {
        if (markers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != markers.Count)
            throw new InvalidDataException($"Smoke case '{caseId}' has duplicate expected markers.");
        if (markers.Any(marker => marker.Length < 4 || !marker.Any(char.IsLetterOrDigit)))
            throw new InvalidDataException($"Smoke case '{caseId}' needs meaningful expected answer fragments (at least four characters).");
        if (markers.Any(marker => Regex.IsMatch(marker, @"^(status|mode|source)=", RegexOptions.IgnoreCase)))
            throw new InvalidDataException($"Smoke case '{caseId}' must check plain-text facts or source paths, not internal diagnostic tokens.");
        if (caseId == "knowledge" && !markers.Any(marker =>
                marker.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
                marker.StartsWith("data/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Smoke case 'knowledge' must include a local docs/ or data/ source path marker.");
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{name} must be an object.");
        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{name} must be an array.");
        return value;
    }

    private static string RequireString(JsonElement parent, string name, int maxLength)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{name} must be a string.");
        var text = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength)
            throw new InvalidDataException($"{name} must contain 1 to {maxLength} characters.");
        return text;
    }

    private static void RequireObjectProperties(JsonElement value, string label, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{label} must be an object.");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || actual.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            !actual.ToHashSet(StringComparer.Ordinal).SetEquals(names))
        {
            throw new InvalidDataException($"{label} contains missing, duplicate, or unsupported settings.");
        }
    }

    private static string[] ValidateStringArray(
        JsonElement array,
        string label,
        int minimum,
        int maximum,
        int maxLength)
    {
        var values = array.EnumerateArray().ToArray();
        if (values.Length < minimum || values.Length > maximum)
            throw new InvalidDataException($"{label} must contain {minimum} to {maximum} values.");
        foreach (var value in values)
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) ||
                value.GetString()!.Trim().Length > maxLength)
            {
                throw new InvalidDataException($"{label} values must contain 1 to {maxLength} characters.");
            }
        }
        return values.Select(value => value.GetString()!.Trim()).ToArray();
    }

    private static string StripComments(string source) => MaskSource(source, maskLiterals: false);

    private static string MaskNonCode(string source) => MaskSource(source, maskLiterals: true);

    // This is a defense-in-depth lexical lint, not a sandbox for arbitrary C#.
    // It keeps line structure stable while excluding prose from API-name checks.
    private static string MaskSource(string source, bool maskLiterals)
    {
        var result = source.ToCharArray();
        var state = LexicalState.Code;
        var rawDelimiterLength = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            switch (state)
            {
                case LexicalState.Code when current == '/' && next == '/':
                    result[index] = result[index + 1] = ' ';
                    index++;
                    state = LexicalState.LineComment;
                    break;
                case LexicalState.Code when current == '/' && next == '*':
                    result[index] = result[index + 1] = ' ';
                    index++;
                    state = LexicalState.BlockComment;
                    break;
                case LexicalState.Code when current == '@' && next == '"':
                    if (maskLiterals) result[index] = result[index + 1] = ' ';
                    index++;
                    state = LexicalState.VerbatimString;
                    break;
                case LexicalState.Code when current == '"' && CountRun(source, index, '"') >= 3:
                    rawDelimiterLength = CountRun(source, index, '"');
                    if (maskLiterals) Mask(result, index, rawDelimiterLength);
                    index += rawDelimiterLength - 1;
                    state = LexicalState.RawString;
                    break;
                case LexicalState.Code when current == '"':
                    if (maskLiterals) result[index] = ' ';
                    state = LexicalState.String;
                    break;
                case LexicalState.Code when current == '\'':
                    if (maskLiterals) result[index] = ' ';
                    state = LexicalState.Character;
                    break;
                case LexicalState.LineComment:
                    if (current is '\r' or '\n') state = LexicalState.Code;
                    else result[index] = ' ';
                    break;
                case LexicalState.BlockComment:
                    result[index] = current is '\r' or '\n' ? current : ' ';
                    if (current == '*' && next == '/')
                    {
                        result[index + 1] = ' ';
                        index++;
                        state = LexicalState.Code;
                    }
                    break;
                case LexicalState.String:
                    if (maskLiterals && current is not ('\r' or '\n')) result[index] = ' ';
                    if (current == '\\' && index + 1 < source.Length)
                    {
                        index++;
                        if (maskLiterals && source[index] is not ('\r' or '\n')) result[index] = ' ';
                    }
                    else if (current == '"') state = LexicalState.Code;
                    break;
                case LexicalState.VerbatimString:
                    if (maskLiterals && current is not ('\r' or '\n')) result[index] = ' ';
                    if (current == '"' && next == '"')
                    {
                        index++;
                        if (maskLiterals) result[index] = ' ';
                    }
                    else if (current == '"') state = LexicalState.Code;
                    break;
                case LexicalState.Character:
                    if (maskLiterals && current is not ('\r' or '\n')) result[index] = ' ';
                    if (current == '\\' && index + 1 < source.Length)
                    {
                        index++;
                        if (maskLiterals && source[index] is not ('\r' or '\n')) result[index] = ' ';
                    }
                    else if (current == '\'') state = LexicalState.Code;
                    break;
                case LexicalState.RawString:
                    var quoteRun = current == '"' ? CountRun(source, index, '"') : 0;
                    if (quoteRun >= rawDelimiterLength)
                    {
                        if (maskLiterals) Mask(result, index, quoteRun);
                        index += quoteRun - 1;
                        state = LexicalState.Code;
                    }
                    else if (maskLiterals && current is not ('\r' or '\n'))
                    {
                        result[index] = ' ';
                    }
                    break;
            }
        }
        return new string(result);

        static int CountRun(string text, int start, char value)
        {
            var count = 0;
            while (start + count < text.Length && text[start + count] == value) count++;
            return count;
        }

        static void Mask(char[] text, int start, int length)
        {
            for (var offset = 0; offset < length; offset++) text[start + offset] = ' ';
        }
    }

    private static void RejectSymlinkPathComponents(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        var inspectionBase = InspectionBase(fullPath);
        var current = inspectionBase;
        foreach (var segment in Path.GetRelativePath(inspectionBase, fullPath).Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
                throw new ArgumentException($"{label} path must not contain symlinks: {path}");
            if (!File.Exists(current) && !Directory.Exists(current)) break;
        }
    }

    private static string InspectionBase(string fullPath)
    {
        var candidates = new List<string>
        {
            Path.GetFullPath(Directory.GetCurrentDirectory()),
            Path.GetFullPath(Path.GetTempPath()),
        };
        if (!OperatingSystem.IsWindows() && Directory.Exists("/tmp"))
            candidates.Add("/tmp");

        return candidates
                   .Distinct(StringComparer.Ordinal)
                   .Where(candidate => IsWithin(candidate, fullPath))
                   .OrderByDescending(candidate => candidate.Length)
                   .FirstOrDefault()
               ?? Path.GetPathRoot(fullPath)
               ?? throw new ArgumentException($"Path has no filesystem root: {fullPath}");
    }

    private static bool IsWithin(string directory, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.Equals(directory, comparison) || path.StartsWith(prefix, comparison);
    }

    private static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (FileNotFoundException)
        {
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string CurrentFile([CallerFilePath] string path = "") => path;

    private enum LexicalState { Code, LineComment, BlockComment, String, VerbatimString, RawString, Character }
    private sealed record ContentSummary(int Count, long TotalBytes);
}
