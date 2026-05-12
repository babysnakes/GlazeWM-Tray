// For more information see https://aka.ms/fsharp-console-apps
open System
open System.Reactive.Disposables
open System.Reactive.Disposables.Fluent
open GlazeWM.Tray.Literals
open Serilog
open Serilog.Core
open Serilog.Events
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.Tray.WebSocketClient

let levelSwitch = LoggingLevelSwitch(LogEventLevel.Information)
Log.Logger <- LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch).WriteTo.Console().CreateLogger()
let mutable failureOccured = false

let handleErrorMessage (msg: obj) =
    Log.Error("Error occurred in websocket client: {Message}", msg)
    failureOccured <- true

let handleError (ex: exn) = ex.Message |> handleErrorMessage

let handleMessages (msg: ParsedMessages) =
    match msg with
    | Workspaces wn ->
        Log.Information(
            "Current workspace: {Name}, {DisplayName}. Active workspaces: {Active}",
            wn.Current.Name,
            wn.Current.DisplayName,
            wn.Active |> List.map (fun w -> w.Name)
        )
    | Paused b -> Log.Information("Paused: {State}", b)
    | NewBindingModes b -> Log.Information("New binding modes: {Modes}", b)

let compositeD = new CompositeDisposable()
let args = Environment.GetCommandLineArgs()
let port = if args.Length > 1 then args[1] else "6123"
let uri = Uri($"ws://localhost:{port}/")
let client = new WebSocketClient(uri)
let parser = new Parser(client)

client.Failures.Subscribe(handleError).DisposeWith(compositeD) |> ignore
parser.Warnings.Subscribe(handleErrorMessage).DisposeWith(compositeD) |> ignore
parser.GlazewmMessages.Subscribe(handleMessages).DisposeWith(compositeD)
|> ignore

[ $"sub -e {SWorkspaceUP} {SWorkspaceACT} {SWorkspaceDeACT} {SBindingModesCH} {SPauseCH} {SFocusCH}"
  QWorkspaces ]
|> List.iter (client :> IWsClient).SendMessage

printfn
    "Type debug/info to set log level, \
         exit to quit, \
         !<query> to send a query with a single response synchronously, \
         any other input to send to GlazeWM"

let (|Query|_|) (s: string) =
    if s.StartsWith('!') then Some s[1..] else None

[<TailCall>]
let rec ReadAndSendLoop () =
    let input = Console.ReadLine()

    match input.ToLower() with
    | "exit" ->
        if not failureOccured then
            compositeD.Dispose()
            (client :> IDisposable).Dispose()
    | "debug" ->
        levelSwitch.MinimumLevel <- LogEventLevel.Debug
        ReadAndSendLoop()
    | "info" ->
        levelSwitch.MinimumLevel <- LogEventLevel.Information
        ReadAndSendLoop()
    | Query q ->
        client.Query q
        |> function
            | Ok r -> Log.Information("Response: {Message}", r)
            | Error e -> Log.Error("Error occurred: {Message}", e)
        ReadAndSendLoop()
    | _ ->
        (client :> IWsClient).SendMessage input
        ReadAndSendLoop()

ReadAndSendLoop()
Log.CloseAndFlush()
