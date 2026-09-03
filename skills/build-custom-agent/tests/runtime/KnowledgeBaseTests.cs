using CustomAgent;

internal static class KnowledgeBaseTests
{
    public static int Run()
    {
        var assertions = 0;
        var previousDirectory = Directory.GetCurrentDirectory();
        var testDirectory = Directory.CreateTempSubdirectory("agent-knowledge-tests-");
        try
        {
            Directory.SetCurrentDirectory(testDirectory.FullName);
            // Unique relative folders exercise the working-directory fallback
            // without colliding with starter content copied beside this runner.
            var docsFolder = "fixture-docs-" + Guid.NewGuid().ToString("N");
            var dataFolder = "fixture-data-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(docsFolder);
            Directory.CreateDirectory(dataFolder);
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
            File.WriteAllText($"{docsFolder}/hr-policy.md", policy);
            var knowledge = new KnowledgeBase(docsFolder, dataFolder);

            var annualLeave = knowledge.Search("annual leave 24", 1);
            Assert(annualLeave.Contains("section=Annual Leave\nEmployees receive 24"),
                "Annual-leave text must retain its own heading.");
            var parentalLeave = knowledge.Search("parental leave", 1);
            Assert(parentalLeave.Contains("section=Undocumented Policies"),
                "Unsupported parental-leave guidance must not inherit Annual Leave.");
            Assert(parentalLeave.Contains("Refer questions about parental leave to HR"),
                "Retrieval must retain the supplied HR referral, not just a heading.");
            Assert(parentalLeave.Contains($"source={docsFolder}/hr-policy.md"), "Search must label the actual source.");

            var expiry = knowledge.Search("March 31", 1);
            Assert(expiry.Contains("section=Leave Carryover"),
                "A later paragraph must retain the preceding section heading.");
            var carryover = knowledge.Search("Carryover", 3);
            Assert(CountHits(carryover) == 2 && carryover.Contains("at most 5") && carryover.Contains("March 31"),
                "A heading query must retrieve all relevant paragraphs, not a detached heading.");
            Assert(CountHits(knowledge.Search("Carryover", 1)) == 1, "Search must honor topK=1.");
            Assert(CountHits(knowledge.Search("leave", 99)) == 3, "Search must cap topK at three.");
            Assert(CountHits(knowledge.Search("leave", 0)) == 1, "Search must clamp topK to at least one.");
            Assert(knowledge.Search("xyzzymissing") == "status=not_found; reason=no_matching_local_content",
                "A no-match query must not return unrelated policy.");
            Assert(knowledge.Search("?! a to") == "status=not_found; reason=query_has_no_search_terms",
                "An empty-term query must remain an honest no-result.");

            var fullRead = knowledge.Read($"{docsFolder}/hr-policy.md");
            Assert(fullRead.Contains($"source={docsFolder}/hr-policy.md; section=full_document; content_complete=True"),
                "Full reads must identify their source and scope.");
            Assert(fullRead.EndsWith(policy, StringComparison.Ordinal),
                "Small-document reads must retain all original section headings and text.");
            Assert(knowledge.Read("hr-policy") == fullRead, "The source-name alias must still support full reads.");
            Assert(knowledge.Read("missing.md") == "status=not_found; reason=local_source_missing",
                "A missing source must not substitute a different document.");

            File.WriteAllText($"{docsFolder}/long.md", "# Long Policy\n\n## Continuation Rules\n\n" +
                new string('x', 1_200) + " continuationneedle " + new string('y', 1_300) +
                "\n\n## Later Section\n\nsecondsectionneedle");
            File.WriteAllText($"{docsFolder}/unheaded.txt", "unheadedneedle needs an honest source label.");
            File.WriteAllText($"{dataFolder}/issues.json", "{\"id\":\"MOCK-1\",\"summary\":\"mockdataneedle\"}");
            var extended = new KnowledgeBase(docsFolder, dataFolder);
            var continuation = extended.Search("continuationneedle", 1);
            Assert(continuation.Contains("section=Continuation Rules"),
                "Long paragraph continuations must inherit the same heading.");
            Assert(continuation.Contains($"source={docsFolder}/long.md") && !continuation.Contains(new string('x', 1_200)),
                "The continuation must be its own bounded chunk from the correct source.");
            var longChunks = extended.Search("Continuation Rules", 3).Split("\n\n");
            Assert(longChunks.Length == 3 && longChunks.All(chunk =>
                    chunk.Contains("section=Continuation Rules") && chunk[(chunk.IndexOf('\n') + 1)..].Length <= 1_200),
                "Every long chunk must carry the heading and obey the content length bound.");
            Assert(extended.Search("secondsectionneedle", 1).Contains("section=Later Section"),
                "A subsequent heading must replace the previous section.");
            Assert(extended.Search("unheadedneedle", 1).Contains("section=(no heading)"),
                "Plain text must not invent a Markdown section.");
            var mockData = extended.SearchData("mockdataneedle", 1);
            Assert(mockData.Contains($"mode=simulated; source={dataFolder}/issues.json; section=(no heading)"),
                "Data searches must keep the simulated mode and actual source.");
            Assert(extended.SearchData("parental") == "status=not_found; reason=no_matching_simulated_data",
                "Data-only searches must not leak policy-document hits.");

            File.WriteAllText($"{docsFolder}/oversized-read.md", "# Large Policy\n\n" + new string('z', 9_000));
            var truncated = new KnowledgeBase(docsFolder, dataFolder).Read($"{docsFolder}/oversized-read.md");
            Assert(truncated.Contains("section=full_document; content_complete=False") && truncated.EndsWith("[truncated]"),
                "Large reads must disclose that source context is incomplete.");
            Assert(truncated[(truncated.IndexOf('\n') + 1)..].Length == 8_000 + "\n[truncated]".Length,
                "The full-read content limit must remain bounded.");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            testDirectory.Delete(recursive: true);
        }
        return assertions;

        void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }
    }

    private static int CountHits(string result)
        => result.Split("status=found;", StringSplitOptions.None).Length - 1;
}
