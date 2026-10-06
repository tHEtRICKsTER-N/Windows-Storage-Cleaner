using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Cleaner.Core;

namespace Cleaner.Maintenance;
public partial class MaintenanceWindow : Window
{
    private readonly MaintenanceOperation operation;
    private bool running, completed;
    public MaintenanceWindow(MaintenanceOperation operation)
    {
        this.operation = operation; InitializeComponent(); TitleText.Text = operation.Title; DescriptionText.Text = operation.Description; ImpactText.Text = operation.Impact;
        OutputBox.Text = string.Join(Environment.NewLine, operation.Commands.Select(x => $"{MaintenanceCatalog.ExecutablePath(x)} {string.Join(' ', x.Arguments)}")) + "\n\nReview the impact before running.\n";
    }
    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (running || completed) return;
        if (operation.ChangesSystem && MessageBox.Show(this, $"{operation.Title}\n\n{operation.Impact}\n\nRun this Windows maintenance operation now?", "Confirm Windows maintenance", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        running = true; RunButton.IsEnabled = false; StatusText.Text = "Windows is working. Keep this window open; maintenance cannot be stopped safely.";
        try
        {
            foreach (var command in operation.Commands)
            {
                var start = new ProcessStartInfo(MaintenanceCatalog.ExecutablePath(command)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System) };
                foreach (string argument in command.Arguments) start.ArgumentList.Add(argument);
                start.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
                start.Environment["PSModulePath"] = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules");
                using var process = new Process { StartInfo = start };
                process.OutputDataReceived += (_, data) => { if (data.Data is not null) Dispatcher.BeginInvoke(() => Append(data.Data)); };
                process.ErrorDataReceived += (_, data) => { if (data.Data is not null) Dispatcher.BeginInvoke(() => Append(data.Data)); };
                if (!process.Start()) throw new InvalidOperationException("Windows could not start this tool.");
                process.BeginOutputReadLine(); process.BeginErrorReadLine(); await process.WaitForExitAsync(); process.WaitForExit();
                if (process.ExitCode == 3010) { Append("Windows requests a restart to complete this operation."); StatusText.Text = "Finished. Restart Windows when convenient."; }
                else if (process.ExitCode != 0) throw new InvalidOperationException($"Windows returned exit code {process.ExitCode}. Review the output. Some earlier steps may have completed.");
                else StatusText.Text = "Operation completed. Review Windows output; free-space changes may be affected by other activity.";
            }
            completed = true; RunButton.Content = "Completed";
        }
        catch (Exception ex) { Append(ex.Message); StatusText.Text = "Operation stopped. Review the output before retrying."; completed = true; RunButton.Content = "Stopped"; }
        finally { running = false; }
    }
    private void Append(string line)
    {
        OutputBox.AppendText(line + Environment.NewLine);
        if (OutputBox.Text.Length > 512_000) OutputBox.Text = "[Earlier output trimmed]\n" + OutputBox.Text[^384_000..];
        OutputBox.ScrollToEnd();
    }
    private void Copy_Click(object sender, RoutedEventArgs e) { try { Clipboard.SetText(OutputBox.Text); } catch (System.Runtime.InteropServices.COMException ex) { StatusText.Text = ex.Message; } }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (running) { e.Cancel = true; StatusText.Text = "Windows maintenance is still running. Close this window when it finishes."; } }
}
