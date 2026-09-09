using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

return HelperTests.Run();

static class HelperTests
{
    private static int _assertions;

    public static int Run()
    {
        var repository = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(CurrentFile())!, "..", "..", ".."));
        var skill = Path.Combine(repository, "skills", "build-custom-agent");
        var copier = Path.Combine(skill, "scripts", "copy-template.cs");
        var validator = Path.Combine(skill, "scripts", "validate-project.cs");
        var root = Path.Combine(Path.GetTempPath(), $"build-custom-agent-tests-{Guid.NewGuid():N}");
        string? tempAliasRoot = null;
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid");
            ExpectExit(0, copier, "--target", valid);
            Expect(!File.Exists(Path.Combine(valid, ".env.example")),
                "Copied projects must not contain .env.example.");
            var copiedProgram = File.ReadAllText(Path.Combine(valid, "Program.cs"));
            Expect(copiedProgram.Contains("AddObservability", StringComparison.Ordinal) &&
                   copiedProgram.Contains("AddToolObservability", StringComparison.Ordinal) &&
                   copiedProgram.Contains("RecordInputs = false", StringComparison.Ordinal) &&
                   copiedProgram.Contains("RecordOutputs = false", StringComparison.Ordinal) &&
                   copiedProgram.Contains("new FunctionInvokingChatClient", StringComparison.Ordinal) &&
                   copiedProgram.Contains("MaximumIterationsPerRequest = 3", StringComparison.Ordinal) &&
                   copiedProgram.Contains("MaxOutputTokens = 800", StringComparison.Ordinal) &&
                   copiedProgram.Contains("AllowMultipleToolCalls = false", StringComparison.Ordinal),
                "The fixed runtime must configure SDK tracing and preserve explicit model/tool bounds.");
            Expect(!File.Exists(Path.Combine(valid, "MetadataOnlyChatClient.cs")) &&
                   !File.Exists(Path.Combine(valid, "MetadataOnlyTool.cs")),
                "Copied projects must not contain replacement SDK wrappers.");
            ExpectExit(0, validator, "--target", valid);
            var settingsPath = Path.Combine(valid, "appsettings.json");
            File.WriteAllText(
                settingsPath,
                File.ReadAllText(settingsPath)
                    .Replace("Custom Knowledge Assistant", "HR Knowledge Assistant", StringComparison.Ordinal)
                    .Replace("custom-knowledge-assistant", "hr-knowledge-assistant", StringComparison.Ordinal)
                    .Replace("\"Preset\": \"knowledge\"", "\"Preset\": \"workflow\"", StringComparison.Ordinal)
                    .Replace(
                        "Ask about local content or look up a simulated record…",
                        "Ask an HR policy question…",
                        StringComparison.Ordinal));
            File.WriteAllText(
                Path.Combine(valid, "docs", "sample-knowledge.md"),
                "# Synthetic HR knowledge\n\nThis is local prototype content.\n");
            File.WriteAllText(
                Path.Combine(valid, "INTEGRATION_PLAN.md"),
                "# Future adapter\n\nA developer must implement and secure the live adapter.\n");
            File.AppendAllText(
                Path.Combine(valid, "Tools.cs"),
                "\n// Mentioning HttpClient in a comment is harmless and must not trigger validation.\n");
            Directory.CreateDirectory(Path.Combine(valid, "docs", "nested"));
            Directory.CreateDirectory(Path.Combine(valid, "data", "nested"));
            File.WriteAllText(Path.Combine(valid, "docs", "nested", "policy.md"), "# Nested policy\n\nTest guidance.");
            File.WriteAllText(Path.Combine(valid, "data", "nested", "records.csv"), "id,status\nMOCK-1,open\n");
            DeclareSource(settingsPath, "docs/nested/policy.md", "supplied");
            DeclareSource(settingsPath, "data/nested/records.csv", "mock");
            ExpectExit(0, validator, "--target", valid);
            ExpectBuild(Path.Combine(valid, "CustomAgent.csproj"));
            var output = Path.Combine(valid, "bin", "Release", "net10.0");
            Expect(File.Exists(Path.Combine(output, "docs", "sample-knowledge.md")),
                "Built source labels must keep their relative paths. Found: " +
                string.Join(", ", Directory.GetFiles(valid, "sample-knowledge.md", SearchOption.AllDirectories)));
            Expect(File.Exists(Path.Combine(output, "docs", "nested", "policy.md")), "Nested Markdown paths must remain unchanged.");
            Expect(File.Exists(Path.Combine(output, "data", "nested", "records.csv")), "Nested CSV paths must remain unchanged.");
            Expect(File.Exists(Path.Combine(output, "data", "sample-records.json")), "JSON source paths must remain unchanged.");
            Expect(File.Exists(Path.Combine(output, "wwwroot", "index.html")), "The UI must remain under wwwroot.");
            Expect(!Directory.Exists(Path.Combine(output, "docs", "docs")), "The build must not duplicate the docs prefix.");
            ExpectExit(0, validator, "--target", valid);
            ExpectExit(2, copier, "--target", valid);

            var frozen = CopyFresh(copier, root, "frozen-smoke");
            var frozenSettingsPath = Path.Combine(frozen, "appsettings.json");
            var baseline = Path.Combine(root, "smoke-baseline.json");
            File.Copy(frozenSettingsPath, baseline);
            var baselineText = File.ReadAllText(baseline);
            ExpectExit(0, validator, "--target", frozen, "--smoke-baseline", baseline);
            var frozenSettings = JsonNode.Parse(baselineText)!;
            frozenSettings["Agent"]!["Purpose"] = "A revised local prototype purpose.";
            File.WriteAllText(frozenSettingsPath, frozenSettings.ToJsonString());
            ExpectExit(0, validator, "--target", frozen, "--smoke-baseline", baseline);
            // Whitespace and object-property ordering are not changes to expectations.
            foreach (var smokeCase in frozenSettings["Smoke"]!["Cases"]!.AsArray())
            {
                var smokeObject = smokeCase!.AsObject();
                var prompt = smokeObject["Prompt"];
                smokeObject.Remove("Prompt");
                smokeObject.Add("Prompt", prompt);
            }
            File.WriteAllText(frozenSettingsPath, frozenSettings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ExpectExit(0, validator, "--smoke-baseline", baseline, "--target", frozen);
            frozenSettings = JsonNode.Parse(baselineText)!;
            frozenSettings["Smoke"]!["Cases"]![1]!["ExpectedMarkers"]!.AsArray().RemoveAt(0);
            File.WriteAllText(frozenSettingsPath, frozenSettings.ToJsonString());
            ExpectExit(0, validator, "--target", frozen);
            ExpectExit(2, validator, "--target", frozen, "--smoke-baseline", baseline);
            frozenSettings = JsonNode.Parse(baselineText)!;
            frozenSettings["Smoke"]!["Cases"]![1]!["Prompt"] = "A changed smoke question.";
            File.WriteAllText(frozenSettingsPath, frozenSettings.ToJsonString());
            ExpectExit(2, validator, "--target", frozen, "--smoke-baseline", baseline);
            // Without the flag, ordinary pre-test customization is still allowed.
            ExpectExit(0, validator, "--target", frozen);
            File.WriteAllText(frozenSettingsPath, baselineText);
            ExpectExit(2, validator, "--target", frozen, "--smoke-baseline", Path.Combine(root, "missing.json"));
            ExpectExit(2, validator, "--target", frozen, "--smoke-baseline", frozenSettingsPath);
            var invalidBaseline = Path.Combine(root, "invalid-baseline.json");
            File.WriteAllText(invalidBaseline, "{}");
            ExpectExit(2, validator, "--target", frozen, "--smoke-baseline", invalidBaseline);
            File.WriteAllText(invalidBaseline, "not JSON");
            ExpectExit(2, validator, "--target", frozen, "--smoke-baseline", invalidBaseline);
            var baselineLink = Path.Combine(root, "baseline-link.json");
            File.CreateSymbolicLink(baselineLink, baseline);
            ExpectExit(2, validator, "--target", frozen, "--smoke-baseline", baselineLink);
            Expect(File.ReadAllText(baseline) == baselineText, "Validation must never overwrite the smoke baseline.");

            var fixedEdit = CopyFresh(copier, root, "fixed-edit");
            File.AppendAllText(Path.Combine(fixedEdit, "Program.cs"), "\n// changed\n");
            ExpectExit(2, validator, "--target", fixedEdit);

            var unsafeEdit = CopyFresh(copier, root, "unsafe-edit");
            File.AppendAllText(
                Path.Combine(unsafeEdit, "Tools.cs"),
                "\npublic sealed class UnsafeProbe { public bool Exists(string path) => new FileInfo(path).Exists; }\n");
            ExpectExit(2, validator, "--target", unsafeEdit);

            var harmlessText = CopyFresh(copier, root, "harmless-text");
            File.AppendAllText(Path.Combine(harmlessText, "Tools.cs"), """"

                public static class HarmlessText
                {
                    public const string Ordinary = "HttpClient, Credential, File.Open, and Process.Start are prose.";
                    public const string Verbatim = @"System.Net and Environment.GetEnvironmentVariable are prose.";
                    public const string Raw = """
                        global::System.IO.File and unsafe stackalloc are prose.
                        """;
                }
                """");
            ExpectExit(0, validator, "--target", harmlessText);
            ExpectBuild(Path.Combine(harmlessText, "CustomAgent.csproj"));

            foreach (var (name, code) in new[]
            {
                ("alias-bypass", "\nusing IO = System.IO;\n"),
                ("static-bypass", "\nusing static System.IO.File;\n"),
                ("global-bypass", "\npublic static class Probe { public static bool Read() => global::System.IO.File.Exists(\"x\"); }\n"),
                ("unsafe-bypass", "\npublic unsafe static class Probe { public static int* Read() => stackalloc int[1]; }\n"),
            })
            {
                var bypass = CopyFresh(copier, root, name);
                File.AppendAllText(Path.Combine(bypass, "Tools.cs"), code);
                ExpectExit(2, validator, "--target", bypass);
            }

            var inventedSource = CopyFresh(copier, root, "invented-source");
            File.AppendAllText(Path.Combine(inventedSource, "Tools.cs"),
                "\npublic static class SourceProbe { public const string Citation = \"data/nonexistent-issues.json\"; }\n");
            ExpectExit(2, validator, "--target", inventedSource);

            var completeSource = CopyFresh(copier, root, "complete-source");
            File.WriteAllText(Path.Combine(completeSource, "docs", "policy.md-v2.md"), "# Revised policy\n\nExample only.");
            DeclareSource(Path.Combine(completeSource, "appsettings.json"), "docs/policy.md-v2.md", "mock");
            File.AppendAllText(Path.Combine(completeSource, "Tools.cs"),
                "\npublic static class SourceProbe { public const string Citation = \"docs/policy.md-v2.md\"; }\n");
            ExpectExit(0, validator, "--target", completeSource);

            var unicodeSource = CopyFresh(copier, root, "unicode-source");
            File.WriteAllText(Path.Combine(unicodeSource, "docs", "правила.md"), "# Правила\n\nПримерни данни.");
            var unicodeSettingsPath = Path.Combine(unicodeSource, "appsettings.json");
            var unicodeSettings = JsonNode.Parse(File.ReadAllText(unicodeSettingsPath))!;
            unicodeSettings["Agent"]!["Examples"]![0] = "Explain docs/правила.md.";
            unicodeSettings["Content"]!["Sources"]!["docs/правила.md"] = "supplied";
            var escapedSettings = unicodeSettings.ToJsonString();
            Expect(escapedSettings.Contains("\\u", StringComparison.Ordinal), "The fixture must actually use JSON Unicode escapes.");
            File.WriteAllText(unicodeSettingsPath, escapedSettings);
            ExpectExit(0, validator, "--target", unicodeSource);

            var undeclaredSource = CopyFresh(copier, root, "undeclared-source");
            File.WriteAllText(Path.Combine(undeclaredSource, "docs", "extra.md"), "# Extra\n\nUndeclared.");
            ExpectExit(2, validator, "--target", undeclaredSource);

            var missingSource = CopyFresh(copier, root, "missing-source");
            File.Delete(Path.Combine(missingSource, "docs", "sample-knowledge.md"));
            ExpectExit(2, validator, "--target", missingSource);

            var invalidProvenance = CopyFresh(copier, root, "invalid-provenance");
            DeclareSource(Path.Combine(invalidProvenance, "appsettings.json"),
                "docs/sample-knowledge.md", "synthetic");
            ExpectExit(2, validator, "--target", invalidProvenance);

            var unknownDeclaration = CopyFresh(copier, root, "unknown-declaration");
            DeclareSource(Path.Combine(unknownDeclaration, "appsettings.json"),
                "docs/not-present.md", "mock");
            ExpectExit(2, validator, "--target", unknownDeclaration);

            var secretFile = CopyFresh(copier, root, "secret-file");
            File.WriteAllText(Path.Combine(secretFile, ".env"), "EXAMPLE=not-a-real-secret\n");
            ExpectExit(2, validator, "--target", secretFile);

            var secretExample = CopyFresh(copier, root, "secret-example");
            File.WriteAllText(Path.Combine(secretExample, ".env.example"), "EXAMPLE=not-a-real-secret\n");
            ExpectExit(2, validator, "--target", secretExample);

            var modelEdit = CopyFresh(copier, root, "model-edit");
            var modelSettings = Path.Combine(modelEdit, "appsettings.json");
            File.WriteAllText(
                modelSettings,
                File.ReadAllText(modelSettings).Replace("gpt-4.1", "another-model", StringComparison.Ordinal));
            ExpectExit(2, validator, "--target", modelEdit);

            var invalidUiPreset = CopyFresh(copier, root, "invalid-ui-preset");
            var invalidUiSettings = Path.Combine(invalidUiPreset, "appsettings.json");
            File.WriteAllText(
                invalidUiSettings,
                File.ReadAllText(invalidUiSettings)
                    .Replace("\"Preset\": \"knowledge\"", "\"Preset\": \"custom-css\"", StringComparison.Ordinal));
            ExpectExit(2, validator, "--target", invalidUiPreset);

            var invalidUiPlaceholder = CopyFresh(copier, root, "invalid-ui-placeholder");
            var invalidPlaceholderSettings = Path.Combine(invalidUiPlaceholder, "appsettings.json");
            var invalidPlaceholderJson = JsonNode.Parse(File.ReadAllText(invalidPlaceholderSettings))!;
            invalidPlaceholderJson["Agent"]!["Ui"]!["InputPlaceholder"] = new string('x', 141);
            File.WriteAllText(
                invalidPlaceholderSettings,
                invalidPlaceholderJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ExpectExit(2, validator, "--target", invalidUiPlaceholder);

            var weakSmoke = CopyFresh(copier, root, "weak-smoke");
            var weakSmokeSettings = Path.Combine(weakSmoke, "appsettings.json");
            var settings = JsonNode.Parse(File.ReadAllText(weakSmokeSettings))!;
            foreach (var smokeCase in settings["Smoke"]!["Cases"]!.AsArray())
                smokeCase!["ExpectedMarkers"] = new JsonArray(JsonValue.Create("the"));
            File.WriteAllText(
                weakSmokeSettings,
                settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ExpectExit(2, validator, "--target", weakSmoke);

            // Short domain facts are useful when paired with a source or another concrete fact.
            var shortSmoke = CopyFresh(copier, root, "short-smoke");
            var shortSettingsPath = Path.Combine(shortSmoke, "appsettings.json");
            var shortSettings = JsonNode.Parse(File.ReadAllText(shortSettingsPath))!;
            foreach (var markers in new[]
            {
                new[] { "P2", "Ada", "data/sample-records.json" },
                new[] { "P2", "Engineering" },
            })
            {
                shortSettings["Smoke"]!["Cases"]![1]!["ExpectedMarkers"] = new JsonArray(markers.Select(marker => JsonValue.Create(marker)).ToArray());
                File.WriteAllText(shortSettingsPath, shortSettings.ToJsonString());
                ExpectExit(0, validator, "--target", shortSmoke);
            }
            foreach (var markers in new[]
            {
                new[] { "P2", "Ada" },
                new[] { "x", "data/sample-records.json" },
                new[] { "..", "data/sample-records.json" },
            })
            {
                shortSettings["Smoke"]!["Cases"]![1]!["ExpectedMarkers"] = new JsonArray(markers.Select(marker => JsonValue.Create(marker)).ToArray());
                File.WriteAllText(shortSettingsPath, shortSettings.ToJsonString());
                ExpectExit(2, validator, "--target", shortSmoke);
            }

            var diagnosticSmoke = CopyFresh(copier, root, "diagnostic-smoke");
            var diagnosticSettings = Path.Combine(diagnosticSmoke, "appsettings.json");
            var diagnosticJson = JsonNode.Parse(File.ReadAllText(diagnosticSettings))!;
            diagnosticJson["Smoke"]!["Cases"]![1]!["ExpectedMarkers"] = new JsonArray(JsonValue.Create("mode=simulated"));
            File.WriteAllText(diagnosticSettings, diagnosticJson.ToJsonString());
            ExpectExit(2, validator, "--target", diagnosticSmoke);

            var toolOverflow = CopyFresh(copier, root, "tool-overflow");
            var definitionPath = Path.Combine(toolOverflow, "AgentDefinition.cs");
            File.WriteAllText(
                definitionPath,
                File.ReadAllText(definitionPath).Replace(
                    "AIFunctionFactory.Create(tools.LookupLocalRecord),",
                    "AIFunctionFactory.Create(tools.LookupLocalRecord),\n            AIFunctionFactory.Create(tools.SearchLocalContent),",
                    StringComparison.Ordinal));
            ExpectExit(2, validator, "--target", toolOverflow);

            var realParent = Path.Combine(root, "real-parent");
            Directory.CreateDirectory(realParent);
            var linkParent = Path.Combine(root, "link-parent");
            Directory.CreateSymbolicLink(linkParent, realParent);
            ExpectExit(2, copier, "--target", Path.Combine(linkParent, "via-symlink"));
            var realTarget = Path.Combine(realParent, "real-target");
            ExpectExit(0, copier, "--target", realTarget);
            ExpectExit(2, validator, "--target", Path.Combine(linkParent, "real-target"));

            var workingDirectoryTarget = Path.Combine(root, "working-directory-target");
            Directory.CreateDirectory(workingDirectoryTarget);
            ExpectExitFrom(2, workingDirectoryTarget, copier, "--target", ".");
            Expect(Directory.Exists(workingDirectoryTarget) &&
                   !Directory.EnumerateFileSystemEntries(workingDirectoryTarget).Any(),
                "The copier must not replace its process working directory.");
            var otherEmptyTarget = Path.Combine(root, "other-empty-target");
            Directory.CreateDirectory(otherEmptyTarget);
            ExpectExit(0, copier, "--target", otherEmptyTarget);
            Expect(File.Exists(Path.Combine(otherEmptyTarget, "CustomAgent.csproj")),
                "A different real empty directory remains a supported target.");

            if (!OperatingSystem.IsWindows() && Directory.Exists("/tmp"))
            {
                tempAliasRoot = Path.Combine("/tmp", $"build-custom-agent-alias-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempAliasRoot);
                var aliasTarget = Path.Combine(tempAliasRoot, "valid");
                ExpectExit(0, copier, "--target", aliasTarget);
                ExpectExit(0, validator, "--target", aliasTarget);
            }

            Console.WriteLine($"HELPER_TESTS_OK assertions={_assertions}");
            return 0;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (tempAliasRoot is not null && Directory.Exists(tempAliasRoot))
                Directory.Delete(tempAliasRoot, recursive: true);
        }
    }

    private static string CopyFresh(string copier, string root, string name)
    {
        var target = Path.Combine(root, name);
        ExpectExit(0, copier, "--target", target);
        return target;
    }

    private static void ExpectExit(int expected, string script, params string[] arguments)
        => ExpectExitCore(expected, null, script, arguments);

    private static void ExpectExitFrom(
        int expected,
        string workingDirectory,
        string script,
        params string[] arguments)
        => ExpectExitCore(expected, workingDirectory, script, arguments);

    private static void ExpectExitCore(
        int expected,
        string? workingDirectory,
        string script,
        params string[] arguments)
    {
        _assertions++;
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--file");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start dotnet helper.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != expected)
        {
            throw new InvalidOperationException(
                $"Expected exit {expected}, got {process.ExitCode} for {Path.GetFileName(script)}.\n{stdout}{stderr}");
        }
    }

    private static void DeclareSource(
        string settingsPath,
        string label,
        string provenance)
    {
        var settings = JsonNode.Parse(File.ReadAllText(settingsPath))!;
        settings["Content"]!["Sources"]![label] = provenance;
        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Expect(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectBuild(string project)
    {
        _assertions++;
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("--configuration");
        start.ArgumentList.Add("Release");

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Generated starter build failed.\n{stdout}{stderr}");
    }

    private static string CurrentFile([CallerFilePath] string path = "") => path;
}
