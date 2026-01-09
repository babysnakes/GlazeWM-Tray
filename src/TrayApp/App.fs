namespace GlazeWM.TrayApp.Application

open System
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.Tray.WebSocketClient
open GlazeWM.TrayApp.Helpers.Notifications
open Serilog
open Serilog.Core

module Main =

    let view () =
        Component(fun _ ->

            DockPanel.create
                [ DockPanel.children
                      [ TextBlock.create
                            [ TextBlock.dock Dock.Top
                              TextBlock.fontSize 48.0
                              TextBlock.verticalAlignment VerticalAlignment.Center
                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                              TextBlock.text "GlazeWM Tray DEV" ] ] ])

type MainWindow() =
    inherit HostWindow()

    do
        base.Title <- "GlazeWM Tray"
        base.Width <- 600
        base.Height <- 400
        base.Content <- Main.view ()

    override this.OnClosing(e: WindowClosingEventArgs) =
        this.Hide()
        e.Cancel <- true
        base.Hide()

type private TrayIconState =
    { Workspace: WorkspaceName
      Paused: bool
      CustomBinding: bool }

module Assets =
    let internal loadIcons () : Map<string, WindowIcon> =
        let ids = [ yield! [ 0..9 ] |> List.map string; "qm" ]
        let themes = [ "w"; "b"; "g" ]

        [ for id in ids do
              for theme in themes -> $"icon-{id}-{theme}" ]
        |> List.map (fun name -> name, WindowIcon(System.IO.Path.Combine("Assets", $"{name}.ico")))
        |> Map.ofList

type App(levelSwitch: LoggingLevelSwitch, logDir: string) =
    inherit Application()

    let mutable workspaceIcons: Map<string, WindowIcon> = Map.empty
    let uri = Uri("ws://localhost:6123/")

    // Hold references at the class level so they aren't GC'd
    let mutable parser: Parser option = None
    let mutable wsClient: WebSocketClient option = None
    let mutable messageHandler: MailboxProcessor<ParsingOutput> option = None

    /// logic for matching state to icon
    let matchStateToIcon (state: TrayIconState) =
        let theme = if (state.Paused || state.CustomBinding) then "g" else "w"
        let name = if state.CustomBinding then "qm" else state.Workspace.Name
        let key = $"icon-{name}-{theme}"

        workspaceIcons
        |> Map.tryFind key
        |> Option.defaultValue workspaceIcons["icon-qm-w"]

    /// Toggle show/hide of the main window
    let toggleMainWindow (desktopLifetime: IClassicDesktopStyleApplicationLifetime) =
        match desktopLifetime.MainWindow with
        | null ->
            let w = MainWindow()
            desktopLifetime.MainWindow <- w
            w.Show()
        | w when w.IsVisible |> not ->
            w.Show()

            if w.WindowState = WindowState.Minimized then
                w.WindowState <- WindowState.Normal

            w.Activate()
        | w -> w.Hide()

    let trayUpdater (tray: TrayIcon) =
        MailboxProcessor<ParsingOutput>.Start(fun inbox ->
            let rec loop (state: TrayIconState) =
                async {
                    let! msg = inbox.Receive()

                    let! newState =
                        (Avalonia.Threading.Dispatcher.UIThread
                            .InvokeAsync(fun () ->
                                let st =
                                    match msg with
                                    | CurrentWorkspace wn -> { state with Workspace = wn }
                                    | Paused p -> { state with Paused = p }
                                    | NewBindingModes cb -> { state with CustomBinding = cb }
                                    | UnSuccessfulResponse msg ->
                                        sendNotification "Unsuccessful Response from GlazeWM" $"{msg}"
                                        state

                                if st <> state then
                                    tray.Icon <- matchStateToIcon st
                                    tray.ToolTipText <- $"Workspace {st.Workspace.Name}"

                                st)
                            .GetTask()
                         |> Async.AwaitTask)

                    return! loop newState
                }

            let defaultState =
                { Workspace =
                    { Name = "?"
                      DisplayName = "Unknown Workspace" }
                  Paused = false
                  CustomBinding = false }

            loop defaultState)

    let cleanup (tray: TrayIcon) =
        Log.Information("Shutting down...")
        wsClient |> Option.iter (fun c -> (c :> IDisposable).Dispose())
        tray.IsVisible <- false

    let initGlazeConnection (ti: TrayIcon) =
        let agent = trayUpdater ti
        let parser' = Parser(agent)
        let client = new WebSocketClient(uri, parser'.Dispatcher())
        parser'.SetWsClient client.Agent
        parser <- Some parser'
        wsClient <- Some client
        messageHandler <- Some agent

        // TODO: Improve handling
        client.Error.Add(fun msg -> Log.Error("Error occurred in websocket client: {Message}", msg))
        parser'.Error.Add(fun msg -> Log.Error("Error occurred in message parser: {Message}", msg))

        [ "sub -e workspace_updated workspace_activated workspace_deactivated binding_modes_changed pause_changed focus_changed"
          "query workspaces" ]
        |> List.map (SendMessage >> client.Agent.Post)
        |> ignore

    let openLogsDir _ =
        let startInfo = System.Diagnostics.ProcessStartInfo(logDir)
        startInfo.UseShellExecute <- true
        System.Diagnostics.Process.Start(startInfo) |> ignore

    member private this.MkMenu(desktopLifetime: IClassicDesktopStyleApplicationLifetime) =
        let showHideItem = NativeMenuItem(Header = "Show/Hide Window")
        showHideItem.Click.Add(fun _ -> toggleMainWindow desktopLifetime)

        let openLogsMenu = NativeMenuItem(Header = "Open Logs Directory")
        openLogsMenu.Click.Add(openLogsDir)

        let toggleDebug =
            NativeMenuItem(Header = "Verbose Logging", ToggleType = NativeMenuItemToggleType.CheckBox)

        toggleDebug.IsChecked <- false

        toggleDebug.Click.Add(fun _ ->
            if toggleDebug.IsChecked then
                levelSwitch.MinimumLevel <- Events.LogEventLevel.Debug
            else
                levelSwitch.MinimumLevel <- Events.LogEventLevel.Information)

        let quitItem = NativeMenuItem(Header = "Quit")

        quitItem.Click.Add(fun _ ->
            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as dl -> dl.Shutdown(0)
            | _ -> ())

        let menu = NativeMenu()
        menu.Items.Add(showHideItem)
        menu.Items.Add(NativeMenuItemSeparator())
        menu.Items.Add(openLogsMenu)
        menu.Items.Add(toggleDebug) // Add it to your menu
        menu.Items.Add(NativeMenuItemSeparator())
        menu.Items.Add(quitItem)
        menu

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark
        workspaceIcons <- Assets.loadIcons ()

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            // Make shut down explicit, Don't shut down when closing the main window
            desktopLifetime.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            Log.Information("Application started")

            let menu = this.MkMenu desktopLifetime
            let tray = new TrayIcon()
            tray.ToolTipText <- "Workspace ?"
            tray.Menu <- menu
            tray.Clicked.Add(fun _ -> toggleMainWindow desktopLifetime)
            // TODO: replace with default app icon
            let app_icon = WindowIcon(System.IO.Path.Combine("Assets", "icon-qm-w.ico"))
            tray.Icon <- app_icon
            let icons = TrayIcons()
            icons.Add(tray)
            TrayIcon.SetIcons(this, icons)
            initGlazeConnection tray
            desktopLifetime.Exit.Add(fun _ -> cleanup tray)

            tray.IsVisible <- true

        | _ -> ()
