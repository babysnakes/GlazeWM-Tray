namespace GlazeWM.Tray.MessageParser

open System
open System.Reactive.Disposables
open System.Reactive.Linq
open Farse
open Farse.Operators
open FsToolkit.ErrorHandling
open FSharp.Control.Reactive
open GlazeWM.Tray.Extensions
open Serilog
open GlazeWM.Tray.Literals
open GlazeWM.Tray.Models
open GlazeWM.Tray.Models.WorkspaceResponse

type private JsonType =
    | FocusChanged of string
    | PausedChanged of string
    | BindingModesChanged of string
    | QueryWorkspaces of string
    | QueryPaused of string
    | QueryBindingModes of string
    | WorkspaceStar

type ParserWarnings =
    | UnsuccessfulResponse of string
    | ParserError of string
    | UnexpectedError of exn
    | NoCurrentWorkspace

type private MessageType =
    | QueryResponseType of string
    | SubscriptionResponseType of string
    | UnSuccessfulResponseType of string

type private MessageTypeResult = Result<MessageType option, string>

type Parser(client: IWsClient) as this =

    let compositeD = new CompositeDisposable()
    let glazewmMessages = Subject<ParsedMessage>.broadcast
    let warnings = Subject<ParserWarnings>.broadcast

    do
        client.ReceivedMessages
        |> Observable.choose this.Dispatch
        |> Observable.scanInit empty this.ParseMessage
        |> Observable.subscribe ignore
        |> Disposable.disposeWith compositeD

        glazewmMessages |> Disposable.disposeWith compositeD
        warnings |> Disposable.disposeWith compositeD

    let logParseError subject err =
        Log.Error("Error parsing {Subject}: {Err}", subject, err)

    /// Stops after the first matcher succeeds or in errors
    let mergeMatcher (json: string) (f: string -> MessageTypeResult) (current: MessageTypeResult) : MessageTypeResult =
        match current with
        | Ok(Some _) -> current
        | Ok None -> f json
        | Error e -> Error e

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
        |> Result.mapError ParserError.asString

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
        |> Result.mapError ParserError.asString

    let tryQueryResponse (json: string) =
        parser {
            // Assuming this is called after `trySubscriptionEvent`, it must be of the type 'client_response'
            let! clientMessage = "clientMessage" ?= Parse.string
            return clientMessage |> Option.map QueryResponseType
        }
        |> Parser.parse json
        |> Result.mapError ParserError.asString

    let parseFocusChangedEvent (json: string) (state: WorkspacesResponse) =
        Log.Debug("messageParser received focus changed: {Message}", json)
        resultOption {
            let! parsed = Parser.parse json FocusChangedEventData.windowParser
            return! tryGetWorkspaceNotificationByGuid parsed state |> Ok
        }
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "focus changed event")

    let parseWorkspaceResponse (json: string) =
        Log.Debug("messageParser received query workspaces: {Message}", json)
        Parser.parse json wrParser
        |> ResultOption.ofResult
        |> ResultOption.bind (fun wr ->
            match wr |> tryGetWorkspacesNotification with
            | Some wn -> Some(wr, wn)
            | None ->
                Log.Warning("No current workspace found in {Workspaces}", wr.Data)
                None
            |> ResultOption.ofOption)
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "workspace response")

    let parseQueryPausedResponse (json: string) =
        Log.Debug("messageParser received query paused: {Message}", json)
        parser { return! "data" &= Parse.bool }
        |> Parser.parse json
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "query paused response")

    let parseBindingModesResponse (json: string) =
        Log.Debug("messageParser received query binding modes: {Message}", json)
        Parser.parse json BindingModeQueryResponse.parser
        |> Result.map (fun parsed -> parsed.Data.BindingModes |> List.isEmpty |> not)
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "process binding modes response")

    let parsePausedChangedEvent (json: string) =
        Log.Debug("messageParser received pause changed: {Message}", json)
        Parser.parse json PauseChangedEvent.parser
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "pause changed event")

    let parseBindingModesEvent (json: string) =
        Log.Debug("messageParser received binding modes changed: {Message}", json)
        Parser.parse json BindingModesChangedEvent.parser
        |> Result.map (fun parsed -> parsed.Data.NewBindingModes |> List.isEmpty |> not)
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "process binding modes event")

    member private _.Dispatch msg =
        try
            tryUnsuccessfulResponse msg
            |> mergeMatcher msg trySubscriptionEvent
            |> mergeMatcher msg tryQueryResponse
            |> function
                | Ok(Some(UnSuccessfulResponseType msg)) ->
                    msg |> UnsuccessfulResponse |> warnings.OnNext
                    None
                | Ok(Some(SubscriptionResponseType SFocusCH)) -> Some(FocusChanged msg)
                | Ok(Some(SubscriptionResponseType SBindingModesCH)) -> Some(BindingModesChanged msg)
                | Ok(Some(SubscriptionResponseType SPauseCH)) -> Some(PausedChanged msg)
                | Ok(Some(SubscriptionResponseType SWorkspaceUP))
                | Ok(Some(SubscriptionResponseType SWorkspaceDeACT))
                | Ok(Some(SubscriptionResponseType SWorkspaceACT)) -> Some WorkspaceStar
                | Ok(Some(QueryResponseType QWorkspaces)) -> Some(QueryWorkspaces msg)
                | Ok(Some(QueryResponseType QPaused)) -> Some(QueryPaused msg)
                | Ok(Some(QueryResponseType QBinding)) -> Some(QueryBindingModes msg)
                | Ok _ ->
                    Log.Debug("Unhandled message: {Message}", msg)
                    None
                | Error e ->
                    Log.Error("Error parsing message: {Ex}", e)
                    ParserError e |> warnings.OnNext
                    None
        with ex ->
            UnexpectedError ex |> warnings.OnNext
            None


    member private _.ParseMessage (state: WorkspacesResponse) (msg: JsonType) =
        try
            match msg with
            | FocusChanged m ->
                match parseFocusChangedEvent m state with
                | Ok(Some wn) -> wn |> Workspaces |> glazewmMessages.OnNext
                | Ok None -> client.SendMessage QWorkspaces
                | Error e -> ParserError e |> warnings.OnNext

                state
            | QueryWorkspaces m ->
                match parseWorkspaceResponse m with
                | Ok(Some(wr, wn)) ->
                    wn |> Workspaces |> glazewmMessages.OnNext
                    wr
                | Ok None ->
                    warnings.OnNext NoCurrentWorkspace
                    state
                | Error e ->
                    ParserError e |> warnings.OnNext
                    state
            | QueryPaused m ->
                match parseQueryPausedResponse m with
                | Ok paused -> paused |> Paused |> glazewmMessages.OnNext
                | Error e -> e |> ParserError |> warnings.OnNext

                state
            | QueryBindingModes m ->
                match parseBindingModesResponse m with
                | Ok bm -> bm |> NewBindingModes |> glazewmMessages.OnNext
                | Error e -> e |> ParserError |> warnings.OnNext

                state
            | PausedChanged m ->
                match parsePausedChangedEvent m with
                | Ok event -> event.Data.IsPaused |> Paused |> glazewmMessages.OnNext
                | Error e -> e |> ParserError |> warnings.OnNext

                state
            | BindingModesChanged m ->
                match parseBindingModesEvent m with
                | Ok nb -> nb |> NewBindingModes |> glazewmMessages.OnNext
                | Error e -> e |> ParserError |> warnings.OnNext

                state
            | WorkspaceStar ->
                client.SendMessage QWorkspaces
                state
        with ex ->
            UnexpectedError ex |> warnings.OnNext
            state

    /// GlazeWM Parsed Messages
    member _.GlazewmMessages = glazewmMessages.AsObservable()

    /// Parser warnings
    member _.Warnings = warnings.AsObservable()

    interface IDisposable with
        member _.Dispose() = compositeD.Dispose()

module CustomParsers =
    open FSharp.Data
    open JsonExtensions
    open Parse

    let logParseError subject err =
        Log.Error("Error parsing {Subject}: {Err}", subject, err)

    let successfulMessage (msg: string option) (success: bool) =
        if success then
            Ok true
        else
            Error(msg |> Option.defaultValue "Unspecified Error")

    let private extractResponseData (json: string) =
        let jsonData = JsonValue.Parse(json)
        let success = jsonData?success.AsBoolean()
        if success then
            Data jsonData?data
        else
            ErrorMsg(jsonData?error.AsString())

    let tryExtractResponseData json =
        Result.tryCatch (fun () -> extractResponseData json)

    let parseSuccess (json: string) =
        parser {
            let! msg = "error" ?= string
            let! _ = "success" &= valid bool (successfulMessage msg)
            return ()
        }
        |> Parser.parse json
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "unsuccessful response")

    /// Parses output of 'Query Workspaces' that includes the windows.
    let parseWorkspaces (json: string) =
        parser {
            let! msg = "error" ?= string
            let! _ = "success" &= valid bool (successfulMessage msg)
            let! data = "data.workspaces" &= list Workspaces.parse
            return data
        }
        |> Parser.parse json
        |> Result.mapError ParserError.asString
        |> Result.teeError (logParseError "workspace response")
