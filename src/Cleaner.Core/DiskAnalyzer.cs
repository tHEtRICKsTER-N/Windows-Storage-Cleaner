using System.Security.Cryptography;

namespace Cleaner.Core;

public sealed record LargeFile(string Path, long Bytes, DateTime ModifiedUtc)
{
    public string SizeLabel => ByteSize.Format(Bytes);
    public string ModifiedLabel => ModifiedUtc.ToLocalTime().ToString("dd MMM yyyy");
}
public sealed record DuplicateSet(string Hash, long BytesPerFile, string[] Paths)
{
    public long RedundantBytes => BytesPerFile * (Paths.Length - 1);
    public string SizeLabel => ByteSize.Format(RedundantBytes);
    public string PathList => string.Join(Environment.NewLine, Paths);
}
public sealed record DiskAnalysisReport(string Root, bool Cancelled, bool Limited, int FilesExamined,
    LargeFile[] LargeFiles, DuplicateSet[] Duplicates, string[] Issues);

public sealed class DiskAnalyzer
{
    public DiskAnalysisReport Analyze(string root, bool findDuplicates = false, long minimumBytes = 50 * 1024 * 1024,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (minimumBytes < 0) throw new ArgumentException("Minimum size cannot be negative.");
        root = Path.GetFullPath(root);
        if (root.StartsWith(@"\\") || !Directory.Exists(root)) throw new ArgumentException("Choose an existing local folder.");
        var files = new List<LargeFile>(); var issues = new List<string>(); var duplicates = new List<DuplicateSet>();
        int examined = 0; bool cancelled = false, limited = false;
        var queue = new Stack<string>(); queue.Push(root);
        try
        {
            while (queue.TryPop(out string? folder))
            {
                cancellation.ThrowIfCancellationRequested();
                if (examined >= 200_000) { limited = true; break; }
                try
                {
                    CheckAncestors(folder);
                    foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try
                        {
                            var attributes = File.GetAttributes(path);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.System)) != 0) continue;
                            if ((attributes & FileAttributes.Directory) != 0) { queue.Push(path); continue; }
                            examined++;
                            if (examined % 200 == 0) progress?.Report($"{examined:N0} files examined · {folder}");
                            if (examined >= 200_000) { limited = true; queue.Clear(); break; }
                            var info = new FileInfo(path);
                            if (info.Length >= minimumBytes && files.Count < 10_000) files.Add(new(path, info.Length, info.LastWriteTimeUtc));
                            else if (files.Count >= 10_000) limited = true;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Issue(ex.Message); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Issue(ex.Message); }
            }
            if (findDuplicates)
            {
                long hashedBytes = 0;
                foreach (var group in files.GroupBy(x => x.Bytes).Where(x => x.Count() > 1))
                {
                    var hashes = new Dictionary<string, List<string>>();
                    foreach (var file in group)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (file.Bytes > 512L * 1024 * 1024 || hashedBytes + file.Bytes > 2L * 1024 * 1024 * 1024) { limited = true; continue; }
                        try
                        {
                            CheckAncestors(file.Path);
                            var identity = FileIdentity.Read(file.Path);
                            if (identity.Bytes != file.Bytes || identity.LastWriteUtc != file.ModifiedUtc) throw new IOException("File changed during analysis.");
                            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            byte[] buffer = new byte[128 * 1024]; int length;
                            progress?.Report($"Checking duplicate content · {Path.GetFileName(file.Path)}");
                            while ((length = stream.Read(buffer)) > 0) { cancellation.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, length); }
                            if (FileIdentity.Read(file.Path) != identity) throw new IOException("File changed during hashing.");
                            string key = Convert.ToHexString(hash.GetHashAndReset());
                            if (!hashes.TryGetValue(key, out var paths)) hashes[key] = paths = [];
                            paths.Add(file.Path); hashedBytes += file.Bytes;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Issue(ex.Message); }
                    }
                    duplicates.AddRange(hashes.Where(x => x.Value.Count > 1).Select(x => new DuplicateSet(x.Key, group.Key, x.Value.ToArray())));
                }
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        return new(root, cancelled, limited, examined, files.OrderByDescending(x => x.Bytes).Take(500).ToArray(),
            duplicates.OrderByDescending(x => x.RedundantBytes).ToArray(), issues.ToArray());
        void Issue(string text) { if (issues.Count < 1000) issues.Add(text); }
    }
    private static void CheckAncestors(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked paths are excluded.");
    }
}
