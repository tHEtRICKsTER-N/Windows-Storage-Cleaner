using System.Security.Principal;
using System.Windows;
using Cleaner.Core;

namespace Cleaner.Maintenance;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MaintenanceOperation operation;
        try { if (e.Args.Length != 1) throw new ArgumentException("Choose one operation through Windows Storage Cleaner's Windows tools page."); operation = MaintenanceCatalog.Get(e.Args[0]); }
        catch (ArgumentException ex) { MessageBox.Show(ex.Message, "Invalid maintenance request"); Shutdown(2); return; }
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        { MessageBox.Show("Open this operation from the main app's Windows tools page and approve Windows administrator permission.", "Administrator permission required"); Shutdown(3); return; }
        MainWindow = new MaintenanceWindow(operation); MainWindow.Show();
    }
}
