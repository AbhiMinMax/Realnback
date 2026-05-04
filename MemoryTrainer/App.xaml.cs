using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
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

        LiveCharts.Configure(config => config.AddDarkTheme());

        PathHelper.EnsureDataDirectories();
        AppLogger.Log("App", $"Starting up — data folder: {PathHelper.DataFolder}");

        var db = new DatabaseService(PathHelper.DatabasePath);
        db.InitialiseSchema();
        AppLogger.Log("App", "Database schema initialized");

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
        {
            AppLogger.Log("App", $"Incomplete session found (id={incompleteSession.Id}, name='{incompleteSession.Name}'), offering restore");
            mainVm.OfferSessionRestore(incompleteSession);
        }
        else
        {
            AppLogger.Log("App", "No incomplete session — showing config");
            mainVm.ShowConfig();
        }

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Log("App", "Shutting down");
        _trayService?.Dispose();
        base.OnExit(e);
    }

    private void SetupGlobalExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogUnhandled(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogUnhandled(ex);
        };
    }

    private static void LogUnhandled(Exception ex)
    {
        AppLogger.Error("App", "Unhandled exception", ex);
        try
        {
            File.AppendAllText(PathHelper.ErrorLogPath, $"[{DateTime.UtcNow:O}] {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
