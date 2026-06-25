module GlazeWM.TrayApp.Models

open System
open System.IO
open Farse
open GlazeWM.Tray.Extensions
open GlazeWM.Tray.Literals
open Serilog.Core
open Serilog.Events
type AppConfig =
    { LogsDirectory: string
      Port: int
      Debug: bool
      StartWithWindow: bool }

type RuntimeEnvironment =
    { LevelSwitch: LoggingLevelSwitch
      Config: AppConfig }

module AppConfig =
    open Farse.Parse
    open Farse.Operators

    let private dataDir =
        let ext =
            Environment.GetEnvironmentVariable(AppConfigDirPostfixEnv)
            |> Option.ofObj
            |> Option.defaultValue ""
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $"GlazeWM-Tray{ext}")

    let private logDir = Path.Combine(dataDir, "logs")
    let configPath = Path.Combine(dataDir, "config.json")

    let asJson (config: AppConfig) =
        JObj
            [ "Port", JNum config.Port
              "LogsDirectory", JStr config.LogsDirectory
              "Debug", JBit config.Debug
              "StartWithWindow", JBit config.StartWithWindow ]

    let asJsonString = asJson >> Json.asString Indented

    let appConfigParser =
        parser {
            let! port = "Port" &= int
            let! logsDirectory = "LogsDirectory" &= string
            let! debug = "Debug" &= bool
            let! startWithWindow = "StartWithWindow" &= bool
            return
                { Port = port
                  LogsDirectory = logsDirectory
                  Debug = debug
                  StartWithWindow = startWithWindow }
        }

    let private fromFile () =
        if Path.Exists configPath then
            let json = File.ReadAllText(configPath)
            Parser.parse json appConfigParser
            |> Result.mapError ParserError.asString
            |> Some
        else
            None

    // Creates paths and default config file. Returns the default config.
    let private init () : Result<AppConfig, string> =
        let ac =
            { LogsDirectory = logDir
              Port = GlazeWMDefaultPort
              Debug = false
              StartWithWindow = false }
        let jsonString = ac |> asJsonString
        Result.tryCatch (fun () -> File.WriteAllText(configPath, jsonString))
        |> Result.map (fun _ -> ac)

    let load () : Result<AppConfig, string> =
        if not (Path.Exists logDir) then
            Directory.CreateDirectory(logDir) |> ignore

        fromFile () |> Option.defaultWith init

module RuntimeEnvironment =
    let init (config: AppConfig) =
        let level =
            if config.Debug then
                LogEventLevel.Debug
            else
                LogEventLevel.Information
        { LevelSwitch = LoggingLevelSwitch(level)
          Config = config }

type IViewsHelpers =
    abstract member RunSyncQuery: string -> Result<string, string>
