using MemoryTrainer.Helpers;
using MemoryTrainer.Services;
using MemoryTrainer.ViewModels;
using MemoryTrainer.Views;
using System.Windows;
using Application = System.Windows.Application;

namespace MemoryTrainer;

public partial class App : Application
{
    private TrayService? _trayService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SetupGlobalExceptionHandling();

        PathHelper.EnsureDataDirectories();

        var db = new DatabaseService(PathHelper.DatabasePath);
        db.InitialiseSchema();

        var screenshotService = new ScreenshotService(PathHelper.ScreenshotsPath);
        var audioCaptureService = new AudioCaptureService();
        var cameraCaptureService = new CameraCaptureService();
        var cleanupService = new CleanupService(db);
        var engine = new SessionEngine(db, screenshotService, audioCaptureService, cameraCaptureService, cleanupService);

        var mainVm = new MainViewModel(engine, db, cleanupService);
        var mainWindow = new MainWindow { DataContext = mainVm };
        MainWindow = mainWindow;

        _trayService = new TrayService(engine, mainWindow);

        var incompleteSession = db.GetIncompleteSessionAsync().GetAwaiter().GetResult();
        if (incompleteSession != null)
            mainVm.OfferSessionRestore(incompleteSession);
        else
            mainVm.ShowConfig();

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        base.OnExit(e);
    }

    private void SetupGlobalExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogError(ex);
        };
    }

    private static void LogError(Exception ex)
    {
        try
        {
            File.AppendAllText(PathHelper.ErrorLogPath, $"[{DateTime.UtcNow:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Swallow — error.log write failed
        }
        System.Diagnostics.Debug.WriteLine($"[App] Unhandled exception: {ex}");
    }
}
