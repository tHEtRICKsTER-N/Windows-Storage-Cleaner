using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cleaner.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool preview = e.Args.Length is >= 2 and <= 4 && e.Args[0] is "--render-preview" or "--render-preview-small";
        using var identity = WindowsIdentity.GetCurrent();
        if (!preview && new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("Please open Windows Storage Cleaner as a normal user. This version handles user caches and does not require administrator access.",
                "Open without administrator access", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1); return;
        }
        var window = new MainWindow(diagnosticMode: preview);
        MainWindow = window;
        if (preview)
        {
            window.LoadPreview(e.Args.Length >= 3 ? e.Args[2] : "Paper");
            if (e.Args[0] == "--render-preview-small") { window.Width = 1120; window.Height = 800; }
            window.ShowActivated = false;
            window.Left = -20000; window.Top = -20000;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.Show();
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var surface = (FrameworkElement)window.Content;
                surface.UpdateLayout();
                window.VerifyPreviewControls(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!);
                if (e.Args.Length >= 4) window.ShowPage(int.Parse(e.Args[3]));
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
                    (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.GetFullPath(e.Args[1])); encoder.Save(stream);
                Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else window.Show();
    }
}
