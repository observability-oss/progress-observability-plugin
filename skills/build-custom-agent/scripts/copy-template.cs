using System.Runtime.CompilerServices;

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
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                Console.WriteLine("Usage: dotnet run --file copy-template.cs -- [--target <directory>]");
                return 0;
            }

            var targetArgument = ParseTarget(args);
            var source = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(CurrentFile())!,
                "..", "assets", "custom-agent-starter"));
            var target = CopyTemplate(source, targetArgument);
            Console.WriteLine(target);
            return 0;
        }
        catch (Exception error) when (error is ArgumentException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"copy-template: {error.Message}");
            return 2;
        }
    }

    private static string ParseTarget(string[] args)
    {
        if (args.Length == 0)
        {
            return "custom-agent";
        }

        if (args.Length == 2 && args[0] == "--target" &&
            !string.IsNullOrWhiteSpace(args[1]))
        {
            return args[1];
        }

        throw new ArgumentException(
            "Usage: dotnet run --file copy-template.cs -- [--target <directory>]");
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

        RejectSymlinkPathComponents(target, "Target");

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
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
        (name.StartsWith(".env.", StringComparison.Ordinal) && name != ".env.example");

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
