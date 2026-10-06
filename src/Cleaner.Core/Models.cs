using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cleaner.Core;

public sealed record CleaningRule(string Id, int Version, string Title, string RelativePath,
    string[] Patterns, int MinimumAgeDays, bool Recursive, string Risk, string Explanation,
    string Category = "System caches");
public sealed record ScanOptions(int MinimumAgeDays = 0, string[]? ExcludedPaths = null)
{
    public bool Excludes(string path) => (ExcludedPaths ?? []).Any(excluded =>
        path.Equals(excluded, StringComparison.OrdinalIgnoreCase) || PathSafety.IsChild(path, excluded));
}
public sealed record FileSnapshot(string Path, long Bytes, DateTime LastWriteUtc,
    DateTime CreationUtc, string Identity);
public sealed record Candidate(string RuleId, FileSnapshot File);
public sealed record ScanIssue(string RuleId, string Path, string Reason);
public sealed record ScanProgress(string Title, int FilesExamined, int Candidates);
public sealed record RuleScan(CleaningRule Rule, string Root, string Status, Candidate[] Files)
{
    public long Bytes => Files.Sum(x => x.File.Bytes);
}
public sealed record ScanReport(DateTime StartedUtc, DateTime CompletedUtc, bool Cancelled,
    RuleScan[] Rules, ScanIssue[] Issues, ScanOptions? Options = null)
{
    public int FileCount => Rules.Sum(x => x.Files.Length);
    public long CandidateBytes => Rules.Sum(x => x.Bytes);
}
public sealed record CleanupEntry(string RuleId, string Path, string Status, long Bytes, string Detail);
public sealed record CleanupReport(DateTime StartedUtc, DateTime CompletedUtc, bool Cancelled,
    CleanupEntry[] Entries)
{
    public int RecycledFiles => Entries.Count(x => x.Status == "Recycled");
    public long RecycledBytes => Entries.Where(x => x.Status == "Recycled").Sum(x => x.Bytes);
    // Recycling retains disk allocation. Do not represent recycled bytes as reclaimed space.
    public long SpaceReclaimedBytes => 0;
}
public static class ReportJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
public static class ByteSize
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.##} {units[i]}";
    }
}
