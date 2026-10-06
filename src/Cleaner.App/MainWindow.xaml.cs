using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Cleaner.Core;
using Microsoft.Win32;

namespace Cleaner.App;

public partial class MainWindow : Window
{
    public ObservableCollection<RuleRow> Rows { get; } = [];
    private readonly ObservableCollection<string> exclusions = [];
    private readonly bool preview;
    private bool initializing = true, busy, updatingRows;
    private UserPreferences preferences;
    private ScanReport? report;
    private DiskAnalysisReport? analysis;
    private CancellationTokenSource? cancellation;
    private static readonly int[] Ages = [0, 14, 30, 90];

    public MainWindow(bool diagnosticMode = false)
    {
        preview = diagnosticMode;
        preferences = preview ? new("Paper") : PreferencesStore.Load();
        InitializeComponent(); DataContext = this;
        ThemeCombo.ItemsSource = ThemeManager.Names; ThemeCombo.SelectedItem = preferences.Theme;
        ThemeManager.Apply(preferences.Theme);
        CategoryCombo.ItemsSource = new[] { "All categories", "System caches", "Graphics", "Browsers", "Development", "Diagnostics" }; CategoryCombo.SelectedIndex = 0;
        AgeCombo.ItemsSource = new[] { "Rule defaults", "14+ days", "30+ days", "90+ days" }; AgeCombo.SelectedIndex = Array.IndexOf(Ages, preferences.MinimumAgeDays);
        SizeCombo.ItemsSource = new[] { "1+ MB", "50+ MB", "100+ MB", "500+ MB" }; SizeCombo.SelectedIndex = 1;
        foreach (string path in preferences.ExcludedPaths ?? []) exclusions.Add(path);
        ExclusionList.ItemsSource = exclusions;
        MaintenanceItems.ItemsSource = MaintenanceCatalog.All;
        MaintenanceItems.IsEnabled = !preview;
        foreach (var rule in RuleCatalog.Load()) Rows.Add(new(new(rule, "", "Ready", [])));
        CollectionViewSource.GetDefaultView(Rows).Filter = x => Matches((RuleRow)x);
        initializing = false;
        ShowPage(0);
    }
    private bool Matches(RuleRow row) =>
        (CategoryCombo.SelectedIndex == 0 || row.Category == CategoryCombo.SelectedItem?.ToString()) &&
        (string.IsNullOrWhiteSpace(SearchBox.Text) || row.Title.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
    private void Filter_Changed(object sender, TextChangedEventArgs e) => FilterRows();
    private void Category_Changed(object sender, SelectionChangedEventArgs e) => FilterRows();
    private void FilterRows()
    {
        if (initializing) return;
        CollectionViewSource.GetDefaultView(Rows).Refresh();
        if (RuleGrid.SelectedItem is not RuleRow selected || !Matches(selected)) RuleGrid.SelectedItem = Rows.FirstOrDefault(Matches);
    }
    private void FileFilter_Changed(object sender, TextChangedEventArgs e) => RefreshFiles();
    private void RefreshFiles()
    {
        if (initializing) return;
        if (RuleGrid.SelectedItem is not RuleRow row) { FileGrid.ItemsSource = null; FilesShownText.Text = ""; return; }
        var visible = row.Files.Where(x => string.IsNullOrWhiteSpace(FileSearchBox.Text) || x.Path.Contains(FileSearchBox.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        FileGrid.ItemsSource = visible; FilesShownText.Text = $"{visible.Length:N0} of {row.Files.Length:N0} files · selection spans all categories";
    }
    private ScanOptions Options() => new(preferences.MinimumAgeDays, exclusions.ToArray());
    private void Age_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || AgeCombo.SelectedIndex < 0) return;
        preferences = preferences with { MinimumAgeDays = Ages[AgeCombo.SelectedIndex] }; SavePreferences(); InvalidateSelection();
    }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || ThemeCombo.SelectedItem is not string theme) return;
        ThemeManager.Apply(theme); preferences = preferences with { Theme = theme }; SavePreferences();
    }
    private void SavePreferences()
    {
        if (preview) return;
        try { PreferencesStore.Save(preferences with { ExcludedPaths = exclusions.ToArray() }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = $"Preferences could not be saved: {ex.Message}"; }
    }
    private void InvalidateSelection()
    {
        if (report is null) return;
        DisableSelection(); StatusText.Text = "Review settings changed. Scan again before recycling.";
    }
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview) return;
        SetBusy(true); report = null; Rows.Clear(); FileGrid.ItemsSource = null;
        CandidateBytesText.Text = FileCountText.Text = "—"; RunDetailText.Text = "";
        DetailsTitle.Text = "Scanning cache locations"; DetailsExplanation.Text = "Linked, protected, locked, recent and excluded files are skipped.";
        cancellation = new(); var options = Options();
        var progress = new Progress<ScanProgress>(p => StatusText.Text = $"Scanning {p.Title} · {p.FilesExamined:N0} files examined");
        try
        {
            var scan = await Task.Run(() => new CacheScanner().Scan(progress, cancellation.Token, options));
            LoadReport(scan);
            StatusText.Text = scan.Cancelled ? "Stopped. Results are partial; scan again to enable recycling." : $"Scan complete · {scan.FileCount:N0} candidates. Select individual files or cache categories.";
        }
        catch (Exception ex) { StatusText.Text = $"Scan failed: {ex.Message}"; }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); }
    }
    private void LoadReport(ScanReport scan)
    {
        report = scan; Rows.Clear();
        foreach (var result in scan.Rules) { var row = new RuleRow(result); row.PropertyChanged += (_, _) => UpdateSelection(); Rows.Add(row); }
        CandidateBytesText.Text = ByteSize.Format(scan.CandidateBytes); FileCountText.Text = scan.FileCount.ToString("N0");
        ExclusionText.Text = $"{scan.Issues.Length:N0} exclusions · {scan.Rules.Length} locations";
        FilterRows(); RuleGrid.SelectedItem = Rows.FirstOrDefault(x => Matches(x) && x.Files.Length > 0) ?? Rows.FirstOrDefault(Matches);
        ExportButton.IsEnabled = !busy; UpdateSelection();
    }
    private void RuleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        if (RuleGrid.SelectedItem is RuleRow row) { DetailsTitle.Text = row.Title; DetailsExplanation.Text = row.Scan.Rule.Explanation; }
        RefreshFiles();
    }
    private FileRow[] Selected() => Rows.SelectMany(x => x.Files).Where(x => x.IsSelected && x.CanSelect).ToArray();
    private void UpdateSelection()
    {
        if (updatingRows || initializing) return;
        var selected = Selected(); SelectedBytesText.Text = ByteSize.Format(selected.Sum(x => x.Candidate.File.Bytes)); SelectedCountText.Text = $"{selected.Length:N0} files selected";
        RecycleButton.IsEnabled = !busy && !preview && report is { Cancelled: false } && selected.Length > 0;
    }
    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        updatingRows = true; foreach (var row in Rows) row.IsSelected = false; updatingRows = false; UpdateSelection();
    }
    private async void Recycle_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview || report is null || report.Cancelled) return;
        var selected = Selected(); if (selected.Length == 0) return;
        string categories = string.Join(", ", Rows.Where(x => x.Files.Any(f => f.IsSelected)).Select(x => x.Title));
        if (MessageBox.Show(this, $"Recycle {selected.Length:N0} selected files ({ByteSize.Format(selected.Sum(x => x.Candidate.File.Bytes))})?\n\nCategories: {categories}\n\nSelection includes files hidden by filters. Cache content may need downloading or rebuilding again. Temporary files and error reports may be useful.\n\nChanged, locked, excluded and non-recyclable files are skipped. Recycling retains disk space until the bin is emptied.",
            "Review your selection", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SetBusy(true); cancellation = new(); string? journal = null;
        try
        {
            Directory.CreateDirectory(PreferencesStore.LogsFolder); journal = Path.Combine(PreferencesStore.LogsFolder, $"recycle-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
            var progress = new Progress<string>(path => StatusText.Text = $"Recycling {Path.GetFileName(path)}");
            var cleanup = await RunSta(() => new CleanupEngine(options: Options()).Recycle(selected.Select(x => x.Candidate).ToArray(), journal, progress, cancellation.Token));
            DisableSelection(); StatusText.Text = $"{(cleanup.Cancelled ? "Stopped" : "Finished")} · {cleanup.RecycledFiles:N0} recycled · {cleanup.Entries.Count(x => x.Status == "Skipped"):N0} skipped · 0 B reclaimed by recycling";
            RunDetailText.Text = "Restore files in the Windows Recycle Bin. Activity history contains the journal. Scan again to refresh results.";
        }
        catch (Exception ex) { DisableSelection(); StatusText.Text = $"Cleanup stopped: {ex.Message}. Scan again before retrying."; RunDetailText.Text = journal is null ? "" : $"Journal: {journal}"; }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); }
    }
    private void DisableSelection()
    {
        updatingRows = true; try { foreach (var file in Rows.SelectMany(x => x.Files)) file.CanSelect = false; } finally { updatingRows = false; } UpdateSelection();
    }
    private static Task<T> RunSta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private void SetBusy(bool value)
    {
        busy = value; ScanButton.IsEnabled = AnalyzeButton.IsEnabled = !value && !preview;
        ExportButton.IsEnabled = !value && report is not null; AnalysisExportButton.IsEnabled = !value && analysis is not null;
        RuleGrid.IsEnabled = FileGrid.IsEnabled = AgeCombo.IsEnabled = SettingsNav.IsEnabled = !value;
        Activity.Visibility = CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); StatusText.Text = "Stopping after the current operation…"; }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (busy) { cancellation?.Cancel(); e.Cancel = true; StatusText.Text = "Stopping safely. Close the window when the operation finishes."; } }
    private void Export_Click(object sender, RoutedEventArgs e) { if (report is not null) SaveJson(report, "cache-scan"); }
    private void AnalysisExport_Click(object sender, RoutedEventArgs e) { if (analysis is not null) SaveJson(analysis, "storage-analysis"); }
    private void SaveJson<T>(T value, string name)
    {
        if (preview) return;
        var dialog = new SaveFileDialog { Filter = "JSON report (*.json)|*.json", FileName = $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(value, ReportJson.Options)); StatusText.Text = "Report exported. It includes local file paths; review before sharing."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "Could not save report"); }
    }
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (preview || busy) return;
        var dialog = new OpenFolderDialog { Title = "Choose a local folder to analyze" }; if (dialog.ShowDialog(this) == true) AnalysisPathBox.Text = dialog.FolderName;
    }
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview) return;
        if (string.IsNullOrWhiteSpace(AnalysisPathBox.Text)) { StatusText.Text = "Choose a folder first."; return; }
        SetBusy(true); cancellation = new(); string root = AnalysisPathBox.Text;
        long bytes = new long[] { 1, 50, 100, 500 }[Math.Max(SizeCombo.SelectedIndex, 0)] * 1024 * 1024;
        bool duplicates = DuplicateCheck.IsChecked == true;
        var progress = new Progress<string>(text => StatusText.Text = text);
        try
        {
            analysis = await Task.Run(() => new DiskAnalyzer().Analyze(root, duplicates, bytes, progress, cancellation.Token));
            LargeGrid.ItemsSource = analysis.LargeFiles; DuplicateGrid.ItemsSource = analysis.Duplicates;
            AnalysisSummary.Text = $"{analysis.FilesExamined:N0} files · {analysis.LargeFiles.Length:N0} large files · {analysis.Duplicates.Length:N0} duplicate groups";
            StatusText.Text = analysis.Cancelled ? "Analysis stopped. Results are partial." : analysis.Limited ? "Analysis complete with limits. Results may be partial; choose a smaller folder or narrower size threshold." : "Analysis complete. No files were changed.";
            RunDetailText.Text = $"{analysis.Issues.Length:N0} exclusions. Results are advisory; identical content does not mean a copy is unnecessary.";
        }
        catch (Exception ex) { StatusText.Text = $"Analysis failed: {ex.Message}"; }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); }
    }
    private void Maintenance_Click(object sender, RoutedEventArgs e)
    {
        if (preview || busy || sender is not Button { Tag: string id }) return;
        _ = MaintenanceCatalog.Get(id);
        string helper = Path.Combine(AppContext.BaseDirectory, "WindowsStorageCleaner.Maintenance.exe");
        if (!File.Exists(helper)) { MessageBox.Show(this, "The maintenance helper is missing. Use the setup or portable release built by scripts/release.ps1.", "Maintenance helper unavailable"); return; }
        try { Process.Start(new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", Arguments = id, WorkingDirectory = Path.GetDirectoryName(helper)! }); }
        catch (Win32Exception ex) { StatusText.Text = ex.NativeErrorCode == 1223 ? "Administrator permission was cancelled. No maintenance started." : ex.Message; }
    }
    private void WindowsControl_Click(object sender, RoutedEventArgs e)
    {
        if (preview || sender is not Button { Tag: string tag }) return;
        string target = tag switch { "storage" => "ms-settings:storagesense", "disk-cleanup" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cleanmgr.exe"), "restore" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SystemPropertiesProtection.exe"), _ => throw new ArgumentException("Unknown Windows control.") };
        Open(target);
    }
    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => LoadHistory();
    private void LoadHistory()
    {
        if (preview) return;
        try { var runs = HistoryEntry.Load(PreferencesStore.LogsFolder); HistoryGrid.ItemsSource = runs; StatusText.Text = runs.Length == 0 ? "No cleanup history yet. A journal is created when you recycle files." : $"{runs.Length} recent cleanup journals."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = ex.Message; }
    }
    private void OpenJournal_Click(object sender, RoutedEventArgs e)
    {
        if (preview || HistoryGrid.SelectedItem is not HistoryEntry entry) return;
        Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"), entry.JournalPath);
    }
    private void OpenRecycleBin_Click(object sender, RoutedEventArgs e) { if (!preview) Open("explorer.exe", "shell:RecycleBinFolder"); }
    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (preview || busy) return;
        var dialog = new OpenFolderDialog { Title = "Choose a folder to exclude" };
        if (dialog.ShowDialog(this) != true || exclusions.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase)) return;
        if (exclusions.Count >= 100) { StatusText.Text = "The maximum is 100 excluded folders."; return; }
        exclusions.Add(Path.GetFullPath(dialog.FolderName)); SavePreferences(); InvalidateSelection();
    }
    private void RemoveExclusion_Click(object sender, RoutedEventArgs e) { if (busy || ExclusionList.SelectedItem is not string path) return; exclusions.Remove(path); SavePreferences(); InvalidateSelection(); }
    private void OpenData_Click(object sender, RoutedEventArgs e) { if (!preview) { Directory.CreateDirectory(PreferencesStore.DataFolder); Open(PreferencesStore.DataFolder); } }
    private void Open(string target, string? argument = null)
    {
        try { var start = new ProcessStartInfo(target) { UseShellExecute = true }; if (argument is not null) start.Arguments = '"' + argument + '"'; Process.Start(start); }
        catch (Exception ex) when (ex is Win32Exception or IOException) { StatusText.Text = ex.Message; }
    }
    private void CacheNav_Click(object sender, RoutedEventArgs e) => ShowPage(0);
    private void AnalyzeNav_Click(object sender, RoutedEventArgs e) => ShowPage(1);
    private void ToolsNav_Click(object sender, RoutedEventArgs e) => ShowPage(2);
    private void HistoryNav_Click(object sender, RoutedEventArgs e) { ShowPage(3); LoadHistory(); }
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(4);
    public void ShowPage(int index)
    {
        FrameworkElement[] pages = [CachePage, AnalyzePage, ToolsPage, HistoryPage, SettingsPage]; Button[] nav = [CacheNav, AnalyzeNav, ToolsNav, HistoryNav, SettingsNav];
        if (index < 0 || index >= pages.Length) throw new ArgumentException("Unknown page.");
        for (int i = 0; i < pages.Length; i++) { pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed; nav[i].Tag = i == index ? "active" : ""; }
        PageTitle.Text = new[] { "Review your cache files.", "Understand what takes space.", "Windows maintenance, reviewed.", "A record of every cleanup.", "Your preferences. Your control." }[index];
        PageSubtitle.Text = new[] { "Browser, graphics and developer caches. Choose every file before recycling.", "Explore large files and matching content without changing them.", "Supported Windows operations with explicit administrator permission.", "Local journals keep the outcome of each recycling operation.", "Save your appearance and choose folders to leave alone." }[index];
        PageEyebrow.Text = new[] { "21 CURATED RULES / RECOVERABLE CLEANUP", "READ-ONLY STORAGE EXPLORER", "ADVANCED SYSTEM TOOLS", "LOCAL ACTIVITY HISTORY", "APPEARANCE / EXCLUSIONS / PRIVACY" }[index];
    }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Windows Storage Cleaner 0.2.0\n\nMIT licensed open-source software. No telemetry.\n\n21 bundled rules, four color themes, system appearance, folder exclusions, storage exploration, duplicate analysis, operation history and Windows maintenance.\n\nNo registry cleaning, automatic duplicate deletion, or background cleanup. See the release README and SECURITY.md for limitations.", "About Windows Storage Cleaner");
    public void LoadPreview(string theme = "Paper")
    {
        var now = DateTime.UtcNow; int[] counts = [3, 6, 4, 2, 0, 3, 0, 2, 0, 0, 2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0];
        var scans = RuleCatalog.Load().Select((rule, i) => new RuleScan(rule, $@"C:\Users\Sample\AppData\Local\{rule.RelativePath}", counts[i] == 0 ? "Not present" : "Scanned",
            Enumerable.Range(0, counts[i]).Select(j => new Candidate(rule.Id, new($@"C:\Users\Sample\AppData\Local\{rule.RelativePath}\cache-{j + 1}.tmp", (j + 1) * 24_500_000L, now.AddDays(-40), now.AddDays(-50), $"demo-{i}-{j}"))).ToArray())).ToArray();
        LoadReport(new(now.AddSeconds(-2), now, false, scans, [])); Rows[1].IsSelected = true; RuleGrid.SelectedItem = Rows[1]; ThemeCombo.SelectedItem = theme;
        ScanButton.IsEnabled = ExportButton.IsEnabled = AnalyzeButton.IsEnabled = false;
        StatusText.Text = "DEMO DATA · Interface preview. These are example files, not a scan of this computer.";
        AnalysisPathBox.Text = @"C:\Users\Sample\Downloads"; AnalysisSummary.Text = "428 files · 3 large files · 1 duplicate group";
        LargeGrid.ItemsSource = new[] { new LargeFile(@"C:\Users\Sample\Downloads\project-archive.zip", 220_000_000, now.AddDays(-45)), new LargeFile(@"C:\Users\Sample\Downloads\installer.exe", 580_000_000, now.AddDays(-30)), new LargeFile(@"C:\Users\Sample\Downloads\backup-copy.zip", 220_000_000, now.AddDays(-40)) };
        DuplicateGrid.ItemsSource = new[] { new DuplicateSet("DEMO", 220_000_000, [@"C:\Users\Sample\Downloads\project-archive.zip", @"C:\Users\Sample\Downloads\backup-copy.zip"]) };
        HistoryGrid.ItemsSource = new[] { new HistoryEntry(now.AddDays(-1), "Complete · 2 skipped", 124, 220_000_000, "DEMO"), new HistoryEntry(now.AddDays(-7), "Complete · 0 skipped", 84, 146_000_000, "DEMO") };
    }
    public void VerifyPreviewControls(string outputFolder)
    {
        var row = (DataGridRow)RuleGrid.ItemContainerGenerator.ContainerFromItem(Rows[1]); var category = FindCheckBox(row) ?? throw new InvalidOperationException("Category checkbox missing.");
        Toggle(category); if (Rows[1].Files.Any(x => x.IsSelected)) throw new InvalidOperationException("Category deselection failed.");
        Toggle(category); if (Rows[1].Files.Any(x => !x.IsSelected)) throw new InvalidOperationException("Category selection failed.");
        FileGrid.UpdateLayout(); var fileRow = (DataGridRow)FileGrid.ItemContainerGenerator.ContainerFromItem(Rows[1].Files[0]); var file = FindCheckBox(fileRow) ?? throw new InvalidOperationException("File checkbox missing.");
        Toggle(file); if (Rows[1].Files[0].IsSelected || Rows[1].IsSelected is not null || SelectedCountText.Text != "5 files selected") throw new InvalidOperationException("Mixed selection failed.");
        Toggle(file); if (Rows[1].IsSelected != true || RecycleButton.IsEnabled) throw new InvalidOperationException("Cleanup preview guard failed.");
        string original = ThemeManager.CurrentName; var colors = new HashSet<Color>();
        foreach (string theme in new[] { "Paper", "Midnight", "Evergreen", "Amethyst" }) { ThemeManager.Apply(theme); colors.Add(((SolidColorBrush)Application.Current.Resources["Accent"]).Color); }
        if (colors.Count != 4) throw new InvalidOperationException("Theme palettes are not distinct."); ThemeManager.Apply(original);
        CategoryCombo.SelectedItem = "Browsers"; if (CollectionViewSource.GetDefaultView(Rows).Cast<RuleRow>().Any(x => x.Category != "Browsers")) throw new InvalidOperationException("Category filtering failed."); CategoryCombo.SelectedIndex = 0; RuleGrid.SelectedItem = Rows[1];
        if (MaintenanceItems.IsEnabled) throw new InvalidOperationException("Preview enables maintenance.");
        File.WriteAllText(Path.Combine(outputFolder, "ui-checks.txt"), "PASS category deselect\nPASS category select\nPASS individual selection\nPASS mixed state\nPASS preview cleanup blocked\nPASS four distinct themes\nPASS category filter\nPASS preview maintenance blocked\n");
        static void Toggle(CheckBox check) => ((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();
        static CheckBox? FindCheckBox(DependencyObject parent) { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is CheckBox box) return box; if (FindCheckBox(child) is { } found) return found; } return null; }
    }
}
