module GlazeWM.TrayApp.Models

open System
open System.IO
open GlazeWM.Tray.Literals
open Serilog.Core
open Serilog.Events

type AppConfig =
    { LevelSwitch: LoggingLevelSwitch
      LogsDirectory: string
      Port: int }

module AppConfig =
    let private mkLogDir () =
        let logDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlazeWM-Tray", "logs")
        if not <| Directory.Exists(logDir) then
            Directory.CreateDirectory(logDir) |> ignore

        logDir

    let load () =
        let port =
            Environment.GetEnvironmentVariable(PortDevEnvVar)
            |> Option.ofObj
            |> Option.map int
            |> Option.defaultValue GlazeWMDefaultPort

        { LevelSwitch = LoggingLevelSwitch(LogEventLevel.Information)
          LogsDirectory = mkLogDir ()
          Port = port }
