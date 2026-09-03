using System.Diagnostics;
using System.Runtime.CompilerServices;
using OperationsDataAnalyst;

internal static class OutputLayoutTests
{
    public static async Task<int> RunAsync()
    {
        var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(CurrentFile())!, "../../../.."));
        var source = Path.Combine(repository, "skills/build-template-agent/assets/operations-data-analyst");
        var temporary = Path.Combine(Path.GetTempPath(), "operations-layout-" + Guid.NewGuid().ToString("N"));
        var copied = Path.Combine(temporary, "copied-app");
        Directory.CreateDirectory(temporary);
        var checks = 0;
        try
        {
            await Dotnet("run", "--file", Path.Combine(repository, "skills/build-template-agent/scripts/copy-template.cs"),
                "--", "--template", "operations-data-analyst", "--target", copied);
            Check(!Directory.Exists(Path.Combine(copied, "bin")) && !Directory.Exists(Path.Combine(copied, "obj")),
                "customer copy starts without stale build artifacts");
            var project = Path.Combine(copied, "OperationsDataAnalyst.csproj");
            Check(File.ReadAllBytes(project).SequenceEqual(File.ReadAllBytes(Path.Combine(source, "OperationsDataAnalyst.csproj"))),
                "copied project is unchanged canonical source");
            await Dotnet("build", project, "--configuration", "Debug", "--nologo");
            VerifyOutput(Path.Combine(copied, "bin/Debug/net10.0"), "clean copied build");
            var publish = Path.Combine(temporary, "published-app");
            await Dotnet("publish", project, "--configuration", "Release", "--output", publish, "--nologo");
            VerifyOutput(publish, "clean copied publish");
            return checks;
        }
        finally { Directory.Delete(temporary, recursive: true); }

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + label);
            checks++;
        }
        void VerifyOutput(string output, string label)
        {
            foreach (var relative in new[] { "data/operations.csv", "wwwroot/index.html", "appsettings.json" })
            {
                var target = Path.Combine(output, relative);
                Check(File.Exists(target), label + " has exact runtime path " + target + "; candidates: " +
                    string.Join(", ", Directory.GetFiles(copied, Path.GetFileName(relative), SearchOption.AllDirectories)));
                Check(File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(Path.Combine(source, relative))),
                    label + " preserves source bytes for " + relative);
                var matches = Directory.GetFiles(output, Path.GetFileName(relative), SearchOption.AllDirectories);
                Check(matches.Length == 1 && matches[0] == target, label + " has no duplicated content path for " + relative);
            }
            Check(MetricsStore.Load(Path.Combine(output, "data/operations.csv")).RowCount == 90,
                label + " loads the actual deployed CSV, not a harness-linked fixture");
        }
    }

    private static async Task Dotnet(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet_start_failed");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var combined = await output + await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("Output-layout command failed: " + combined);
    }

    private static string CurrentFile([CallerFilePath] string path = "") => path;
}
