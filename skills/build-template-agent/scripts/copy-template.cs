using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

return TemplateCopier.Run(args);

static class TemplateCopier
{
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.Ordinal)
    {
        "bin", "obj", "__pycache__", ".git", ".vs", ".DS_Store"
    };

    public static int Run(string[] args)
    {
        try
        {
            var catalog = ReadCatalog();
            if (args.SequenceEqual(["--list"]))
            {
                Console.WriteLine(JsonSerializer.Serialize(catalog, TemplateJsonContext.Default.TemplateArray));
                return 0;
            }
            var (templateId, targetArgument) = ParseArguments(args);
            var template = catalog.SingleOrDefault(item => item.Id == templateId)
                ?? throw new ArgumentException($"Unknown template '{templateId}'. Use --list for available templates.");
            var source = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(CurrentFile())!,
                "..", "assets", template.Id));
            if (!File.Exists(Path.Combine(source, template.Project)))
                throw new ArgumentException($"Template project is missing: {template.Project}");
            var target = CopyTemplate(source, targetArgument);
            Console.WriteLine(target);
            return 0;
        }
        catch (Exception error) when (error is ArgumentException
                                      or IOException
                                      or UnauthorizedAccessException
                                      or JsonException)
        {
            Console.Error.WriteLine($"copy-template: {error.Message}");
            return 2;
        }
    }

    private static Template[] ReadCatalog()
    {
        var path = Path.Combine(Path.GetDirectoryName(CurrentFile())!, "..", "templates.json");
        var catalog = JsonSerializer.Deserialize(File.ReadAllText(path), TemplateJsonContext.Default.TemplateArray);
        if (catalog is not { Length: > 0 } ||
            catalog.Any(item => item is null ||
                !Regex.IsMatch(item.Id ?? "", "^[a-z0-9]+(-[a-z0-9]+)*$") ||
                string.IsNullOrWhiteSpace(item.Name) ||
                !Regex.IsMatch(item.Project ?? "", "^[A-Za-z][A-Za-z0-9]*[.]csproj$") ||
                item.SmokeCases is not { Length: 3 } ||
                item.SmokeCases.Any(id => !Regex.IsMatch(id ?? "", "^[a-z0-9]+(-[a-z0-9]+)*$")) ||
                item.SmokeCases.Distinct(StringComparer.Ordinal).Count() != 3) ||
            catalog.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Length)
        {
            throw new ArgumentException("Invalid template catalog.");
        }
        return catalog;
    }

    private static (string TemplateId, string Target) ParseArguments(string[] args)
    {
        var templateId = "release-evidence-reviewer";
        string? target = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length ||
                args[index] is not ("--template" or "--target") ||
                !seen.Add(args[index]) || string.IsNullOrWhiteSpace(args[index + 1]) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Usage: dotnet run --file copy-template.cs -- [--template <id>] [--target <directory>] | --list");
            if (args[index] == "--template") templateId = args[index + 1];
            else target = args[index + 1];
        }
        return (templateId, target ?? templateId);
    }

    private static string CopyTemplate(string source, string targetArgument)
    {
        ValidateSource(source);
        if (targetArgument.Split(Path.DirectorySeparatorChar,
                                 Path.AltDirectorySeparatorChar).Contains(".."))
        {
            throw new ArgumentException($"Target must not contain '..': {targetArgument}");
        }

        var target = Path.GetFullPath(targetArgument);
        var parent = Path.GetDirectoryName(target);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new ArgumentException($"Target parent does not exist: {parent}");
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (target.Equals(Path.GetFullPath(Directory.GetCurrentDirectory()), comparison))
        {
            throw new ArgumentException($"Target must not be the current working directory: {targetArgument}");
        }
        RejectSymlinkPathComponents(target, "Target");

        var sourcePrefix = source.TrimEnd(Path.DirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
        if (target.Equals(source, comparison) || target.StartsWith(sourcePrefix, comparison))
        {
            throw new ArgumentException(
                $"Target must be outside the template asset: {targetArgument}");
        }

        var targetExisted = Directory.Exists(target);
        if ((File.Exists(target) && !targetExisted) ||
            (targetExisted && Directory.EnumerateFileSystemEntries(target).Any()))
        {
            throw new ArgumentException(
                $"Target exists and is not an empty real directory: {targetArgument}");
        }

        var staging = Path.Combine(parent, $".{Path.GetFileName(target)}.copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            CopyDirectory(source, staging);

            if (targetExisted)
            {
                if (!Directory.Exists(target) || IsLink(target) ||
                    Directory.EnumerateFileSystemEntries(target).Any())
                {
                    throw new IOException(
                        $"Target changed during copy; refusing to replace it: {targetArgument}");
                }
                Directory.Delete(target);
            }
            else if (File.Exists(target) || Directory.Exists(target) || IsLink(target))
            {
                throw new IOException(
                    $"Target appeared during copy; refusing to overwrite it: {targetArgument}");
            }

            RejectSymlinkPathComponents(target, "Target");
            Directory.Move(staging, target);
            return target;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void ValidateSource(string source)
    {
        if (!Directory.Exists(source))
        {
            throw new ArgumentException($"Template asset is missing: {source}");
        }
        if (IsLink(source))
        {
            throw new ArgumentException($"Template asset must not be a symlink: {source}");
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     source, "*", SearchOption.AllDirectories))
        {
            if (IsLink(entry))
            {
                throw new ArgumentException($"Template asset contains a symlink: {entry}");
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(sourcePath);
            if (ShouldExclude(name))
            {
                continue;
            }

            var destinationPath = Path.Combine(destination, name);
            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destinationPath);
                CopyDirectory(sourcePath, destinationPath);
                Directory.SetLastWriteTimeUtc(
                    destinationPath, Directory.GetLastWriteTimeUtc(sourcePath));
            }
            else
            {
                File.Copy(sourcePath, destinationPath);
                File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
                }
            }
        }
    }

    private static bool ShouldExclude(string name) =>
        ExcludedNames.Contains(name) ||
        name == ".env" ||
        name.StartsWith(".env.", StringComparison.Ordinal);

    private static bool IsLink(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (FileNotFoundException)
        {
            return new FileInfo(path).LinkTarget is not null ||
                   new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
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

    private static string CurrentFile([CallerFilePath] string path = "") => path;

}

internal sealed record Template(string Id, string Name, string Project, string[] SmokeCases);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Template[]))]
internal partial class TemplateJsonContext : JsonSerializerContext;
