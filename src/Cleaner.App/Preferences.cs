using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Cleaner.App;

public sealed record UserPreferences(string Theme = "System", int MinimumAgeDays = 0, string[]? ExcludedPaths = null);
public static class PreferencesStore
{
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsStorageCleaner");
    public static string LogsFolder => Path.Combine(DataFolder, "Logs");
    public static UserPreferences Load()
    {
        string path = Path.Combine(DataFolder, "preferences.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return new();
            var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path)) ?? new();
            return new(ThemeManager.Names.Contains(preferences.Theme) ? preferences.Theme : "System",
                preferences.MinimumAgeDays is 0 or 14 or 30 or 90 ? preferences.MinimumAgeDays : 0,
                (preferences.ExcludedPaths ?? []).Take(100).Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public static void Save(UserPreferences preferences)
    {
        Directory.CreateDirectory(DataFolder);
        string path = Path.Combine(DataFolder, "preferences.json"), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
public static class ThemeManager
{
    public static string[] Names => ["System", "Paper", "Midnight", "Evergreen", "Amethyst"];
    public static string CurrentName { get; private set; } = "Paper";
    public static void Apply(string name)
    {
        if (!Names.Contains(name)) name = "System";
        CurrentName = name;
        string resolved = name;
        if (name == "System")
        {
            try { resolved = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0 ? "Midnight" : "Paper"; }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { resolved = "Paper"; }
        }
        string[] keys = ["WindowBg", "Surface", "SurfaceAlt", "Line", "Ink", "Muted", "Accent", "AccentSoft", "AccentInk", "Sidebar", "SidebarInk", "SidebarMuted", "SidebarActive", "WarningBg", "WarningInk"];
        string[] colors = resolved switch
        {
            "Midnight" => ["#0F1522","#182233","#1D293E","#334259","#EDF2FC","#A8B8CE","#70A6FF","#263D62","#B4D1FF","#0A101D","#EFF5FF","#9EAFC8","#243A5C","#3F3420","#F7D497"],
            "Evergreen" => ["#EDF5F0","#FFFFFF","#F3F8F4","#D3E4D9","#163629","#557165","#087F62","#DDF2E8","#07654F","#10352A","#EEFFF4","#A9CDBD","#215240","#FFF1D5","#795722"],
            "Amethyst" => ["#F4F1FA","#FFFFFF","#F8F6FC","#E1D9ED","#322444","#746382","#7954C8","#EDE5FB","#6941B5","#292036","#F7F0FF","#C0ADD6","#4A3664","#FFF0DC","#795322"],
            _ => ["#F2F5FA","#FFFFFF","#F7F9FC","#DCE4EF","#18253D","#5C6E89","#2563EB","#E8EFFF","#2057C7","#101C33","#EEF4FF","#A7B9D4","#243E65","#FFF3DA","#76541A"]
        };
        for (int i = 0; i < keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); brush.Freeze();
            Application.Current.Resources[keys[i]] = brush;
        }
    }
}
