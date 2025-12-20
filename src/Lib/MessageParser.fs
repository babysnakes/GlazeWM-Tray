namespace GlazeWM.Tray.MessageParser

open System
open System.Text.Json
open System.Text.Json.Serialization
open GlazeWM.Tray.Models
open FsToolkit.ErrorHandling
open GlazeWM.Tray.WebSocketClient
open Serilog
open Farse
open Farse.Operators

type MessageParserEvent =
    | ParseError of string
    | NoCurrentWorkspace
    | AgentError of exn

type private JsonType =
    | FocusChanged of JsonElement
    | PausedChanged of JsonElement
    | BindingModesChanged of JsonElement
    | QueryWorkspaces of JsonElement
    | Unhandled of string
    | WorkspaceStar

type private MessageType =
    | QueryResponseType of string
    | SubscriptionResponseType of string
    | UnSuccessfulResponseType of string

type private MessageTypeResult = Result<MessageType option, string>

type Parser(handler: MailboxProcessor<ParsingOutput>) =

    let queryWorkspacesPhrase = "query workspaces"

    let errorEvent = Event<MessageParserEvent>()
    let mutable wsClient: MailboxProcessor<WebSocketMessage> option = None

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
            let! clientMessage = "clientMessage" ?= Parse.string
            return clientMessage |> Option.map QueryResponseType
        }
        |> Parser.parse json

    /// Check for the current workspace and notifies the handler if found. Returns optional current workspace.
    let handleWorkspacesResponse (wr: WorkspacesResponse) =
        match wr |> WorkspaceResponse.extractCurrentWorkspace with
        | Some current ->
            current
            |> WorkspaceResponse.extractWorkspaceName
            |> CurrentWorkspace
            |> handler.Post

            Some(current, id)
        | None ->
            Log.Warning("No current workspace found in {Workspaces}", wr.Data)
            errorEvent.Trigger NoCurrentWorkspace
            None

    /// Notifies the handler with the current workspace if identified in the provided state.
    let handleFocusChangedEvent (e: FocusChangedEvent) (state: WorkspacesResponse option) =
        option {
            let! wr = state
            let! w = wr |> WorkspaceResponse.tryGetWorkspace e.Data.FocusedContainer.ParentId
            let wn = WorkspaceResponse.extractWorkspaceName w
            return wn |> CurrentWorkspace |> handler.Post
        }

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
                                let parsed = JsonSerializer.Deserialize<FocusChangedEvent>(m, options)

                                handleFocusChangedEvent parsed state
                                |> Option.defaultWith (fun () ->
                                    SendMessage queryWorkspacesPhrase |> wsClient.Value.Post)
                            | QueryWorkspaces m ->
                                Log.Debug("messageParser received query workspaces: {Message}", m)
                                let parsed = JsonSerializer.Deserialize<WorkspacesResponse>(m, options)

                                match handleWorkspacesResponse parsed with
                                | Some _ -> return! loop (Some parsed)
                                | None -> ()
                            | PausedChanged m ->
                                Log.Debug("messageParser received pause changed: {Message}", m)
                                let parsed = JsonSerializer.Deserialize<PauseChangedEvent>(m, options)
                                handler.Post(Paused parsed.Data.IsPaused)
                            | BindingModesChanged m ->
                                Log.Debug("messageParser received binding modes changed: {Message}", m)
                                let parsed = JsonSerializer.Deserialize<BindingModesChangedEvent>(m, options)
                                let nb = (parsed.Data.NewBindingModes |> List.isEmpty |> not)
                                handler.Post(NewBindingModes nb)
                            | WorkspaceStar -> wsClient.Value.Post(SendMessage queryWorkspacesPhrase)
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
                                | Ok(Some(SubscriptionResponseType "focus_changed")) ->
                                    messageParser.Post(FocusChanged root)
                                | Ok(Some(SubscriptionResponseType "binding_modes_changed")) ->
                                    messageParser.Post(BindingModesChanged root)
                                | Ok(Some(SubscriptionResponseType "pause_changed")) ->
                                    messageParser.Post(PausedChanged root)
                                | Ok(Some(SubscriptionResponseType "workspace_updated"))
                                | Ok(Some(SubscriptionResponseType "workspace_deactivated"))
                                | Ok(Some(SubscriptionResponseType "workspace_activated")) ->
                                    messageParser.Post(WorkspaceStar)
                                | Ok(Some(QueryResponseType "query workspaces")) ->
                                    messageParser.Post(QueryWorkspaces root)
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
