using System.IO;
using System.Text.Json;
using Cleaner.Core;

namespace Cleaner.App;

public sealed record HistoryEntry(DateTime DateUtc, string Status, int RecycledFiles, long RecycledBytes, string JournalPath)
{
    public string DateLabel => DateUtc.ToLocalTime().ToString("dd MMM yyyy · HH:mm");
    public string SizeLabel => ByteSize.Format(RecycledBytes);
    public static HistoryEntry[] Load(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var entries = new List<HistoryEntry>();
        foreach (var file in Directory.EnumerateFiles(folder, "recycle-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(50))
        {
            try
            {
                if (new FileInfo(file).Length > 20 * 1024 * 1024) { entries.Add(new(File.GetLastWriteTimeUtc(file), "Large journal · open to review", 0, 0, file)); continue; }
                using var last = JsonDocument.Parse(File.ReadLines(file).Last());
                var data = last.RootElement;
                if (data.GetProperty("kind").GetString() != "finish") { entries.Add(new(File.GetLastWriteTimeUtc(file), "Interrupted · check the Recycle Bin", 0, 0, file)); continue; }
                var report = JsonSerializer.Deserialize<CleanupReport>(data.GetProperty("report"), ReportJson.Options)!;
                if (report?.Entries is null) throw new JsonException("Journal report has no entries.");
                int skipped = report.Entries.Count(x => x.Status == "Skipped");
                entries.Add(new(report.CompletedUtc, report.Cancelled ? "Stopped" : $"Complete · {skipped} skipped", report.RecycledFiles, report.RecycledBytes, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
            { entries.Add(new(File.GetLastWriteTimeUtc(file), "Journal needs review", 0, 0, file)); }
        }
        return entries.ToArray();
    }
}
