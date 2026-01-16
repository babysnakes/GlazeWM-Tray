// For more information see https://aka.ms/fsharp-console-apps
open System
open Serilog
open Serilog.Core
open Serilog.Events
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.Tray.WebSocketClient

let levelSwitch = LoggingLevelSwitch(LogEventLevel.Information)
Log.Logger <- LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch).WriteTo.Console().CreateLogger()

let demoHandler =
    MailboxProcessor.Start(fun (inbox: MailboxProcessor<ParsingOutput>) ->
        let rec loop () =
            async {
                let! msg = inbox.Receive()

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
                | UnSuccessfulResponse msg -> Log.Error("Unsuccessful Response: {Message}", msg)

                return! loop ()
            }

        loop ())

let args = Environment.GetCommandLineArgs()
let port = if args.Length > 1 then args[1] else "6123"
let uri = Uri($"ws://localhost:{port}/")
let parser = Parser(demoHandler)
let client = new WebSocketClient(uri, parser.Dispatcher())
parser.SetWsClient client.Agent
let mutable failureOccured = false
client.InitializeSubscription()

// IMPORTANT: listen to error events
client.Error.Add(fun msg ->
    Log.Error("Error occurred in websocket client: {Message}", msg)
    failureOccured <- true)

parser.Error.Add(fun msg -> Log.Error("Error occurred in message parser: {Message}", msg))

printfn "Type debug/info to set log level, exit to quit, any other input to send to GlazeWM"

[<TailCall>]
let rec ReadAndSendLoop () =
    let input = Console.ReadLine()

    match input.ToLower() with
    | "exit" -> if not failureOccured then client.Agent.PostAndReply(Exit)
    | "debug" ->
        levelSwitch.MinimumLevel <- LogEventLevel.Debug
        ReadAndSendLoop()
    | "info" ->
        levelSwitch.MinimumLevel <- LogEventLevel.Information
        ReadAndSendLoop()
    | "refresh" ->
        client.RefreshState()
        ReadAndSendLoop()
    | _ ->
        client.Agent.Post(SendMessage input)
        ReadAndSendLoop()

ReadAndSendLoop()
Log.CloseAndFlush()
