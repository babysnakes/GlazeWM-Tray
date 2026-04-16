using System;
using System.IO;
using Avalonia;
using ReactiveUI.Avalonia;   // UseReactiveUI() extension
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GlazeWM.TrayAppCS;

class Program
{
    static AppBuilder BuildAvaloniaApp(LoggingLevelSwitch levelSwitch, string logDir) =>
        AppBuilder
            .Configure(() => new App(levelSwitch, logDir))
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace(areas: [])
            .With(new Avalonia.MacOSPlatformOptions { ShowInDock = false });

    [STAThread]
    public static int Main(string[] args)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GlazeWM-Tray", "logs");

        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

#if DEBUG
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console()
            .CreateLogger();
#else
        var logPath = Path.Combine(logDir, "log.txt");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.File(logPath, fileSizeLimitBytes: 100_000, retainedFileCountLimit: 10)
            .CreateLogger();
#endif

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal((Exception)e.ExceptionObject, "Unhandled exception causing crash");
            Log.CloseAndFlush();
        };

        return BuildAvaloniaApp(levelSwitch, logDir)
            .StartWithClassicDesktopLifetime(args);
    }
}
