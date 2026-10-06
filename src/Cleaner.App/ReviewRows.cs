using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cleaner.Core;

namespace Cleaner.App;

public abstract class NotifyRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

public sealed class FileRow(Candidate candidate) : NotifyRow
{
    private bool selected;
    private bool available = true;
    public Candidate Candidate { get; } = candidate;
    public string Path => Candidate.File.Path;
    public string SizeLabel => ByteSize.Format(Candidate.File.Bytes);
    public string ModifiedLabel => Candidate.File.LastWriteUtc.ToLocalTime().ToString("dd MMM yyyy");
    public bool CanSelect { get => available; set { available = value; if (!value) IsSelected = false; Changed(); } }
    public bool IsSelected { get => selected; set { if (selected == value) return; selected = value && CanSelect; Changed(); } }
}

public sealed class RuleRow : NotifyRow
{
    private bool changingSelection;
    public RuleScan Scan { get; }
    public FileRow[] Files { get; }
    public string Title => Scan.Rule.Title;
    public string Risk => Scan.Rule.Risk;
    public string Category => Scan.Rule.Category;
    public string Status => Scan.Status;
    public string CountLabel => Scan.Status == "Ready" ? "—" : Files.Length.ToString("N0");
    public string SizeLabel => Scan.Status == "Ready" ? "—" : ByteSize.Format(Scan.Bytes);
    public bool CanSelect => Files.Any(x => x.CanSelect);
    public bool? IsSelected
    {
        get
        {
            var available = Files.Where(x => x.CanSelect).ToArray();
            if (available.Length == 0 || available.All(x => !x.IsSelected)) return false;
            return available.All(x => x.IsSelected) ? true : null;
        }
        set
        {
            changingSelection = true;
            try { foreach (var file in Files) file.IsSelected = value ?? true; }
            finally { changingSelection = false; }
            Changed();
        }
    }
    public RuleRow(RuleScan scan)
    {
        Scan = scan;
        Files = scan.Files.Select(x => new FileRow(x)).ToArray();
        foreach (var file in Files) file.PropertyChanged += (_, _) =>
        {
            if (!changingSelection) { Changed(nameof(IsSelected)); Changed(nameof(CanSelect)); }
        };
    }
}
