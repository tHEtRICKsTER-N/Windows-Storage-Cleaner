using System.Text.Json;
using Cleaner.Core;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("""
        Windows Storage Cleaner 0.2.0

        cleaner rules
        cleaner scan [--dry-run] [--min-age DAYS] [--exclude PATH] [--json] [--output FILE]
        cleaner analyze FOLDER [--duplicates] [--min-mb MB] [--json] [--output FILE]

        All CLI commands are read-only. Duplicate checks apply to files meeting the size threshold.
        --exclude can be repeated. Reports never overwrite existing files.
        Use the desktop app to review and recycle cache files.
        """);
    return 0;
}
try
{
    if (args[0] == "rules" && args.Length == 1)
    {
        foreach (var rule in RuleCatalog.Load()) Console.WriteLine($"{rule.Id,-24} {rule.Title} | {rule.Category} | {rule.MinimumAgeDays}+ days | {rule.Risk}");
        return 0;
    }
    if (args[0] is not ("scan" or "analyze")) throw new ArgumentException("Unknown command. Run cleaner --help.");
    bool analyze = args[0] == "analyze", json = false, duplicates = false;
    string? output = null, folder = null;
    int age = 0; long minimumMb = 50;
    var exclusions = new List<string>();
    int start = 1;
    if (analyze)
    {
        if (args.Length < 2 || args[1].StartsWith("--")) throw new ArgumentException("analyze requires a folder path.");
        folder = Path.GetFullPath(args[1]); start = 2;
    }
    for (int i = start; i < args.Length; i++)
        switch (args[i])
        {
            case "--dry-run" when !analyze: break;
            case "--json": json = true; break;
            case "--output" when i + 1 < args.Length: output = Path.GetFullPath(args[++i]); break;
            case "--min-age" when !analyze && i + 1 < args.Length:
                if (!int.TryParse(args[++i], out age) || age is < 0 or > 3650) throw new ArgumentException("Minimum age must be 0–3650 days.");
                break;
            case "--exclude" when !analyze && i + 1 < args.Length: exclusions.Add(Path.GetFullPath(args[++i])); break;
            case "--duplicates" when analyze: duplicates = true; break;
            case "--min-mb" when analyze && i + 1 < args.Length:
                if (!long.TryParse(args[++i], out minimumMb) || minimumMb is < 0 or > 1_048_576) throw new ArgumentException("Minimum size must be 0–1048576 MB.");
                break;
            default: throw new ArgumentException($"Unknown or incomplete option: {args[i]}");
        }
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    object report; bool cancelled;
    if (analyze)
    {
        var result = new DiskAnalyzer().Analyze(folder!, duplicates, minimumMb * 1024 * 1024, cancellation: cancellation.Token);
        report = result; cancelled = result.Cancelled;
        if (!json)
        {
            Console.WriteLine($"READ-ONLY ANALYSIS | {result.FilesExamined:N0} files examined\n");
            foreach (var file in result.LargeFiles) Console.WriteLine($"{file.SizeLabel,12}  {file.Path}");
            foreach (var group in result.Duplicates) Console.WriteLine($"\nIdentical content ({group.Paths.Length} files; {group.SizeLabel} redundant logical size):\n{group.PathList}");
            Console.WriteLine($"\n{result.Issues.Length} issues | Limited: {result.Limited} | Cancelled: {result.Cancelled}");
            Console.WriteLine("Duplicate findings are advisory. No files are deleted or selected for cleanup.");
        }
    }
    else
    {
        var result = new CacheScanner().Scan(cancellation: cancellation.Token, options: new(age, exclusions.ToArray()));
        report = result; cancelled = result.Cancelled;
        if (!json)
        {
            Console.WriteLine("READ-ONLY SCAN | No files changed\n");
            foreach (var row in result.Rules) Console.WriteLine($"{row.Rule.Title,-32} {row.Files.Length,7} files {ByteSize.Format(row.Bytes),12}  {row.Status}");
            Console.WriteLine($"\n{result.FileCount:N0} candidates | {ByteSize.Format(result.CandidateBytes)} logical file size | {result.Issues.Length:N0} exclusions");
            Console.WriteLine("Candidate bytes are estimates. Recycled files retain disk space until the Recycle Bin is emptied.");
            if (result.Cancelled) Console.WriteLine("Scan cancelled. Results are partial.");
        }
    }
    var serialized = JsonSerializer.Serialize(report, report.GetType(), ReportJson.Options);
    if (output is not null)
    {
        using var writer = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write));
        writer.Write(serialized); Console.Error.WriteLine($"Report saved to {output}");
    }
    if (json) Console.WriteLine(serialized);
    return cancelled ? 130 : 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
{ Console.Error.WriteLine(ex.Message); return 1; }
