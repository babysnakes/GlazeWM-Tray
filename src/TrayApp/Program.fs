namespace GlazeWM.TrayAPP.Main

open System
open System.IO
open Avalonia
open Serilog
open Serilog.Core
open Serilog.Events
open GlazeWM.TrayApp.Application

module Program =

    let mkApp () =
        let logDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlazeWM-Tray", "logs")

        if not <| Directory.Exists(logDir) then
            Directory.CreateDirectory(logDir) |> ignore

        let levelSwitch = LoggingLevelSwitch(LogEventLevel.Information)
#if DEBUG
        Log.Logger <- LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch).WriteTo.Console().CreateLogger()
#else
        let logPath = Path.Combine(logDir, "log.txt")

        Log.Logger <-
            LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.File(logPath, fileSizeLimitBytes = 100_000, retainedFileCountLimit = 10)
                .CreateLogger()
#endif
        App(levelSwitch, logDir)

    [<EntryPoint>]
    let main (args: string[]) =
        AppDomain.CurrentDomain.UnhandledException.Add(fun e ->
            let ex = (e.ExceptionObject :?> Exception)
            Log.Fatal(ex, "Unhandled exception causing crash")
            Log.CloseAndFlush())

        AppBuilder
            .Configure<App>(fun _ -> mkApp ())
            .LogToTrace()
            .UsePlatformDetect()
            .UseSkia()
            .StartWithClassicDesktopLifetime(args)
