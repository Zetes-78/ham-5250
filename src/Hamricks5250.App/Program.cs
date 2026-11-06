using System.Windows;
using Hamricks5250.Core;

namespace Hamricks5250.App;

/// <summary>
/// Entry point. Command line:
///   Hamricks5250.exe [--profile "Name"] [--host name] [--port 992] [--notls] [--kiosk]
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var settings = AppSettings.Load();
        string? profileName = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "--profile" when next != null: profileName = next; i++; break;
                case "--host" when next != null: settings.GetProfile(profileName).Host = next; i++; break;
                case "--port" when next != null && int.TryParse(next, out var port): settings.GetProfile(profileName).Port = port; i++; break;
                case "--notls": settings.GetProfile(profileName).UseTls = false; break;
                case "--kiosk": settings.Kiosk = true; break;
            }
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.SessionEnding += (_, _) => MainWindow.WindowsShuttingDown = true;
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show($"Something went wrong:\n{e.Exception.Message}", "Ham 5250", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        var window = new MainWindow(settings, profileName);
        return app.Run(window);
    }
}
