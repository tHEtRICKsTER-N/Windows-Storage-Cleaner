using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using Cleaner.Core;

internal static class Program
{
    private static string testRoot = "";
    private static int failures;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("Pass a workspace directory for test fixtures."); return 1; }
        testRoot = Path.Combine(Path.GetFullPath(args[0]), $"cleaner-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        Test("Bundled rule catalog validates", () => Assert(RuleCatalog.Load().Length == 21));
        Test("Minimum age preferences only narrow a scan", () =>
        {
            var root = Fixture(); OldFile(root);
            Assert(new CacheScanner(root, [Rule()]).Scan(options: new(30)).FileCount == 0);
            Assert(new CacheScanner(root, [Rule()]).Scan(options: new(0)).FileCount == 1);
            Throws<ArgumentException>(() => new CacheScanner(root, [Rule()]).Scan(options: new(-1)));
        });
        Test("Folder exclusions apply to scans and cleanup", () =>
        {
            var root = Fixture(); var path = OldFile(root); var scan = Scan(root); var options = new ScanOptions(0, [Path.Combine(root, "Temp")]);
            var excluded = new CacheScanner(root, [Rule()]).Scan(options: options);
            Assert(excluded.FileCount == 0 && excluded.Rules[0].Status == "Excluded by you");
            var fake = new FakeRecycler(); var report = new CleanupEngine(root, [Rule()], fake, options).Recycle(scan.Rules[0].Files, Log(root));
            Assert(fake.Calls == 0 && report.Entries[0].Detail.Contains("excluded") && File.Exists(path));
        });
        Test("Browser cache rules leave credentials and history outside their roots", () =>
        {
            var root = Fixture(); var rules = RuleCatalog.Load().Where(x => x.Id == "chrome-cache").ToArray();
            string profile = Path.Combine(root, "Google", "Chrome", "User Data", "Default"); string cache = Path.Combine(profile, "Cache", "Cache_Data");
            Directory.CreateDirectory(cache); string file = Path.Combine(cache, "cache-data"); File.WriteAllText(file, "cache");
            File.SetCreationTimeUtc(file, DateTime.UtcNow.AddDays(-20)); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-20));
            foreach (string name in new[] { "Login Data", "Cookies", "History", "Bookmarks" }) File.WriteAllText(Path.Combine(profile, name), "private");
            var report = new CacheScanner(root, rules).Scan(); Assert(report.FileCount == 1 && report.Rules[0].Files[0].File.Path == file);
            Assert(File.ReadAllText(Path.Combine(profile, "Login Data")) == "private");
        });
        Test("Disk analysis finds identical content without changing files", () =>
        {
            var root = Fixture(); var original = OldFile(root, "a.tmp"); OldFile(root, "b.tmp");
            string different = OldFile(root, "c.tmp"); File.WriteAllText(different, "different!!!");
            var result = new DiskAnalyzer().Analyze(Path.Combine(root, "Temp"), true, minimumBytes: 1);
            Assert(result.LargeFiles.Length == 3 && result.Duplicates.Length == 1 && result.Duplicates[0].Paths.Length == 2);
            Assert(File.ReadAllText(original) == "fixture data" && result.Duplicates[0].RedundantBytes == 12);
        });
        Test("Storage analysis skips linked folders and supports cancellation", () =>
        {
            var root = Fixture(); var outside = Fixture(); OldFile(outside); Junction(Path.Combine(root, "Temp", "linked"), Path.Combine(outside, "Temp"));
            Assert(new DiskAnalyzer().Analyze(Path.Combine(root, "Temp"), minimumBytes: 1).LargeFiles.Length == 0);
            using var token = new CancellationTokenSource(); token.Cancel();
            Assert(new DiskAnalyzer().Analyze(root, cancellation: token.Token).Cancelled);
        });
        Test("Maintenance requests have a fixed command whitelist", () =>
        {
            foreach (var operation in MaintenanceCatalog.All)
            {
                Assert(operation.Commands.Length > 0 && !string.IsNullOrWhiteSpace(operation.Impact));
                foreach (var command in operation.Commands)
                {
                    Assert(Path.IsPathFullyQualified(MaintenanceCatalog.ExecutablePath(command)));
                    Assert(command.Arguments.All(x => !x.Contains("ResetBase", StringComparison.OrdinalIgnoreCase)));
                }
            }
            Throws<ArgumentException>(() => MaintenanceCatalog.Get("clean-components & arbitrary.exe"));
            Throws<ArgumentException>(() => MaintenanceCatalog.ExecutablePath(new("cmd.exe", [])));
            Assert(!MaintenanceCatalog.Get("analyze-components").ChangesSystem);
        });
        Test("History reads completed, interrupted and malformed journals", () =>
        {
            var root = Fixture(); OldFile(root); var scan = Scan(root);
            Engine(root, new FakeRecycler()).Recycle(scan.Rules[0].Files, Path.Combine(root, "recycle-complete.jsonl"));
            File.WriteAllText(Path.Combine(root, "recycle-partial.jsonl"), "{\"kind\":\"start\"}");
            File.WriteAllText(Path.Combine(root, "recycle-invalid.jsonl"), "{\"kind\":\"finish\",\"report\":null}");
            var history = Cleaner.App.HistoryEntry.Load(root);
            Assert(history.Length == 3 && history.Any(x => x.RecycledFiles == 1 && x.RecycledBytes == 12));
            Assert(history.Any(x => x.Status.StartsWith("Interrupted")) && history.Any(x => x.Status == "Journal needs review"));
        });
        Test("Rules reject escaping, absolute, and malformed roots", () =>
        {
            foreach (var path in new[] { "..\\Temp", "C:\\Windows", "Temp:stream", "Temp\\..", "Temp ", "%TEMP%", "" })
                Throws<ArgumentException>(() => RuleCatalog.Validate([Rule() with { RelativePath = path }]));
            Throws<ArgumentException>(() => RuleCatalog.Validate([Rule(), Rule()]));
            Throws<ArgumentException>(() => RuleCatalog.Validate([Rule() with { MinimumAgeDays = 0 }]));
            Throws<ArgumentException>(() => RuleCatalog.Validate([Rule() with { Patterns = ["..\\*"] }]));
        });
        Test("Read-only scan finds only old matching files", () =>
        {
            var root = Fixture(); var old = OldFile(root); var fresh = OldFile(root, "fresh.tmp");
            File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
            var recentCreation = OldFile(root, "recent-create.tmp"); File.SetCreationTimeUtc(recentCreation, DateTime.UtcNow);
            OldFile(root, "notes.docx"); OldFile(root, "disk.vhdx");
            var scan = Scan(root);
            Assert(scan.FileCount == 1 && scan.Rules[0].Files[0].File.Path == old);
            Assert(File.ReadAllText(old) == "fixture data" && File.Exists(fresh));
            Assert(scan.CandidateBytes == new FileInfo(old).Length);
        });
        Test("Read-only and locked files are excluded", () =>
        {
            var root = Fixture(); var readOnly = OldFile(root, "read-only.tmp");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            var locked = OldFile(root, "locked.tmp");
            using var stream = File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var scan = Scan(root); Assert(scan.FileCount == 0 && scan.Issues.Length == 2);
        });
        Test("Hard links are excluded", () =>
        {
            var root = Fixture(); var source = OldFile(root);
            Assert(CreateHardLink(Path.Combine(root, "Temp", "linked.tmp"), source, IntPtr.Zero));
            Assert(Scan(root).FileCount == 0);
        });
        Test("Scanner does not traverse junctions", () =>
        {
            var root = Fixture(); var target = Fixture(); var outside = OldFile(target);
            Junction(Path.Combine(root, "Temp", "redirect"), Path.Combine(target, "Temp"));
            var scan = Scan(root);
            Assert(scan.FileCount == 0 && scan.Issues.Any(x => x.Reason.Contains("Linked")) && File.Exists(outside));
        });
        Test("Cache root replaced with a junction after scanning is rejected", () =>
        {
            var root = Fixture(); OldFile(root); var scan = Scan(root); var target = Fixture(); var outside = OldFile(target);
            Directory.Move(Path.Combine(root, "Temp"), Path.Combine(root, "OriginalTemp"));
            Junction(Path.Combine(root, "Temp"), Path.Combine(target, "Temp"));
            var fake = new FakeRecycler(); var result = Engine(root, fake).Recycle(scan.Rules[0].Files, Log(root));
            Assert(fake.Calls == 0 && result.Entries.Single().Detail.Contains("Linked") && File.Exists(outside));
        });
        Test("Sibling prefix escape is rejected", () =>
        {
            var root = Fixture(); var safe = new PathSafety(root, [Rule()]);
            Throws<IOException>(() => safe.Check(Path.Combine(root, "TempElsewhere", "a.tmp"), safe.RootFor(Rule()), true));
        });
        Test("Changed candidates are skipped before the recycler", () =>
        {
            var root = Fixture(); var path = OldFile(root); var scan = Scan(root); File.AppendAllText(path, " changed");
            var fake = new FakeRecycler(); var result = Engine(root, fake).Recycle(scan.Rules[0].Files, Log(root));
            Assert(fake.Calls == 0 && result.Entries.Single().Status == "Skipped" && File.Exists(path) &&
                result.Entries.Single().Detail.Contains("changed", StringComparison.OrdinalIgnoreCase));
        });
        Test("Replaced file with identical metadata is rejected by identity", () =>
        {
            var root = Fixture(); var path = OldFile(root); var scan = Scan(root); var snapshot = scan.Rules[0].Files[0].File;
            File.Move(path, path + ".original");
            File.WriteAllText(path, "fixture data"); File.SetCreationTimeUtc(path, snapshot.CreationUtc); File.SetLastWriteTimeUtc(path, snapshot.LastWriteUtc);
            var fake = new FakeRecycler(); var result = Engine(root, fake).Recycle(scan.Rules[0].Files, Log(root));
            Assert(fake.Calls == 0 && result.Entries.Single().Status == "Skipped" &&
                result.Entries.Single().Detail.Contains("changed", StringComparison.OrdinalIgnoreCase));
        });
        Test("Unknown rule and forged out-of-root candidate are blocked", () =>
        {
            var root = Fixture(); OldFile(root); var candidate = Scan(root).Rules[0].Files.Single(); var fake = new FakeRecycler();
            var outside = Path.Combine(root, "outside.tmp"); File.Copy(candidate.File.Path, outside);
            var result = Engine(root, fake).Recycle([candidate with { RuleId = "unknown" }, candidate with { File = candidate.File with { Path = outside } }], Log(root));
            Assert(fake.Calls == 0 && result.Entries.All(x => x.Status == "Skipped") && File.Exists(outside));
        });
        Test("Missing journal directory prevents any recycling", () =>
        {
            var root = Fixture(); OldFile(root); var fake = new FakeRecycler();
            Throws<DirectoryNotFoundException>(() => Engine(root, fake).Recycle(Scan(root).Rules[0].Files, Path.Combine(root, "missing", "journal.jsonl")));
            Assert(fake.Calls == 0);
        });
        Test("Duplicate selection is processed once and journal is readable", () =>
        {
            var root = Fixture(); OldFile(root); var candidate = Scan(root).Rules[0].Files.Single(); var fake = new FakeRecycler(); var log = Log(root);
            var result = Engine(root, fake).Recycle([candidate, candidate], log);
            Assert(fake.Calls == 1 && result.RecycledFiles == 1 && result.SpaceReclaimedBytes == 0);
            Assert(File.ReadLines(log).Select(line => JsonDocument.Parse(line)).Count() == 4);
        });
        Test("Cancellation stops before an operation and returns partial scan", () =>
        {
            var root = Fixture(); OldFile(root); var scan = Scan(root); using var token = new CancellationTokenSource(); token.Cancel();
            var fake = new FakeRecycler(); var result = Engine(root, fake).Recycle(scan.Rules[0].Files, Log(root), cancellation: token.Token);
            Assert(result.Cancelled && fake.Calls == 0 && new CacheScanner(root, [Rule()]).Scan(cancellation: token.Token).Cancelled);
        });
        Test("Pre-delete guard vetoes permanent deletion and validation failure", () =>
        {
            int calls = 0;
            var sink = new ShellRecycler.RecycleSink(() => calls++);
            Assert(sink.PreDeleteItem(0, IntPtr.Zero) < 0 && calls == 0);
            var failing = new ShellRecycler.RecycleSink(() => throw new IOException("changed"));
            Assert(failing.PreDeleteItem(0x80, IntPtr.Zero) < 0 && failing.Failure == "changed");
            var valid = new ShellRecycler.RecycleSink(() => calls++);
            Assert(valid.PreDeleteItem(0x80, IntPtr.Zero) == 0 && calls == 1);
            var cancelled = new ShellRecycler.RecycleSink(() => throw new OperationCanceledException());
            Assert(cancelled.PreDeleteItem(0x80, IntPtr.Zero) < 0 && cancelled.Cancelled);
        });
        Test("Directory replacement is prevented while recycling", () =>
        {
            var root = Fixture(); OldFile(root); var candidate = Scan(root).Rules[0].Files.Single(); bool blocked = false;
            var fake = new FakeRecycler(() =>
            {
                try { Directory.Move(Path.Combine(root, "Temp"), Path.Combine(root, "MovedTemp")); }
                catch (IOException) { blocked = true; }
            });
            Engine(root, fake).Recycle([candidate], Log(root)); Assert(blocked && fake.Calls == 1);
        });
        if (args.Contains("--shell"))
        {
            Test("Windows Shell recycles one workspace fixture without permanent fallback", () =>
            {
                var root = Fixture(); var path = OldFile(root); var scan = Scan(root);
                var result = new CleanupEngine(root, [Rule()]).Recycle(scan.Rules[0].Files, Log(root));
                Assert(result.RecycledFiles == 1 && !File.Exists(path) && result.SpaceReclaimedBytes == 0,
                    JsonSerializer.Serialize(result, ReportJson.Options));
            });
        }
        Console.WriteLine($"\n{(failures == 0 ? "PASS" : "FAIL")} · {failures} failure(s) · fixtures: {testRoot}");
        return failures == 0 ? 0 : 1;
    }
    private static CleaningRule Rule() => new("fixture", 1, "Fixture cache", "Temp", ["*.tmp"], 7, true, "Review", "Test fixtures only.");
    private static string Fixture() { var path = Path.Combine(testRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(path, "Temp")); return path; }
    private static string OldFile(string root, string name = "old.tmp")
    {
        string path = Path.Combine(root, "Temp", name); File.WriteAllText(path, "fixture data");
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-20)); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-20)); return path;
    }
    private static ScanReport Scan(string root) => new CacheScanner(root, [Rule()]).Scan();
    private static CleanupEngine Engine(string root, IRecycler fake) => new(root, [Rule()], fake);
    private static string Log(string root) => Path.Combine(root, $"journal-{Guid.NewGuid():N}.jsonl");
    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = Process.Start(start) ?? throw new IOException("Could not create test junction.");
        process.WaitForExit(); Assert(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static void Test(string name, Action test) { try { test(); Console.WriteLine($"PASS {name}"); } catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); } }
    private sealed class FakeRecycler(Action? before = null) : IRecycler
    {
        public int Calls { get; private set; }
        public void Recycle(string path, Action validateAgain) { before?.Invoke(); validateAgain(); Calls++; }
    }
    [DllImport("kernel32.dll", EntryPoint="CreateHardLinkW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);
}
