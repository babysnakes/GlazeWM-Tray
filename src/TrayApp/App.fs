namespace GlazeWM.TrayApp.Application

open System
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Styling
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.Tray.WebSocketClient
open GlazeWM.TrayApp.Helpers
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

    let tray = new TrayIcon()
    let mutable variant: ThemeVariant = ThemeVariant.Default // we'll set it right in initialize...
    let mutable workspaceIcons: Map<string, WindowIcon> = Map.empty
    let uri = Uri("ws://localhost:6123/")
    let reconnectMenuItem = NativeMenuItem(Header = "Reconnect to GlazeWM")

    // Hold references at the class level so they aren't GC'd
    let mutable parser: Parser option = None
    let mutable wsClient: WebSocketClient option = None
    let mutable messageHandler: MailboxProcessor<ParsingOutput> option = None

    /// Enable/Disable the `reconnectMenuItem`
    let setReconnectMenuEnabled (state: bool) =
        Avalonia.Threading.Dispatcher.UIThread.Post(fun () -> reconnectMenuItem.IsEnabled <- state)

    /// logic for matching state to icon
    let matchStateToIcon (state: TrayIconState) =
        let bw = if variant = ThemeVariant.Dark then "w" else "b"
        let theme = if (state.Paused || state.CustomBinding) then "g" else bw
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

    let trayUpdater () =
        MailboxProcessor<ParsingOutput>.Start(fun inbox ->
            let rec loop (state: TrayIconState) (iVariant: ThemeVariant) =
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

                                if st <> state || iVariant <> variant then
                                    tray.Icon <- matchStateToIcon st
                                    tray.ToolTipText <- $"Workspace {st.Workspace.Name}"

                                st)
                            .GetTask()
                         |> Async.AwaitTask)

                    return! loop newState variant
                }

            let defaultState =
                { Workspace =
                    { Name = "?"
                      DisplayName = "Unknown Workspace" }
                  Paused = false
                  CustomBinding = false }

            loop defaultState variant)

    let cleanup (tray: TrayIcon) =
        Log.Information("Shutting down...")
        wsClient |> Option.iter (fun c -> (c :> IDisposable).Dispose())
        tray.IsVisible <- false

    /// Resets both the `parser` and `wsClient` references and prints an error message to the user.
    let handleCommunicationError (msg: string) =
        let text =
            $"A fata error occured regarding communication with GlazeWM ({msg}). Check the logs for more details.\
              To renew communication with GlazeWM, please select 'Reinitialize GlazeWM Connection' from the tray menu."

        setReconnectMenuEnabled true
        showErrorMessage "GlazeWM Communication Error" text
        parser <- None
        wsClient <- None

    let handleMessageParserEvent (msg: MessageParserEvent) =
        match msg with
        | ParseError s -> Log.Error("A parser exception had occured: {Err}", s)
        | NoCurrentWorkspace -> sendBugNotification NoCurrentWorkspace
        | UnsetWsClient -> sendBugNotification UnsetWsClient
        | AgentError ex ->
            Log.Error(ex, "MessageParser agent error:")
            handleCommunicationError $"MessageParser: {ex.Message}"

    let handleWsClientEvent (msg: string) =
        Log.Error("A websocket client exception had occured: {Err}", msg)
        handleCommunicationError msg

    let initGlazeConnection () =
        let agent = trayUpdater ()
        let parser' = Parser(agent)
        let client = new WebSocketClient(uri, parser'.Dispatcher())
        parser'.SetWsClient client.Agent
        parser <- Some parser'
        wsClient <- Some client
        messageHandler <- Some agent
        // If we're connected, we can disable the reconnection menu item
        setReconnectMenuEnabled false

        client.Error.Add(handleWsClientEvent)
        parser'.Error.Add(handleMessageParserEvent)
        client.InitializeSubscription()

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

        reconnectMenuItem.Click.Add(fun _ ->
            sendNotification "Reinitializing GlazeWM Connection" "Attempting to reconnect..."
            initGlazeConnection ())

        let refreshItem = NativeMenuItem(Header = "Refresh")
        refreshItem.Click.Add(fun _ -> wsClient |> Option.tryDo (fun c -> c.RefreshState()))

        let menu = NativeMenu()
        menu.Items.Add(showHideItem)
        menu.Items.Add(NativeMenuItemSeparator())
        menu.Items.Add(openLogsMenu)
        menu.Items.Add(toggleDebug) // Add it to your menu
        menu.Items.Add(NativeMenuItemSeparator())
        menu.Items.Add(reconnectMenuItem)
        menu.Items.Add(refreshItem)
        menu.Items.Add(quitItem)
        menu

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        variant <- this.ActualThemeVariant
        workspaceIcons <- Assets.loadIcons ()

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            // Make shut down explicit, Don't shut down when closing the main window
            desktopLifetime.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            Log.Information("Application started")

            let menu = this.MkMenu desktopLifetime
            tray.ToolTipText <- "Workspace ?"
            tray.Menu <- menu
            tray.Clicked.Add(fun _ -> toggleMainWindow desktopLifetime)
            // TODO: replace with default app icon
            let app_icon = WindowIcon(System.IO.Path.Combine("Assets", "icon-qm-w.ico"))
            tray.Icon <- app_icon
            let icons = TrayIcons()
            icons.Add(tray)
            TrayIcon.SetIcons(this, icons)
            initGlazeConnection ()

            this.ActualThemeVariantChanged.Add(fun _ ->
                Log.Debug("Theme variant changed, new variant is {Variant}", variant)
                variant <- this.ActualThemeVariant
                // trigger recalculation of the icon
                wsClient |> Option.tryDo (fun c -> c.RefreshState()))

            desktopLifetime.Exit.Add(fun _ -> cleanup tray)

            tray.IsVisible <- true

        | _ -> ()
