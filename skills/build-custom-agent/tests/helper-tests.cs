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
        var skill = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(CurrentFile())!, ".."));
        var copier = Path.Combine(skill, "scripts", "copy-template.cs");
        var validator = Path.Combine(skill, "scripts", "validate-project.cs");
        var root = Path.Combine(Path.GetTempPath(), $"build-custom-agent-tests-{Guid.NewGuid():N}");
        string? tempAliasRoot = null;
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid");
            ExpectExit(0, copier, "--target", valid);
            ExpectExit(0, validator, "--target", valid);
            var settingsPath = Path.Combine(valid, "appsettings.json");
            File.WriteAllText(
                settingsPath,
                File.ReadAllText(settingsPath)
                    .Replace("Custom Knowledge Assistant", "HR Knowledge Assistant", StringComparison.Ordinal)
                    .Replace("custom-knowledge-assistant", "hr-knowledge-assistant", StringComparison.Ordinal));
            File.WriteAllText(
                Path.Combine(valid, "docs", "sample-knowledge.md"),
                "# Synthetic HR knowledge\n\nThis is local prototype content.\n");
            File.WriteAllText(
                Path.Combine(valid, "INTEGRATION_PLAN.md"),
                "# Future adapter\n\nA developer must implement and secure the live adapter.\n");
            File.AppendAllText(
                Path.Combine(valid, "Tools.cs"),
                "\n// Mentioning HttpClient in a comment is harmless and must not trigger validation.\n");
            ExpectExit(0, validator, "--target", valid);
            ExpectBuild(Path.Combine(valid, "CustomAgent.csproj"));
            ExpectExit(0, validator, "--target", valid);
            ExpectExit(2, copier, "--target", valid);

            var fixedEdit = CopyFresh(copier, root, "fixed-edit");
            File.AppendAllText(Path.Combine(fixedEdit, "Program.cs"), "\n// changed\n");
            ExpectExit(2, validator, "--target", fixedEdit);

            var unsafeEdit = CopyFresh(copier, root, "unsafe-edit");
            File.AppendAllText(
                Path.Combine(unsafeEdit, "Tools.cs"),
                "\npublic sealed class UnsafeProbe { public bool Exists(string path) => new FileInfo(path).Exists; }\n");
            ExpectExit(2, validator, "--target", unsafeEdit);

            var secretFile = CopyFresh(copier, root, "secret-file");
            File.WriteAllText(Path.Combine(secretFile, ".env"), "EXAMPLE=not-a-real-secret\n");
            ExpectExit(2, validator, "--target", secretFile);

            var modelEdit = CopyFresh(copier, root, "model-edit");
            var modelSettings = Path.Combine(modelEdit, "appsettings.json");
            File.WriteAllText(
                modelSettings,
                File.ReadAllText(modelSettings).Replace("gpt-4.1", "another-model", StringComparison.Ordinal));
            ExpectExit(2, validator, "--target", modelEdit);

            var weakSmoke = CopyFresh(copier, root, "weak-smoke");
            var weakSmokeSettings = Path.Combine(weakSmoke, "appsettings.json");
            var settings = JsonNode.Parse(File.ReadAllText(weakSmokeSettings))!;
            foreach (var smokeCase in settings["Smoke"]!["Cases"]!.AsArray())
                smokeCase!["ExpectedMarkers"] = new JsonArray(JsonValue.Create("the"));
            File.WriteAllText(
                weakSmokeSettings,
                settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ExpectExit(2, validator, "--target", weakSmoke);

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
    {
        _assertions++;
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
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
