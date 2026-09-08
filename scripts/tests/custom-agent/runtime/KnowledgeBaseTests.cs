using CustomAgent;
using Microsoft.Extensions.Configuration;

internal static class KnowledgeBaseTests
{
    public static int Run()
    {
        var assertions = 0;
        var root = Directory.CreateTempSubdirectory("agent-knowledge-tests-");
        try
        {
            var docs = Directory.CreateDirectory(Path.Combine(root.FullName, "docs"));
            var data = Directory.CreateDirectory(Path.Combine(root.FullName, "data"));
            const string policy = """
                # Fictional HR Policy

                ## Annual Leave

                Employees receive 24 annual leave days per year.

                ## Leave Carryover

                Employees may carry over at most 5 unused days.

                Carried-over days expire on March 31 of the next year.

                ## Remote Work

                Employees may work remotely up to 2 days per week with manager agreement.

                ## Undocumented Policies

                Parental leave is not documented. Refer questions about parental leave to HR rather than inventing an entitlement.
                """;
            File.WriteAllText(Path.Combine(docs.FullName, "hr-policy.md"), policy);
            var declared = new Dictionary<string, string>
            {
                ["docs/hr-policy.md"] = "supplied",
            };
            var knowledge = Create(root.FullName, declared);

            var annualLeave = knowledge.Search("annual leave 24", 1);
            Assert(annualLeave.Contains("provenance=supplied; source=docs/hr-policy.md; section=Annual Leave\nEmployees receive 24"),
                "Supplied source and its heading must stay attached to the result.");
            var parentalLeave = knowledge.Search("parental leave", 1);
            Assert(parentalLeave.Contains("section=Undocumented Policies") &&
                   parentalLeave.Contains("Refer questions about parental leave to HR"),
                "Retrieval must retain the supplied limitation and referral.");
            Assert(knowledge.Search("March 31", 1).Contains("section=Leave Carryover"),
                "A later paragraph must retain the preceding section heading.");
            var carryover = knowledge.Search("Carryover", 3);
            Assert(CountHits(carryover) == 2 && carryover.Contains("at most 5") && carryover.Contains("March 31"),
                "A heading query must retrieve all relevant paragraphs.");
            Assert(CountHits(knowledge.Search("Carryover", 1)) == 1, "Search must honor topK=1.");
            Assert(CountHits(knowledge.Search("leave", 99)) == 3, "Search must cap topK at three.");
            Assert(CountHits(knowledge.Search("leave", 0)) == 1, "Search must clamp topK to one.");
            Assert(knowledge.Search("xyzzymissing") == "status=not_found; reason=no_matching_local_content",
                "No match must not return unrelated content.");
            Assert(knowledge.Search("?! a to") == "status=not_found; reason=query_has_no_search_terms",
                "An empty-term query must remain an honest no-result.");

            var fullRead = knowledge.Read("docs/hr-policy.md");
            Assert(fullRead.Contains("provenance=supplied; source=docs/hr-policy.md; section=full_document; content_complete=True"),
                "Full reads must report supplied provenance and exact source.");
            Assert(fullRead.EndsWith(policy, StringComparison.Ordinal), "Small reads retain the full original content.");
            Assert(knowledge.Read("hr-policy") == fullRead, "The source-name alias still supports reads.");
            Assert(knowledge.Read("missing.md") == "status=not_found; reason=local_source_missing",
                "A missing source must not substitute another document.");

            File.WriteAllText(Path.Combine(docs.FullName, "long.md"), "# Long Policy\n\n## Continuation Rules\n\n" +
                new string('x', 1_200) + " continuationneedle " + new string('y', 1_300) +
                "\n\n## Later Section\n\nsecondsectionneedle");
            File.WriteAllText(Path.Combine(docs.FullName, "unheaded.txt"), "unheadedneedle needs an honest source label.");
            File.WriteAllText(Path.Combine(data.FullName, "issues.json"), "{\"id\":\"MOCK-1\",\"summary\":\"mockdataneedle\"}");
            declared["docs/long.md"] = "mock";
            declared["docs/unheaded.txt"] = "mock";
            declared["data/issues.json"] = "mock";
            var extended = Create(root.FullName, declared);
            var continuation = extended.Search("continuationneedle", 1);
            Assert(continuation.Contains("section=Continuation Rules") &&
                   continuation.Contains("source=docs/long.md") && !continuation.Contains(new string('x', 1_200)),
                "Long continuations stay bounded and retain their section.");
            var longChunks = extended.Search("Continuation Rules", 3).Split("\n\n");
            Assert(longChunks.Length == 3 && longChunks.All(chunk =>
                    chunk.Contains("section=Continuation Rules") && chunk[(chunk.IndexOf('\n') + 1)..].Length <= 1_200),
                "Every long chunk carries its heading and content limit.");
            Assert(extended.Search("secondsectionneedle", 1).Contains("section=Later Section"),
                "A later heading replaces the previous section.");
            Assert(extended.Search("unheadedneedle", 1).Contains("section=(no heading)"),
                "Plain text does not invent a heading.");
            var mockData = extended.SearchData("mockdataneedle", 1);
            Assert(mockData.Contains("provenance=mock; source=data/issues.json; section=(no heading)"),
                "Structured searches retain mock provenance.");
            Assert(extended.SearchData("parental") == "status=not_found; reason=no_matching_structured_data",
                "Structured searches do not leak document-only hits.");

            declared["data/issues.json"] = "supplied";
            Assert(Create(root.FullName, declared).SearchData("mockdataneedle", 1).Contains("provenance=supplied"),
                "Supplied JSON remains searchable as structured data without being called mock.");

            File.WriteAllText(Path.Combine(docs.FullName, "oversized-read.md"), "# Large Policy\n\n" + new string('z', 9_000));
            declared["docs/oversized-read.md"] = "mock";
            var truncated = Create(root.FullName, declared).Read("docs/oversized-read.md");
            Assert(truncated.Contains("section=full_document; content_complete=False") && truncated.EndsWith("[truncated]"),
                "Large reads disclose truncation.");
            Assert(truncated[(truncated.IndexOf('\n') + 1)..].Length == 8_000 + "\n[truncated]".Length,
                "Full reads remain bounded.");

            var missing = declared.Where(pair => pair.Key != "docs/hr-policy.md").ToDictionary();
            Reject(() => Create(root.FullName, missing), "An undeclared file must fail loading.");
            var extra = new Dictionary<string, string>(declared) { ["docs/not-there.md"] = "mock" };
            Reject(() => Create(root.FullName, extra), "A declared missing file must fail loading.");
            var invalid = new Dictionary<string, string>(declared) { ["docs/hr-policy.md"] = "synthetic" };
            Reject(() => Create(root.FullName, invalid), "Only mock or supplied provenance is valid.");

            var decoy = Directory.CreateTempSubdirectory("agent-knowledge-decoy-");
            var empty = Directory.CreateTempSubdirectory("agent-knowledge-empty-");
            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(decoy.FullName, "docs"));
                File.WriteAllText(Path.Combine(decoy.FullName, "docs", "decoy.md"), "must not load");
                Directory.SetCurrentDirectory(decoy.FullName);
                Reject(() => Create(empty.FullName, new Dictionary<string, string>
                { ["docs/decoy.md"] = "mock" }),
                    "Loading must not fall back to the process working directory.");
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
                decoy.Delete(recursive: true);
                empty.Delete(recursive: true);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
        return assertions;

        void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        void Reject(Action action, string message)
        {
            try { action(); throw new InvalidOperationException(message); }
            catch (InvalidOperationException error) when (error.Message != message) { assertions++; }
        }
    }

    private static KnowledgeBase Create(string root, IReadOnlyDictionary<string, string> declared)
    {
        var values = declared.ToDictionary(
            pair => $"Content:Sources:{pair.Key}",
            pair => (string?)pair.Value,
            StringComparer.Ordinal);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new KnowledgeBase(root, configuration.GetSection("Content:Sources"));
    }

    private static int CountHits(string result)
        => result.Split("status=found;", StringSplitOptions.None).Length - 1;
}
