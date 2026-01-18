namespace GlazeWM.TrayApp.Application

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Styling
open Avalonia.Themes.Fluent
open Serilog
open Serilog.Core
open GlazeWM.Tray.Literals
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.Tray.WebSocketClient
open GlazeWM.TrayApp.Helpers
open GlazeWM.TrayApp.Helpers.Notifications

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
    { Workspaces: WorkspacesNotification
      Paused: bool
      CustomBinding: bool
      Refresh: bool }

module Assets =
    let internal loadIcons () : Map<string, WindowIcon> =
        let ids = [ yield! [ 0..9 ] |> List.map string; "qm" ]
        let themes = [ "w"; "b"; "g" ]

        [ for id in ids do
              for theme in themes -> $"icon-{id}-{theme}" ]
        |> List.append [ "icon"; "error" ]
        |> List.map (fun name -> name, WindowIcon(System.IO.Path.Combine("Assets", $"{name}.ico")))
        |> Map.ofList

type App(levelSwitch: LoggingLevelSwitch, logDir: string) as this =
    inherit Application()

    let uri = Uri("ws://localhost:6123/")
    let StatusIcons: Map<string, WindowIcon> = Assets.loadIcons ()
    let tray = new TrayIcon()
    let mutable disconnected: bool = false
    let mutable parser: Parser option = None // Just to avoid GC on MessageParser
    let mutable wsClient: WebSocketClient option = None

    let emptyState =
        { Workspaces =
            { Current =
                { Name = "?"
                  DisplayName = "Unknown Workspace" }
              Active = [] }
          Paused = false
          CustomBinding = false
          Refresh = false }

    /// logic for matching state to icon
    let matchStateToIcon (state: TrayIconState) =
        let isDarkTheme = this.ActualThemeVariant = ThemeVariant.Dark
        let bw = if isDarkTheme then "w" else "b"
        let theme = if (state.Paused || state.CustomBinding) then "g" else bw

        let name =
            if state.CustomBinding then
                "qm"
            else
                state.Workspaces.Current.Name

        let key = $"icon-{name}-{theme}"
        StatusIcons |> Map.tryFind key |> Option.defaultValue StatusIcons["icon-qm-g"]

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

    let cleanup (tray: TrayIcon) =
        Log.Information("Shutting down...")
        wsClient |> Option.iter (fun c -> (c :> IDisposable).Dispose())
        tray.IsVisible <- false

    /// Resets both the `parser` and `wsClient` references and prints an error message to the user.
    let handleCommunicationError (msg: string) =
        let text =
            $"A fata error occured regarding communication with GlazeWM ({msg}). Check the logs for more details.\
              To renew communication with GlazeWM, please select 'Reinitialize GlazeWM Connection' from the tray menu."

        showErrorMessage "GlazeWM Communication Error" text
        Log.Error("A websocket client exception had occured: {Err}", msg)
        disconnected <- true

        Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
            this.updateTrayMenu []
            tray.Icon <- StatusIcons |> Map.find "error")

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

    let openLogsDir _ =
        let startInfo = System.Diagnostics.ProcessStartInfo(logDir)
        startInfo.UseShellExecute <- true
        System.Diagnostics.Process.Start(startInfo) |> ignore

    let agent =
        MailboxProcessor<AppNotification>.Start(fun inbox ->
            let rec loop (state: TrayIconState) =
                async {
                    let! msg = inbox.Receive()

                    let! newState =
                        (Avalonia.Threading.Dispatcher.UIThread
                            .InvokeAsync(fun () ->
                                let st =
                                    match msg with
                                    | Workspaces wn -> { state with Workspaces = wn }
                                    | RefreshState ->
                                        wsClient
                                        |> Option.tryDo (fun c ->
                                            [ QWorkspaces; QBinding; QPaused ] |> List.iter c.SendMessage)

                                        { state with Refresh = true }
                                    | Paused p -> { state with Paused = p }
                                    | NewBindingModes cb -> { state with CustomBinding = cb }
                                    | UnSuccessfulResponse msg ->
                                        sendNotification "Unsuccessful Response from GlazeWM" $"{msg}"
                                        state

                                if st.Refresh then
                                    emptyState
                                else
                                    if st <> state then
                                        Log.Debug("Refreshing tray icon with: {State}", st)
                                        tray.Icon <- matchStateToIcon st
                                        tray.ToolTipText <- $"Workspace {st.Workspaces.Current.DisplayName}"

                                    if st.Workspaces.Active <> state.Workspaces.Active then
                                        Log.Debug("Refreshing tray Menu with: {Active}", st.Workspaces.Active)
                                        this.updateTrayMenu st.Workspaces.Active

                                    st)
                            .GetTask()
                         |> Async.AwaitTask)

                    return! loop newState
                }

            loop emptyState)

    member private _.initGlazeConnection() =
        let parser' = Parser(agent)
        let client = new WebSocketClient(uri, parser'.Dispatcher())
        parser'.SetWsClient client.Agent
        parser <- Some parser'
        wsClient <- Some client
        // If we're connected, we can disable the reconnection menu item
        disconnected <- false

        client.Error.Add(handleCommunicationError)
        parser'.Error.Add(handleMessageParserEvent)

        $"sub -e {SWorkspaceUP} {SWorkspaceACT} {SWorkspaceDeACT} {SBindingModesCH} {SPauseCH} {SFocusCH}"
        |> client.SendMessage

        agent.Post RefreshState

    member private _.updateTrayMenu(wss: WorkspaceName list) =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            let menu = NativeMenu()
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
            quitItem.Click.Add(fun _ -> desktopLifetime.Shutdown(0))

            let rmi = NativeMenuItem(Header = "Reinitialize GlazeWM Connection")
            rmi.IsEnabled <- disconnected

            rmi.Click.Add(fun _ ->
                sendNotification "Reinitializing GlazeWM Connection" "Attempting to reconnect..."
                this.initGlazeConnection ())

            let refreshItem = NativeMenuItem(Header = "Refresh")
            refreshItem.Click.Add(fun _ -> agent.Post RefreshState)

            wss
            |> List.iter (fun m ->
                let dn =
                    if m.Name = m.DisplayName then
                        $"Workspace {m.Name}"
                    else
                        m.DisplayName

                let item = NativeMenuItem(Header = $"{m.Name} - {dn}")

                item.Click.Add(fun _ ->
                    wsClient
                    |> Option.tryDo (fun c -> $"{CFocusWorkspacePrefix} {m.Name}" |> SendMessage |> c.Agent.Post))

                menu.Items.Add(item))

            menu.Items.Add(NativeMenuItemSeparator())
            menu.Items.Add(openLogsMenu)
            menu.Items.Add(toggleDebug) // Add it to your menu
            menu.Items.Add(NativeMenuItemSeparator())
            menu.Items.Add(rmi)
            menu.Items.Add(showHideItem)
            menu.Items.Add(refreshItem)
            menu.Items.Add(quitItem)
            tray.Menu <- menu
        | _ -> ()

    override _.Initialize() = this.Styles.Add(FluentTheme())

    override _.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            // Make shut down explicit, Don't shut down when closing the main window
            desktopLifetime.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            tray.ToolTipText <- "Workspace ?"
            this.updateTrayMenu []
            // tray.Clicked.Add(fun _ -> toggleMainWindow desktopLifetime) // there's noting there currently ...
            let app_icon = StatusIcons |> Map.find "icon"
            tray.Icon <- app_icon
            let icons = TrayIcons()
            icons.Add(tray)
            TrayIcon.SetIcons(this, icons)
            // TODO: listen to agent's errors?
            this.initGlazeConnection ()

            this.ActualThemeVariantChanged.Add(fun _ ->
                Log.Debug("Theme variant changed, new variant is {Variant}", this.ActualThemeVariant)
                // trigger recalculation of the icon
                agent.Post RefreshState)

            desktopLifetime.Exit.Add(fun _ -> cleanup tray)
            tray.IsVisible <- true

            Log.Information("Application started")

        | _ -> ()
