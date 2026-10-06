using System.IO.Enumeration;

namespace Cleaner.Core;

public sealed class PathSafety
{
    public string LocalRoot { get; }
    private readonly HashSet<string> approvedRoots;

    public PathSafety(string localRoot, IReadOnlyList<CleaningRule> rules)
    {
        RuleCatalog.Validate(rules);
        LocalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localRoot));
        if (!Path.IsPathFullyQualified(LocalRoot) || LocalRoot.StartsWith(@"\\", StringComparison.Ordinal) ||
            LocalRoot.Length <= (Path.GetPathRoot(LocalRoot)?.Length ?? 0))
            throw new ArgumentException("A local, bounded application data root is required.");
        approvedRoots = rules.Select(RootFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string RootFor(CleaningRule rule)
    {
        var path = Path.GetFullPath(Path.Combine(LocalRoot, rule.RelativePath));
        if (!IsChild(path, LocalRoot)) throw new ArgumentException("Rule escapes application data.");
        return path;
    }

    public static bool IsChild(string path, string root) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void Check(string path, string root, bool file)
    {
        var full = Path.GetFullPath(path);
        if (!approvedRoots.Contains(root) || (!full.Equals(root, StringComparison.OrdinalIgnoreCase) && !IsChild(full, root)) ||
            !IsChild(full, LocalRoot) || full.StartsWith(@"\\", StringComparison.Ordinal) || full[2..].Contains(':'))
            throw new IOException("Path is outside an approved cache root.");
        // Check every ancestor, including ancestors above LocalAppData, for redirection.
        string? current = file ? Path.GetDirectoryName(full) : full;
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked or redirected folders are excluded.");
            current = Path.GetDirectoryName(current);
        }
        if (file && (File.GetAttributes(full) & (FileAttributes.ReparsePoint | FileAttributes.Directory |
            FileAttributes.System | FileAttributes.ReadOnly | FileAttributes.Offline | FileAttributes.Encrypted)) != 0)
            throw new IOException("Protected, linked, offline, or read-only file.");
    }

    public static bool Matches(CleaningRule rule, string path) => rule.Patterns.Any(pattern =>
        FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true));
}
