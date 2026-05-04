namespace GlazeWM.TrayAPP.Main

open System
open System.IO
open Avalonia
open GlazeWM.TrayApp.Models
open Serilog
open GlazeWM.TrayApp.Application

module Program =

    let mkApp () =
        let config = AppConfig.load ()
#if DEBUG
        Log.Logger <-
            LoggerConfiguration().MinimumLevel.ControlledBy(config.LevelSwitch).WriteTo.Console().CreateLogger()
#else
        let logPath = Path.Combine(config.LogsDirectory, "log.txt")

        Log.Logger <-
            LoggerConfiguration()
                .MinimumLevel.ControlledBy(config.LevelSwitch)
                .WriteTo.File(logPath, fileSizeLimitBytes = 100_000, retainedFileCountLimit = 10)
                .CreateLogger()
#endif
        App(config)

    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp () =
        AppBuilder
            .Configure<App>(fun _ -> mkApp ())
            .LogToTrace()
            .UsePlatformDetect()
            .UseSkia()
            .WithInterFont()
            .With(MacOSPlatformOptions(ShowInDock = false))

    [<EntryPoint; STAThread>]
    let main (args: string[]) =
        AppDomain.CurrentDomain.UnhandledException.Add(fun e ->
            let ex = (e.ExceptionObject :?> Exception)
            Log.Fatal(ex, "Unhandled exception causing crash")
            Log.CloseAndFlush())

        buildAvaloniaApp().StartWithClassicDesktopLifetime(args)
