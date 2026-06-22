namespace GlazeWM.TrayApp.Views

open System
open System.Reactive.Linq
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.TrayApp.Helpers.Notifications
open GlazeWM.TrayApp.Helpers.TextHelpers
open GlazeWM.TrayApp.Icons
open GlazeWM.TrayApp.Models
open GlazeWM.TrayApp.Views.SharedViews

module WindowsPanel =
    open FsToolkit.ErrorHandling.Operator.Result

    type private State =
        | ResponseData of Workspace list
        | ErrorMsg of string
        | Querying

    let windowPanel title value row : IView list =
        [ TextBlock.create
              [ TextBlock.text title
                TextBlock.row row
                TextBlock.column 0
                TextBlock.fontWeight Avalonia.Media.FontWeight.Bold ]
          TextBlock.create
              [ TextBlock.text value
                TextBlock.row row
                TextBlock.column 1
                TextBlock.padding (8, 3) ] ]

    let private mkToolTip (w: Window) =
        [ Some $"ID: {w.Id}"
          Some $"Title: {w.Title}"
          Some $"Process Name: {w.ProcessName}"
          Some $"State: {w.State}"
          w.ClassName |> Option.map (fun c -> $"Class: {c}")
          Some $"Size: {w.Width}x{w.Height}"
          Some $"Placement: {w.X}x{w.Y}" ]
        |> List.choose id
        |> fun lines -> String.Join(Environment.NewLine, lines)

    let private windowCard (lastSync: IWritable<DateTime>) (vh: IViewsHelpers) (w: Window) : IView =
        Component.create (
            string w.Id,
            fun ctx ->
                let lastSync = ctx.usePassed lastSync
                Border.create
                    [ Border.classes [ "window-card" ]
                      Border.cornerRadius 6
                      Border.padding 8
                      Border.child (
                          Grid.create
                              [ Grid.horizontalAlignment HorizontalAlignment.Left
                                Grid.name $"window-panel-id-{w.Id}"
                                Grid.columnDefinitions "Auto,Auto"
                                Grid.rowDefinitions "Auto,Auto,Auto"
                                Grid.children (
                                    [ windowPanel "Title" w.Title 0
                                      windowPanel "Process" w.ProcessName 1
                                      windowPanel "Size" $"{w.Width}x{w.Height}  [{w.State}]" 2 ]
                                    |> List.concat
                                ) ]
                      )
                      ToolTip.tip (mkToolTip w)
                      Border.contextMenu (
                          ContextMenu.create
                              [ ContextMenu.viewItems
                                    [ MenuItem.create
                                          [ MenuItem.header "Copy"
                                            ToolTip.tip "Copy window data to clipboard"
                                            MenuItem.onClick (fun _ ->
                                                let topLevel = TopLevel.GetTopLevel ctx.control
                                                let data = mkToolTip w
                                                topLevel.Clipboard.SetTextAsync(data) |> Async.AwaitTask |> ignore) ]
                                      MenuItem.create
                                          [ MenuItem.header "Ignore"
                                            ToolTip.tip "Ignore this specific window"
                                            MenuItem.onClick (fun _ ->
                                                async {
                                                    let owner =
                                                        TopLevel.GetTopLevel ctx.control :?> Avalonia.Controls.Window
                                                    let! confirmed =
                                                        confirmDialog
                                                            owner
                                                            $"Ignore window '{w.Title}'?\n\n This will take affect until you close the window or restart GlazeWM."
                                                        |> Async.AwaitTask
                                                    if confirmed then
                                                        async {
                                                            vh.RunSyncQuery $"command --id {w.Id} ignore"
                                                            |> Result.bind CustomParsers.parseSuccess
                                                            |> notifyIfError "Error ignoring window" ctx
                                                            // reset list to avoid confusion
                                                            lastSync.Set DateTime.Now
                                                        }
                                                        |> Async.Start
                                                }
                                                |> Async.StartImmediate) ] ] ]
                      ) ]
        )


    let private workspaceExpander filter lastSync state vh (initial: Workspace) : IView =
        Component.create (
            string initial.Id,
            fun ctx ->
                let lastSync = ctx.usePassed lastSync
                let filter = ctx.usePassedRead filter
                let state = ctx.usePassedRead state
                let ws = ctx.useState initial // check comment on useEffect for reason
                let windows =
                    ws.Current.Children
                    |> List.choose (Workspaces.filterWindow filter.Current)
                    |> List.map (windowCard lastSync vh)
                let windowsCount = List.length ws.Current.Children
                let filteredWindowsCount = List.length windows
                let filterHeader =
                    if windowsCount <> filteredWindowsCount then
                        $" - showing {filteredWindowsCount} filtered "
                        + (pluralize "window" filteredWindowsCount)
                        + " out of {windowsCount}"
                    else
                        $" - {windowsCount} " + (pluralize "window" windowsCount)

                // This solves a bug that because of race condition, the panel does not refresh if the actual
                // data is not saved in a context state.
                ctx.useEffect (
                    handler =
                        (fun _ ->
                            match state.Current with
                            | ResponseData workspaces ->
                                workspaces |> List.tryFind (fun w -> w.Id = initial.Id) |> Option.iter ws.Set
                            | _ -> ()),
                    triggers = [ EffectTrigger.AfterChange state ]
                )

                Expander.create
                    [ Expander.isEnabled (filteredWindowsCount > 0)
                      Expander.header (
                          ws.Current.DisplayName
                          |> Option.defaultValue $"Workspace {ws.Current.Name}{filterHeader}"
                      )
                      Expander.padding 0
                      Expander.content (
                          Border.create
                              [ Border.classes [ "workspace-content" ]
                                Border.padding 12
                                Border.child (
                                    StackPanel.create
                                        [ StackPanel.classes [ "workspace-content" ]
                                          StackPanel.spacing 12
                                          StackPanel.children windows ]
                                ) ]
                      ) ]
        )

    let view (vh: IViewsHelpers) =
        Component(fun ctx ->
            let state = ctx.useState (State.ResponseData List.empty)
            let lastSync = ctx.useState DateTime.Now
            let filter = ctx.useState ""
            use filterSubject = ctx.useState (new Subject<string>())

            ctx.useEffect (
                handler =
                    (fun _ ->
                        state.Set State.Querying

                        async {
                            // jkk state.Set State.Querying
                            match vh.RunSyncQuery "query workspaces" >>= CustomParsers.parseWorkspaces with
                            | Ok m -> state.Set(State.ResponseData m)
                            | Error e -> state.Set(State.ErrorMsg e)
                        }
                        |> Async.Start),
                triggers = [ EffectTrigger.AfterInit; EffectTrigger.AfterChange lastSync ]
            )

            ctx.useEffect (
                handler =
                    (fun _ ->
                        filterSubject.Current
                            .AsObservable()
                            .Throttle(TimeSpan.FromMilliseconds 300.0)
                            .Subscribe(filter.Set)
                        |> ignore),
                triggers = [ EffectTrigger.AfterInit ]
            )

            let filterInput =
                Grid.create
                    [ DockPanel.dock Dock.Bottom
                      Grid.margin (50.0, 8.0, 50.0, 0.0)
                      Grid.columnDefinitions "*, Auto"
                      Grid.minWidth 200.0
                      Grid.maxWidth 600.0
                      Grid.children
                          [ TextBox.create
                                [ Grid.column 0
                                  TextBox.name "windows-filter"
                                  TextBox.minWidth 200.0
                                  TextBox.onTextChanged filterSubject.Current.OnNext
                                  TextBox.watermark "Filter windows by title/process name..." ]
                            StackPanel.create
                                [ Grid.column 1
                                  StackPanel.orientation Orientation.Horizontal
                                  StackPanel.children
                                      [ Button.create
                                            [ Button.name "query-button"
                                              Button.margin (4.0, 0.0, 0.0, 0.0)
                                              Button.content (pathIcon refreshIcon)
                                              ToolTip.tip $"Refresh windows list (last refresh: {lastSync.Current}"
                                              Button.isEnabled state.Current.IsResponseData
                                              Button.onClick (fun _ -> lastSync.Set(DateTime.Now)) ] ] ] ] ]


            let renderWorkspaces (ws: Workspace list) : IView =
                StackPanel.create
                    [ StackPanel.orientation Orientation.Vertical
                      StackPanel.children (List.map (workspaceExpander filter lastSync state vh) ws) ]

            let renderPanel () =
                match state.Current with
                | ResponseData ws -> renderWorkspaces ws
                | Querying -> simplePanel "Waiting for response..."
                | ErrorMsg e -> error e


            DockPanel.create
                [ DockPanel.children [ filterInput; ScrollViewer.create [ ScrollViewer.content (renderPanel ()) ] ] ])
