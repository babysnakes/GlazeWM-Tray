namespace GlazeWM.TrayApp

open System
open GlazeWM.Tray.Literals
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.Tray.WebSocketClient
open GlazeWM.TrayApp.Helpers
open GlazeWM.TrayApp.Models
open Serilog

type GlazeWMConnectionNotification =
    | ConnectionError of string
    | Reconnected
    | BugNoCurrentWorkspace
    | BugUnsetWsClient

type GlazeWMConnection(config: AppConfig, parserMessageHandler: MailboxProcessor<ParsingOutput>) as this =
    let uri = Uri($"ws://localhost:{config.Port}/")
    let notifications = Event<GlazeWMConnectionNotification>()

    let mutable wsClient: WebSocketClient option = None

    let resetConnection (msg: string) =
        wsClient |> Option.iter (fun c -> (c :> IDisposable).Dispose())
        wsClient <- None
        notifications.Trigger(ConnectionError msg)

    let handleMessageParserEvent (msg: MessageParserEvent) =
        match msg with
        | ParseError s -> Log.Error("A parser exception had occured: {Err}", s)
        | NoCurrentWorkspace -> notifications.Trigger BugNoCurrentWorkspace
        | UnsetWsClient -> notifications.Trigger BugUnsetWsClient
        | AgentError ex ->
            Log.Error(ex, "MessageParser agent error:")
            resetConnection $"MessageParser: {ex.Message}"

    let connect () =
        let parser = Parser(parserMessageHandler)
        let client = new WebSocketClient(uri, parser.Dispatcher())
        parser.SetWsClient client.Agent
        wsClient <- Some client

        client.Error.Add resetConnection
        parser.Error.Add handleMessageParserEvent

        $"sub -e {SWorkspaceUP} {SWorkspaceACT} {SWorkspaceDeACT} {SBindingModesCH} {SPauseCH} {SFocusCH}"
        |> client.SendMessage
        this.RefreshState()

    do connect ()

    member _.Reconnect() =
        connect ()
        notifications.Trigger Reconnected

    member _.RefreshState() =
        wsClient
        |> Option.tryDo (fun c -> [ QWorkspaces; QBinding; QPaused ] |> List.iter c.SendMessage)

    member _.Send(msg: string) =
        wsClient |> Option.tryDo (fun c -> msg |> c.SendMessage)

    [<CLIEvent>]
    member this.Notifications = notifications.Publish

    member _.RunSyncQuery(query: string) =
        match wsClient with
        | Some c -> c.Query(query)
        | None -> Error "BUG: No websocket client set"

    interface IDisposable with
        member _.Dispose() =
            wsClient |> Option.iter (fun c -> (c :> IDisposable).Dispose())
