namespace FSharpReactiveUI.ViewModels

open System
open System.Reactive
open ReactiveUI
open FSharp.Data
open FsToolkit.ErrorHandling.Operator.Result
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open Serilog

// Hierarchical node for the JSON TreeView
type JsonTreeNode(label: string, value: JsonValue) =
    let children: JsonTreeNode list =
        match value with
        | JsonValue.Record fields ->
            fields |> Array.map (fun (k, v) -> JsonTreeNode(k, v)) |> Array.toList
        | JsonValue.Array items ->
            items |> Array.indexed |> Array.map (fun (i, v) -> JsonTreeNode(string i, v)) |> Array.toList
        | _ -> []

    member _.Label = label
    member _.Value = value
    member _.Children: JsonTreeNode list = children

    member _.DisplayText =
        match value with
        | JsonValue.String s -> $"{label}: {s}"
        | JsonValue.Number n -> $"{label}: {n}"
        | JsonValue.Float f -> $"{label}: {f}"
        | JsonValue.Boolean b -> $"{label}: {b}"
        | JsonValue.Null -> $"{label}: null"
        | JsonValue.Record _ -> label
        | JsonValue.Array _ -> label

type private QueryState =
    | Empty
    | Querying
    | ErrorMsg of string
    | ResponseData of JsonValue

type QueryPanelViewModel(queryFunc: string -> Result<string, string>) as this =
    inherit ViewModelBase()

    let mutable _queryInput = ""
    let mutable _isEmpty = true
    let mutable _isQuerying = false
    let mutable _isError = false
    let mutable _isData = false
    let mutable _errorMessage = ""
    let mutable _treeItems: JsonTreeNode list = []

    member this.QueryInput
        with get() = _queryInput
        and set v = this.RaiseAndSetIfChanged(&_queryInput, v) |> ignore

    member this.IsEmpty
        with get() = _isEmpty
        and set v = this.RaiseAndSetIfChanged(&_isEmpty, v) |> ignore

    member this.IsQuerying
        with get() = _isQuerying
        and set v = this.RaiseAndSetIfChanged(&_isQuerying, v) |> ignore

    member this.IsError
        with get() = _isError
        and set v = this.RaiseAndSetIfChanged(&_isError, v) |> ignore

    member this.IsData
        with get() = _isData
        and set v = this.RaiseAndSetIfChanged(&_isData, v) |> ignore

    member this.ErrorMessage
        with get() = _errorMessage
        and set v = this.RaiseAndSetIfChanged(&_errorMessage, v) |> ignore

    member this.TreeItems
        with get() = _treeItems
        and set v = this.RaiseAndSetIfChanged(&_treeItems, v) |> ignore

    member private this.SetState(state: QueryState) =
        this.IsEmpty <- (state = Empty)
        this.IsQuerying <- (state = Querying)
        this.IsError <- (match state with | ErrorMsg _ -> true | _ -> false)
        this.IsData <- (match state with | ResponseData _ -> true | _ -> false)

        match state with
        | ErrorMsg e -> this.ErrorMessage <- e
        | ResponseData r -> this.TreeItems <- [ JsonTreeNode("data", r) ]
        | _ -> ()

    member private this.RunQuery() =
        let queryText = this.QueryInput.Trim()

        if not (String.IsNullOrWhiteSpace queryText) then
            Log.Information("Query: {Query}", queryText)
            this.SetState Querying

            async {
                let result =
                    queryFunc queryText >>= CustomParsers.tryExtractResponseData

                Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                    match result with
                    | Ok(GlazeWMRawResponse.Data r) -> this.SetState(ResponseData r)
                    | Ok(GlazeWMRawResponse.ErrorMsg e) -> this.SetState(ErrorMsg e)
                    | Error e -> this.SetState(ErrorMsg e))
            }
            |> Async.Start

    member val ExecuteQueryCommand: ReactiveCommand<Unit, Unit> =
        ReactiveCommand.Create(Action(fun () -> this.RunQuery()))
