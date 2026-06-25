namespace GlazeWM.TrayAPP.Main

open System
open System.IO
open Avalonia
open GlazeWM.TrayApp.Models
open Serilog
open GlazeWM.TrayApp.Application

module Program =

    let mkApp () =
        let config =
            match AppConfig.load () with
            | Ok config -> config
            | Error e ->
                // TODO: show a message box to the user
                failwith $"Error parsing config: {e}"
        let runtimeEnv = RuntimeEnvironment.init config
        let logPath = Path.Combine(config.LogsDirectory, "log.txt")

        Log.Logger <-
            LoggerConfiguration()
                .MinimumLevel.ControlledBy(runtimeEnv.LevelSwitch)
                .WriteTo.File(logPath, fileSizeLimitBytes = 100_000, retainedFileCountLimit = 10)
                .CreateLogger()
        App(runtimeEnv)

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
