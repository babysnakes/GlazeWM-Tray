namespace CounterApp

open Avalonia
open GlazeWM.TrayApp.Application
open Serilog
open Serilog.Core
open Serilog.Events

module Program =

    let mkApp () =
        let levelSwitch = LoggingLevelSwitch(LogEventLevel.Information)
        Log.Logger <- LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch).WriteTo.Console().CreateLogger()
        App(levelSwitch)
        
    [<EntryPoint>]
    let main (args: string[]) =
        AppBuilder.Configure<App>(fun _ -> mkApp()).UsePlatformDetect().UseSkia().StartWithClassicDesktopLifetime(args)
