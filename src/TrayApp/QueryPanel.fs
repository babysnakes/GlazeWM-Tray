namespace GlazeWM.TrayApp.Views

open System.Collections.Generic
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open FSharp.Data
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open Serilog

module QueryPanel =
    open FsToolkit.ErrorHandling.Operator.Result

    type private State =
        | ResponseData of JsonValue
        | ErrorMsg of string
        | Querying
        | Empty

    type private JsonItem =
        | JsonElement of JsonValue
        | JsonRecordElement of name: string * value: JsonValue

    let view (f: string -> Result<string, string>) =
        Component(fun ctx ->
            let state = ctx.useState State.Empty
            let query = ctx.useState ""
            let queryInput = ctx.useState ""

            ctx.useEffect (
                handler =
                    (fun _ ->
                        let queryText = query.Current
                        if System.String.IsNullOrWhiteSpace queryText then
                            state.Set Empty
                        else
                            state.Set Querying
                            // We need to run this in a separate thread to not block the UI thread
                            async {
                                match f queryText >>= CustomParsers.tryExtractResponseData with
                                | Ok(GlazeWMRawResponse.Data r) -> state.Set(ResponseData r)
                                | Ok(GlazeWMRawResponse.ErrorMsg e) -> state.Set(ErrorMsg e)
                                | Error e -> state.Set(ErrorMsg e)
                            }
                            |> Async.Start),
                triggers = [ EffectTrigger.AfterChange query ]
            )
            ctx.useEffect (
                handler = (fun _ -> Log.Information("Query: {Query}", queryInput.Current)),
                triggers = [ EffectTrigger.AfterChange query ]
            )

            let itemsSelector (data: JsonItem) : JsonItem seq =
                match data with
                | JsonElement e -> e
                | JsonRecordElement(_, v) -> v
                |> function
                    | JsonValue.Array a -> a |> Seq.indexed |> Seq.map (fun (i, v) -> JsonRecordElement($"{i}", v))
                    | JsonValue.Record r -> r |> Seq.map JsonRecordElement
                    | _ -> Array.empty

            let treeView (item: JsonItem) =
                match item with
                | JsonElement e ->
                    TextBlock.create
                        [ match e with
                          | JsonValue.String s -> TextBlock.text s
                          | JsonValue.Number n -> TextBlock.text $"{n}"
                          | JsonValue.Float f -> TextBlock.text $"{f}"
                          | JsonValue.Boolean b -> TextBlock.text $"{b}"
                          | JsonValue.Null -> TextBlock.text "null"
                          | JsonValue.Record _ -> TextBlock.text ""
                          | JsonValue.Array _ -> TextBlock.text "" ]
                | JsonRecordElement(name, value) ->
                    TextBlock.create
                        [ match value with
                          | JsonValue.String s -> TextBlock.text $"{name} :  {s}"
                          | JsonValue.Number n -> TextBlock.text $"{name} :  {n}"
                          | JsonValue.Float f -> TextBlock.text $"{name} :  {f}"
                          | JsonValue.Boolean b -> TextBlock.text $"{name} :  {b}"
                          | JsonValue.Null -> TextBlock.text $"{name} :  null"
                          | JsonValue.Record _ -> TextBlock.text $"{name}"
                          | JsonValue.Array _ -> TextBlock.text $"{name}" ]


            let waiting: IView = TextBlock.create [ TextBlock.text "Waiting for response..." ]
            let empty: IView = TextBlock.create [ TextBlock.text "Please enter a query." ]
            let error msg : IView =
                TextBlock.create [ TextBlock.foreground "red"; TextBlock.text msg ]
            let data (json: JsonItem) : IView =
                TreeView.create
                    [ TreeView.dataItems [ json ]
                      TreeView.itemTemplate (DataTemplateView<JsonItem>.create (itemsSelector, treeView)) ]
            let responseView () =
                match state.Current with
                | Empty -> empty
                | Querying -> waiting
                | ErrorMsg e -> error e
                | ResponseData r -> data (JsonRecordElement("data", r))

            let queryInput =
                StackPanel.create
                    [ DockPanel.dock Dock.Bottom
                      DockPanel.margin (0.0, 8.0, 0.0, 0.0)
                      StackPanel.horizontalAlignment HorizontalAlignment.Center
                      StackPanel.width 420.0
                      StackPanel.orientation Orientation.Horizontal
                      StackPanel.children
                          [ TextBox.create
                                [ TextBox.minWidth 340.0
                                  TextBox.text queryInput.Current
                                  TextBox.onTextChanged queryInput.Set
                                  TextBox.watermark "Enter query..." ]
                            Button.create
                                [ Button.content "Query"
                                  Button.isEnabled (not <| System.String.IsNullOrWhiteSpace queryInput.Current)
                                  Button.onClick (fun _ -> query.Set queryInput.Current) ] ] ]

            DockPanel.create
                [ DockPanel.children [
                    queryInput
                    ScrollViewer.create [
                        ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                        ScrollViewer.content (responseView ())
                    ]
                ] ])
