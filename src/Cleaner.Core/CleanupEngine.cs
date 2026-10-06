using System.Text.Json;

namespace Cleaner.Core;

public interface IRecycler
{
    // The implementation must call validateAgain immediately before the operation.
    void Recycle(string path, Action validateAgain);
}

public sealed class CleanupEngine
{
    private readonly CleaningRule[] rules;
    private readonly PathSafety safety;
    private readonly IRecycler recycler;
    private readonly ScanOptions options;
    public CleanupEngine(string? localRoot = null, CleaningRule[]? catalog = null, IRecycler? recycler = null, ScanOptions? options = null)
    {
        rules = catalog ?? RuleCatalog.Load();
        safety = new(localRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), rules);
        this.recycler = recycler ?? new ShellRecycler();
        this.options = options ?? new();
    }

    public CleanupReport Recycle(IReadOnlyList<Candidate> selection, string journalPath,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (selection.Count > CacheScanner.MaximumCandidates) throw new ArgumentException("Selection is too large.");
        var started = DateTime.UtcNow;
        var entries = new List<CleanupEntry>();
        bool cancelled = false;
        // A new journal is mandatory. Never overwrite another run or clean if logging fails.
        using var journal = new StreamWriter(new FileStream(journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
        Write(new { kind = "start", at = started, selected = selection.Count });
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in selection)
        {
            if (cancellation.IsCancellationRequested) { cancelled = true; break; }
            if (!seen.Add(candidate.File.Path)) continue;
            progress?.Report(candidate.File.Path);
            CleanupEntry entry;
            try
            {
                var rule = rules.SingleOrDefault(x => x.Id == candidate.RuleId)
                    ?? throw new IOException("Unknown rule.");
                var root = safety.RootFor(rule);
                safety.Check(candidate.File.Path, root, true);
                using var lease = DirectoryLease.Acquire(candidate.File.Path);
                void Validate()
                {
                    cancellation.ThrowIfCancellationRequested();
                    safety.Check(candidate.File.Path, root, true);
                    if (options.Excludes(candidate.File.Path)) throw new IOException("This path is excluded by your settings.");
                    if (!PathSafety.Matches(rule, candidate.File.Path)) throw new IOException("File no longer matches the rule.");
                    var current = FileIdentity.Read(candidate.File.Path);
                    if (current != candidate.File) throw new IOException("File changed since the scan. Scan again.");
                    int age = Math.Max(rule.MinimumAgeDays, options.MinimumAgeDays);
                    if (current.LastWriteUtc > started.AddDays(-age) ||
                        current.CreationUtc > started.AddDays(-age))
                        throw new IOException("File is too recent.");
                }
                Validate();
                Write(new { kind = "intent", candidate.RuleId, candidate.File.Path, candidate.File.Identity, at = DateTime.UtcNow });
                recycler.Recycle(candidate.File.Path, Validate);
                entry = new(candidate.RuleId, candidate.File.Path, "Recycled", candidate.File.Bytes,
                    "Moved to the Windows Recycle Bin. Restore there; disk space is retained until it is emptied.");
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                entry = new(candidate.RuleId, candidate.File.Path, "Cancelled", 0, "Cleanup was cancelled.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
            { entry = new(candidate.RuleId, candidate.File.Path, "Skipped", 0, ex.Message); }
            entries.Add(entry);
            Write(new { kind = "result", entry, at = DateTime.UtcNow });
            if (cancelled) break;
        }
        cancelled |= cancellation.IsCancellationRequested;
        var report = new CleanupReport(started, DateTime.UtcNow, cancelled, entries.ToArray());
        Write(new { kind = "finish", report });
        return report;

        void Write<T>(T value) => journal.WriteLine(JsonSerializer.Serialize(value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
