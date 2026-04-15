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
    let mutable _state: QueryState = Empty

    member this.QueryInput
        with get() = _queryInput
        and set v = this.RaiseAndSetIfChanged(&_queryInput, v) |> ignore

    member private this.State
        with set v =
            _state <- v
            this.RaisePropertyChanged(nameof this.IsEmpty)
            this.RaisePropertyChanged(nameof this.IsQuerying)
            this.RaisePropertyChanged(nameof this.IsError)
            this.RaisePropertyChanged(nameof this.IsData)
            this.RaisePropertyChanged(nameof this.ErrorMessage)
            this.RaisePropertyChanged(nameof this.TreeItems)

    member _.IsEmpty    = _state = Empty
    member _.IsQuerying = _state = Querying
    member _.IsError    = match _state with ErrorMsg _     -> true | _ -> false
    member _.IsData     = match _state with ResponseData _ -> true | _ -> false

    member _.ErrorMessage =
        match _state with
        | ErrorMsg e -> e
        | _ -> ""

    member _.TreeItems =
        match _state with
        | ResponseData r -> [ JsonTreeNode("data", r) ]
        | _ -> []

    member private this.RunQuery() =
        let queryText = this.QueryInput.Trim()

        if not (String.IsNullOrWhiteSpace queryText) then
            Log.Information("Query: {Query}", queryText)
            this.State <- Querying

            async {
                let result =
                    queryFunc queryText >>= CustomParsers.tryExtractResponseData

                Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                    this.State <-
                        match result with
                        | Ok(Data r)                        -> ResponseData r
                        | Ok(GlazeWMRawResponse.ErrorMsg e) -> ErrorMsg e
                        | Error e                           -> ErrorMsg e)
            }
            |> Async.Start

    member val ExecuteQueryCommand: ReactiveCommand<Unit, Unit> =
        ReactiveCommand.Create(Action(fun () -> this.RunQuery()))
