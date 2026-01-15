namespace GlazeWM.Tray.MessageParser

open System
open System.Text.Json
open System.Text.Json.Serialization
open Farse
open Farse.Operators
open FsToolkit.ErrorHandling
open Serilog
open GlazeWM.Tray.Literals
open GlazeWM.Tray.Models
open GlazeWM.Tray.Models.WorkspaceResponse
open GlazeWM.Tray.WebSocketClient

type MessageParserEvent =
    | ParseError of string
    | NoCurrentWorkspace
    | UnsetWsClient
    | AgentError of exn

type private JsonType =
    | FocusChanged of string
    | PausedChanged of JsonElement
    | BindingModesChanged of JsonElement
    | QueryWorkspaces of JsonElement
    | QueryPaused of string
    | QueryBindingModes of JsonElement
    | Unhandled of string
    | WorkspaceStar

type private MessageType =
    | QueryResponseType of string
    | SubscriptionResponseType of string
    | UnSuccessfulResponseType of string

type private MessageTypeResult = Result<MessageType option, string>

type Parser(handler: MailboxProcessor<ParsingOutput>) =

    let errorEvent = Event<MessageParserEvent>()
    let mutable wsClient: MailboxProcessor<WebSocketMessage> option = None

    let sendWebSocketMessage (msg: string) =
        match wsClient with
        | Some client -> client.Post(SendMessage msg)
        | None ->
            Log.Error("No websocket client set")
            errorEvent.Trigger UnsetWsClient

    /// Stops after the first matcher succeeds or in errors
    let mergeMatcher (json: string) (f: string -> MessageTypeResult) (current: MessageTypeResult) : MessageTypeResult =
        match current with
        | Ok(Some _) -> current
        | Ok None -> f json
        | Error e -> Error e

    let extractRoot (json: string) =
        JsonDocument.Parse(json) |> _.RootElement

    let tryUnsuccessfulResponse (json: string) =
        parser {
            let! success = "success" &= Parse.bool
            and! error = "error" ?= Parse.string

            if success then
                return None
            else
                return
                    error
                    |> Option.defaultValue "Unspecified Error"
                    |> UnSuccessfulResponseType
                    |> Some
        }
        |> Parser.parse json

    let trySubscriptionEvent (json: string) =
        parser {
            let! messageType = "messageType" ?= Parse.string

            if messageType = Some "event_subscription" then
                let! eventType = "data.eventType" &= Parse.string
                return eventType |> SubscriptionResponseType |> Some
            else
                return None
        }
        |> Parser.parse json

    let tryQueryResponse (json: string) =
        parser {
            // Assuming this is called after `trySubscriptionEvent`, it must be of the type 'client_response'
            let! clientMessage = "clientMessage" ?= Parse.string
            return clientMessage |> Option.map QueryResponseType
        }
        |> Parser.parse json

    /// Check for the current workspace and notifies the handler if found. Returns optional current workspace.
    let handleWorkspacesResponse (wr: WorkspacesResponse) =
        wr.Data.Workspaces
        |> List.map extractWorkspaceName
        |> ActiveWorkspaces
        |> handler.Post

        match wr |> extractCurrentWorkspace with
        | Some current ->
            current |> extractWorkspaceName |> CurrentWorkspace |> handler.Post

            Some(current, id)
        | None ->
            Log.Warning("No current workspace found in {Workspaces}", wr.Data)
            errorEvent.Trigger NoCurrentWorkspace
            None

    /// Notifies the handler with the current workspace if identified in the provided state.
    let handleFocusChangedEvent (json: string) (state: WorkspacesResponse) =
        parser {
            let! containerType = "data.focusedContainer.type" &= Parse.string

            if containerType = "window" then
                let! parentId = "data.focusedContainer.parentId" &= Parse.guid

                return
                    tryGetWorkspace parentId state
                    |> Option.map extractWorkspaceName
                    |> Option.map (CurrentWorkspace >> handler.Post)
            else
                return None
        }
        |> Parser.parse json
        |> Result.teeError (fun e ->
            Log.Error("Error parsing focus changed event: {Ex}", e)
            errorEvent.Trigger(ParseError e))
        |> Result.toOption
        |> Option.flatten

    let handleQueryPausedResponse (json: string) =
        parser {
            let! paused = "data" &= Parse.bool
            return paused |> Paused |> handler.Post
        }
        |> Parser.parse json
        |> Result.teeError (fun e ->
            Log.Error("Error parsing query paused response: {Ex}", e)
            errorEvent.Trigger(ParseError e))
        |> ignore

    let messageParser =
        let agent =
            MailboxProcessor<JsonType>.Start(fun inbox ->
                let rec loop (state: WorkspacesResponse option) =
                    let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                    options.PropertyNameCaseInsensitive <- true

                    async {
                        let! msg = inbox.Receive()

                        try
                            match msg with
                            | FocusChanged m ->
                                Log.Debug("messageParser received focus changed: {Message}", m)

                                state
                                |> Option.bind (handleFocusChangedEvent m)
                                |> Option.defaultWith (fun () -> sendWebSocketMessage QWorkspaces)
                            | QueryWorkspaces m ->
                                Log.Debug("messageParser received query workspaces: {Message}", m)
                                let parsed = JsonSerializer.Deserialize<WorkspacesResponse>(m, options)

                                match handleWorkspacesResponse parsed with
                                | Some _ -> return! loop (Some parsed)
                                | None -> ()
                            | QueryPaused m ->
                                Log.Debug("messageParser received query paused: {Message}", m)
                                handleQueryPausedResponse m
                            | QueryBindingModes m ->
                                Log.Debug("messageParser received query binding modes: {Message}", m)
                                let parsed = JsonSerializer.Deserialize<BindingModesQueryResponse>(m, options)
                                let nb = (parsed.Data.BindingModes |> List.isEmpty |> not)
                                handler.Post(NewBindingModes nb)
                            | PausedChanged m ->
                                Log.Debug("messageParser received pause changed: {Message}", m)
                                let parsed = JsonSerializer.Deserialize<PauseChangedEvent>(m, options)
                                handler.Post(Paused parsed.Data.IsPaused)
                            | BindingModesChanged m ->
                                Log.Debug("messageParser received binding modes changed: {Message}", m)
                                let parsed = JsonSerializer.Deserialize<BindingModesChangedEvent>(m, options)
                                let nb = (parsed.Data.NewBindingModes |> List.isEmpty |> not)
                                handler.Post(NewBindingModes nb)
                            | WorkspaceStar -> sendWebSocketMessage QWorkspaces
                            | Unhandled m -> Log.Warning("Unhandled message: {Message}", m)
                        with
                        | :? OperationCanceledException as ex ->
                            Log.Information "Parser cancelled"
                            raise ex
                        | ex ->
                            Log.Error("Error parsing json {Ex}", ex)
                            errorEvent.Trigger(ParseError ex.Message)

                        do! loop state
                    }

                loop None)

        agent.Error.Add(fun exn -> errorEvent.Trigger(AgentError exn))
        agent

    [<CLIEvent>]
    member this.Error = errorEvent.Publish

    member _.Dispatcher() =
        let agent =
            MailboxProcessor<string>.Start(fun inbox ->
                let rec loop () =
                    async {
                        let! msg = inbox.Receive()

                        try
                            let root = extractRoot msg

                            tryUnsuccessfulResponse msg
                            |> mergeMatcher msg trySubscriptionEvent
                            |> mergeMatcher msg tryQueryResponse
                            |> function
                                | Ok(Some(UnSuccessfulResponseType msg)) -> handler.Post(UnSuccessfulResponse msg)
                                | Ok(Some(SubscriptionResponseType SFocusCH)) -> messageParser.Post(FocusChanged msg)
                                | Ok(Some(SubscriptionResponseType SBindingModesCH)) ->
                                    messageParser.Post(BindingModesChanged root)
                                | Ok(Some(SubscriptionResponseType SPauseCH)) -> messageParser.Post(PausedChanged root)
                                | Ok(Some(SubscriptionResponseType SWorkspaceUP))
                                | Ok(Some(SubscriptionResponseType SWorkspaceDeACT))
                                | Ok(Some(SubscriptionResponseType SWorkspaceACT)) -> messageParser.Post(WorkspaceStar)
                                | Ok(Some(QueryResponseType QWorkspaces)) -> messageParser.Post(QueryWorkspaces root)
                                | Ok(Some(QueryResponseType QPaused)) -> messageParser.Post(QueryPaused msg)
                                | Ok(Some(QueryResponseType QBinding)) -> messageParser.Post(QueryBindingModes root)
                                | Ok _ -> messageParser.Post(Unhandled msg)
                                | Error e ->
                                    Log.Error("Error parsing message: {Ex}", e)
                                    errorEvent.Trigger(ParseError e)
                        with
                        | :? OperationCanceledException as ex ->
                            Log.Information "Parser cancelled"
                            raise ex
                        | ex ->
                            Log.Error("Error parsing message: {Ex}", ex)
                            errorEvent.Trigger(ParseError ex.Message)

                        do! loop ()
                    }

                loop ())

        agent.Error.Add(fun exn -> errorEvent.Trigger(AgentError exn))
        agent

    member this.SetWsClient(client: MailboxProcessor<WebSocketMessage>) = wsClient <- Some client
