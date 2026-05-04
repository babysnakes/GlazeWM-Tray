namespace GlazeWM.TrayApp

open System
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Styling
open GlazeWM.Tray.Models
open GlazeWM.TrayApp.Helpers.Operations
open GlazeWM.TrayApp.Models
open Serilog

type MenuEvent =
    | ReconnectRequested
    | RefreshRequested
    | ToggleMainWindow
    | Notify of string * string
    | SwitchWorkspace of WorkspaceName

type TrayMessage =
    | WorkspacesChanged of WorkspacesNotification
    | PausedChanged of bool
    | BindingModesChanged of bool
    | ResetState
    | SetReInitializeMenuEnabled of bool

type private TrayIconState =
    { Workspaces: WorkspacesNotification
      Paused: bool
      CustomBinding: bool
      Reset: bool }

module Assets =
    let internal loadIcons () : Map<string, WindowIcon> =
        let themes = [ "w"; "b"; "g" ]

        [ yield! ([ '0' .. '9' ] @ [ 'a' .. 'z' ]) |> List.map string; "qm" ]
        |> List.collect (fun id -> themes |> List.map (fun theme -> $"icon-{id}-{theme}"))
        |> List.append [ "icon"; "error" ]
        |> List.map (fun name ->
            name, WindowIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", $"{name}.ico")))
        |> Map.ofList

type TrayItem(config: AppConfig) =

    let reInitializeMenu = NativeMenuItem(Header = "Reinitialize GlazeWM Connection")
    let menuEvent = Event<MenuEvent>()
    let errorEvent = Event<exn>()
    let statusIcons: Map<string, WindowIcon> = Assets.loadIcons ()
    let tray = new TrayIcon() // fsharplint:disable-line RedundantNewKeyword

    let mutable persistentMenuItems: NativeMenuItem seq = seq { }

    let emptyState =
        { Workspaces =
            { Current =
                { Name = "?"
                  DisplayName = "Unknown Workspace" }
              Active = [] }
          Paused = false
          CustomBinding = false
          Reset = false }

    /// One time function to populate the persistent tray menu items
    let initializePersistentMenuItems () =
        let desktopLifetime =
            Avalonia.Application.Current.ApplicationLifetime :?> IClassicDesktopStyleApplicationLifetime

        let toggleItem = NativeMenuItem(Header = "Show/Hide Main Window")
        toggleItem.Click.Add(fun _ -> menuEvent.Trigger ToggleMainWindow)

        let openLogsMenu = NativeMenuItem(Header = "Open Logs Directory")
        openLogsMenu.Click.Add(fun _ -> openDirectory config.LogsDirectory)

        let toggleDebug =
            NativeMenuItem(Header = "Verbose Logging", ToggleType = NativeMenuItemToggleType.CheckBox)

        toggleDebug.IsChecked <- false

        toggleDebug.Click.Add(fun _ ->
            if toggleDebug.IsChecked then
                config.LevelSwitch.MinimumLevel <- Events.LogEventLevel.Debug
            else
                config.LevelSwitch.MinimumLevel <- Events.LogEventLevel.Information)

        let quitItem = NativeMenuItem(Header = "Quit")
        quitItem.Click.Add(fun _ -> desktopLifetime.Shutdown(0))

        reInitializeMenu.IsEnabled <- false
        reInitializeMenu.Click.Add(fun _ ->
            Log.Information("Reinitializing GlazeWM Connection")
            menuEvent.Trigger(Notify("Reinitializing GlazeWM Connection", "Attempting to reconnect..."))
            menuEvent.Trigger ReconnectRequested)

        let refreshItem = NativeMenuItem(Header = "Refresh")
        refreshItem.Click.Add(fun _ -> menuEvent.Trigger RefreshRequested)

        persistentMenuItems <-
            seq {
                NativeMenuItemSeparator()
                openLogsMenu
                toggleDebug // Add it to your menu
                NativeMenuItemSeparator()
                reInitializeMenu
                refreshItem
                toggleItem
                quitItem
            }

    let updateTrayMenu (wss: WorkspaceName list) =
        tray.Menu.Items.Clear()

        wss
        |> List.iter (fun m ->
            let dn =
                if m.Name = m.DisplayName then
                    $"Workspace {m.Name}"
                else
                    m.DisplayName

            let item = NativeMenuItem(Header = $"{m.Name} - {dn}")
            item.Click.Add(fun _ -> menuEvent.Trigger(SwitchWorkspace m))

            tray.Menu.Items.Add(item))

        persistentMenuItems |> Seq.iter tray.Menu.Items.Add

    /// logic for matching state to icon
    let matchStateToIcon (state: TrayIconState) =
        let mode = Avalonia.Application.Current.ActualThemeVariant
        let isDarkTheme = mode = ThemeVariant.Dark
        let bw = if isDarkTheme then "w" else "b"
        let theme = if (state.Paused || state.CustomBinding) then "g" else bw

        let name =
            if state.CustomBinding then
                "qm"
            else
                state.Workspaces.Current.Name

        let key = $"icon-{name}-{theme}"
        statusIcons |> Map.tryFind key |> Option.defaultValue statusIcons["icon-qm-g"]

    let handler =
        MailboxProcessor<TrayMessage>.Start(fun inbox ->
            let rec loop (state: TrayIconState) =
                async {
                    let! msg = inbox.Receive()

                    let newState =
                        Avalonia.Threading.Dispatcher.UIThread.Invoke(fun _ ->
                            let st =
                                match msg with
                                | WorkspacesChanged wn -> { state with Workspaces = wn }
                                | PausedChanged p -> { state with Paused = p }
                                | BindingModesChanged cb -> { state with CustomBinding = cb }
                                | ResetState -> { state with Reset = true }
                                | SetReInitializeMenuEnabled b ->
                                    Log.Debug("Setting reinitialize menu enabled to {Enabled}", b)
                                    reInitializeMenu.IsEnabled <- b
                                    state

                            if st.Reset then
                                Log.Debug("Resetting tray icon state")
                                emptyState
                            else
                                if st <> state then
                                    Log.Debug("Refreshing tray icon with: {State}", st)
                                    tray.Icon <- matchStateToIcon st
                                    tray.ToolTipText <- $"Workspace {st.Workspaces.Current.DisplayName}"

                                if st.Workspaces.Active <> state.Workspaces.Active then
                                    Log.Debug("Refreshing tray Menu with: {Active}", st.Workspaces.Active)
                                    updateTrayMenu st.Workspaces.Active
                                st)

                    return! loop newState
                }

            loop emptyState)

    member _.Initialize() =
        tray.ToolTipText <- "Workspace ?"
        tray.Menu <- NativeMenu()
        initializePersistentMenuItems ()
        updateTrayMenu []
        tray.Clicked.Add(fun _ -> menuEvent.Trigger ToggleMainWindow)
        let app_icon = statusIcons |> Map.find "icon"
        tray.Icon <- app_icon
        handler.Error.Add(fun ex -> errorEvent.Trigger(ex))


    member _.Tray = tray
    member _.MenuEvent = menuEvent.Publish
    member _.ErrorEvent = errorEvent.Publish
    member _.Handle(msg: TrayMessage) = handler.Post(msg)

    member _.OnCommunicationError() =
        Avalonia.Threading.Dispatcher.UIThread.Invoke(fun _ ->
            handler.Post(ResetState)
            updateTrayMenu []
            reInitializeMenu.IsEnabled <- true
            tray.Icon <- statusIcons |> Map.find "error")
