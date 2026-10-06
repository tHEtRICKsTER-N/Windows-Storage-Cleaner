namespace Cleaner.Core;

public sealed class CacheScanner
{
    public const int MaximumCandidates = 100_000;
    public const int MaximumIssues = 2_000;
    private readonly CleaningRule[] rules;
    private readonly PathSafety safety;
    public CacheScanner(string? localRoot = null, CleaningRule[]? catalog = null)
    {
        rules = catalog ?? RuleCatalog.Load();
        safety = new(localRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), rules);
    }

    public ScanReport Scan(IProgress<ScanProgress>? progress = null, CancellationToken cancellation = default, ScanOptions? options = null)
    {
        options ??= new();
        if (options.MinimumAgeDays < 0 || options.MinimumAgeDays > 3650) throw new ArgumentException("Minimum age must be between 0 and 3650 days.");
        options = options with { ExcludedPaths = (options.ExcludedPaths ?? []).Select(Path.GetFullPath).ToArray() };
        var started = DateTime.UtcNow;
        var result = new List<RuleScan>();
        var issues = new List<ScanIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int examined = 0;
        bool cancelled = false;
        foreach (var rule in rules)
        {
            string root = safety.RootFor(rule);
            var files = new List<Candidate>();
            string status = "Scanned";
            try
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(new(rule.Title, examined, seen.Count));
                if (options.Excludes(root)) status = "Excluded by you";
                else if (!Directory.Exists(root)) status = "Not present";
                else
                {
                    var queue = new Stack<string>();
                    queue.Push(root);
                    while (queue.TryPop(out var folder))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try
                        {
                            safety.Check(folder, root, false);
                            foreach (string path in Directory.EnumerateFileSystemEntries(folder))
                            {
                                cancellation.ThrowIfCancellationRequested();
                                if (options.Excludes(path)) continue;
                                if (seen.Count >= MaximumCandidates) { status = "Candidate limit reached"; queue.Clear(); break; }
                                try
                                {
                                    var attributes = File.GetAttributes(path);
                                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                                        throw new IOException("Linked files and folders are excluded.");
                                    if ((attributes & FileAttributes.Directory) != 0)
                                    {
                                        if (rule.Recursive) queue.Push(path);
                                        continue;
                                    }
                                    examined++;
                                    if (examined % 200 == 0) progress?.Report(new(rule.Title, examined, seen.Count));
                                    if (!PathSafety.Matches(rule, path)) continue;
                                    // Cheap age filtering avoids opening every recent cache file. The
                                    // handle snapshot below independently verifies both timestamps.
                                    var info = new FileInfo(path);
                                    int age = Math.Max(rule.MinimumAgeDays, options.MinimumAgeDays);
                                    if (info.LastWriteTimeUtc > started.AddDays(-age) ||
                                        info.CreationTimeUtc > started.AddDays(-age)) continue;
                                    safety.Check(path, root, true);
                                    var snapshot = FileIdentity.Read(path);
                                    if (snapshot.LastWriteUtc > started.AddDays(-age) ||
                                        snapshot.CreationUtc > started.AddDays(-age)) continue;
                                    if (seen.Add(snapshot.Identity)) files.Add(new(rule.Id, snapshot));
                                }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                { Issue(rule.Id, path, ex.Message); }
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { status = "Completed with exclusions"; Issue(rule.Id, folder, ex.Message); }
                    }
                }
            }
            catch (OperationCanceledException) { cancelled = true; status = "Cancelled"; }
            result.Add(new(rule, root, status, files.OrderByDescending(x => x.File.Bytes).ToArray()));
            if (cancelled) break;
        }
        return new(started, DateTime.UtcNow, cancelled, result.ToArray(), issues.ToArray(), options);

        void Issue(string ruleId, string path, string reason)
        {
            if (issues.Count < MaximumIssues) issues.Add(new(ruleId, path, reason));
        }
    }
}
