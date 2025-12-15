namespace GlazeWM.Tray.MessageParser

open System.Text.Json
open System.Text.Json.Serialization
open GlazeWM.Tray.Models
open FsToolkit.ErrorHandling
open GlazeWM.Tray.WebSocketClient
open Serilog

type private JsonType =
    | FocusChanged of JsonElement
    | QueryWorkspaces of JsonElement

type Parser(handler: MailboxProcessor<ParsingOutput>) =

    let queryWorkspacesPhrase = "query workspaces"

    let errorEvent = Event<string>()
    let mutable wsClient: MailboxProcessor<WebSocketMessage> option = None

    let extractRoot (json: string) =
        JsonDocument.Parse(json) |> _.RootElement

    let (|SubEvent|_|) (elem: JsonElement) =
        let mutable prop = Unchecked.defaultof<JsonElement>

        if
            elem.TryGetProperty("messageType", &prop)
            && prop.GetString() = "event_subscription"
        then
            let data = elem.GetProperty("data")
            data.GetProperty("eventType").GetString() |> Some
        else
            None

    let (|QueryResp|_|) (elem: JsonElement) =
        let mutable prop = Unchecked.defaultof<JsonElement>

        if elem.TryGetProperty("clientMessage", &prop) then
            prop.GetString() |> Some
        else
            None

    let handleWorkspacesResponse (wr: WorkspacesResponse) =
        match wr |> WorkspaceResponse.extractCurrentWorkspace with
        | Some current ->
            current
            |> WorkspaceResponse.extractWorkspaceName
            |> CurrentWorkspace
            |> handler.Post

            Some(current, id)
        | None ->
            errorEvent.Trigger "No current workspace found"
            failwith "not implemented"

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

                        match msg with
                        | FocusChanged m ->
                            Log.Debug("messageParser received focus changed: {Message}", m)
                            let parsed = JsonSerializer.Deserialize<FocusChangedEvent>(m, options)

                            handleFocusChangedEvent parsed state
                            |> Option.defaultWith (fun () -> SendMessage queryWorkspacesPhrase |> wsClient.Value.Post)
                        | QueryWorkspaces m ->
                            Log.Debug("messageParser received query workspaces: {Message}", m)
                            let parsed = JsonSerializer.Deserialize<WorkspacesResponse>(m, options)

                            match handleWorkspacesResponse parsed with
                            | Some _ -> return! loop (Some parsed)
                            | response ->
                                errorEvent.Trigger $"handleWorkspacesResponse failed {response}"
                                failwith "not implemented"

                        do! loop state
                    }

                loop None)

        agent.Error.Add(fun s -> errorEvent.Trigger s.Message)
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

                            match root with
                            | SubEvent "focus_changed" -> messageParser.Post(FocusChanged root)
                            | QueryResp "query workspaces" -> messageParser.Post(QueryWorkspaces root)
                            | _ -> Log.Warning("Unknown message: {Message}", root)
                        with ex ->
                            Log.Error("Error parsing message: {Ex}", ex)

                        do! loop ()
                    }

                loop ())

        agent.Error.Add(fun s -> errorEvent.Trigger s.Message)
        agent

    member this.SetWsClient(client: MailboxProcessor<WebSocketMessage>) = wsClient <- Some client
