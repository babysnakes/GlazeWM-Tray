namespace GlazeWM.TrayApp.Views

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open FSharp.Data
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.TrayApp.Icons
open GlazeWM.TrayApp.Models
open Serilog
open GlazeWM.TrayApp.Views.SharedViews

module QueryPanel =
    open FsToolkit.ErrorHandling.Operator.Result

    type private State =
        | ResponseData of JsonValue
        | ErrorMsg of string
        | Querying
        | Empty

    type private JsonItem = JsonItem of name: string * value: JsonValue

    let view (vh: IViewsHelpers) =
        Component(fun ctx ->
            let state = ctx.useState State.Empty
            let query = ctx.useState ""
            let queryInput = ctx.useState ""
            let collapseKey = ctx.useState 0 // A key to increment to force to redraw the TreeView
            let copied = ctx.useState false
            let copyBtnIcon = if copied.Current then checkIcon else copyIcon

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
                                match vh.RunSyncQuery queryText >>= CustomParsers.tryExtractResponseData with
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

            let canQuery =
                not <| System.String.IsNullOrWhiteSpace queryInput.Current
                && state.Current <> Querying

            let copyStateToClipboard _ =
                let top = TopLevel.GetTopLevel ctx.control
                match state.Current with
                | ResponseData r ->
                    let data = r.ToString(JsonSaveOptions.None)
                    top.Clipboard.SetTextAsync(data) |> Async.AwaitTask |> ignore
                    copied.Set true
                    async {
                        do! Async.Sleep 1500
                        copied.Set false
                    }
                    |> Async.Start
                | _ -> Log.Warning("Copy on non json data")

            let itemsSelector (JsonItem(_, v)) : JsonItem seq =
                match v with
                | JsonValue.Array a -> a |> Seq.indexed |> Seq.map (fun (i, v) -> JsonItem($"{i}", v))
                | JsonValue.Record r -> r |> Seq.map JsonItem
                | _ -> Array.empty

            let treeView (JsonItem(name, value)) =
                TextBlock.create
                    [ match value with
                      | JsonValue.String s -> TextBlock.text $"{name} :  {s}"
                      | JsonValue.Number n -> TextBlock.text $"{name} :  {n}"
                      | JsonValue.Float f -> TextBlock.text $"{name} :  {f}"
                      | JsonValue.Boolean b -> TextBlock.text $"{name} :  {b}"
                      | JsonValue.Null -> TextBlock.text $"{name} :  null"
                      | JsonValue.Record _ -> TextBlock.text $"{name}"
                      | JsonValue.Array _ -> TextBlock.text $"{name}" ]


            let data (json: JsonItem) : IView =
                Component.create (
                    $"treeview-{collapseKey.Current}",
                    fun _ ->
                        TreeView.create
                            [ TreeView.dataItems [ json ]
                              TreeView.itemTemplate (DataTemplateView<JsonItem>.create (itemsSelector, treeView)) ]
                )
            let responseView () =
                match state.Current with
                | Empty -> simplePanel "Please enter a query."
                | Querying -> simplePanel "Waiting for response..."
                | ErrorMsg e -> error e
                | ResponseData r -> data (JsonItem("data", r))

            let queryInput =
                Grid.create
                    [ DockPanel.dock Dock.Bottom
                      Grid.margin (50.0, 8.0, 50.0, 0.0)
                      Grid.columnDefinitions "*, Auto"
                      Grid.minWidth 200.0
                      Grid.maxWidth 600.0
                      Grid.children
                          [ TextBox.create
                                [ Grid.column 0
                                  TextBox.name "query-input"
                                  TextBox.minWidth 200.0
                                  TextBox.text queryInput.Current
                                  TextBox.onTextChanged queryInput.Set
                                  TextBox.watermark "Enter query..." ]
                            StackPanel.create
                                [ Grid.column 1
                                  StackPanel.orientation Orientation.Horizontal
                                  StackPanel.children
                                      [ Button.create
                                            [ Button.name "query-button"
                                              Button.margin (4.0, 0.0, 0.0, 0.0)
                                              Button.content "Query"
                                              ToolTip.tip "Execute query"
                                              Button.isEnabled canQuery
                                              Button.onClick (fun _ -> query.Set queryInput.Current) ]
                                        Button.create
                                            [ Button.name "fold-button"
                                              Button.margin (8.0, 0.0, 0.0, 0.0)
                                              Button.content (pathIcon collapseIcon)
                                              ToolTip.tip "Collapse all folds"
                                              Button.isEnabled state.Current.IsResponseData
                                              Button.onClick (fun _ -> collapseKey.Set(collapseKey.Current + 1)) ]
                                        Button.create
                                            [ Button.name "copy-button"
                                              Button.margin (4.0, 0.0, 0.0, 0.0)
                                              Button.content (pathIcon copyBtnIcon)
                                              ToolTip.tip "Copy formated JSON to clipboard"
                                              Button.isEnabled state.Current.IsResponseData
                                              Button.onClick copyStateToClipboard ] ] ] ] ]

            DockPanel.create
                [ DockPanel.children
                      [ queryInput
                        ScrollViewer.create
                            [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                              ScrollViewer.content (responseView ()) ] ] ])
