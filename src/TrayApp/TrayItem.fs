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

type TrayIconState =
    { Workspaces: WorkspacesNotification
      Paused: bool
      CustomBinding: bool }

    static member empty =
        { Workspaces =
            { Current =
                { Name = "?"
                  DisplayName = "Unknown Workspace" }
              Active = [] }
          Paused = false
          CustomBinding = false }


module Assets =
    let internal loadIcons () : Map<string, WindowIcon> =
        let themes = [ "w"; "b"; "g" ]

        [ yield! ([ '0' .. '9' ] @ [ 'a' .. 'z' ]) |> List.map string; "qm" ]
        |> List.collect (fun id -> themes |> List.map (fun theme -> $"icon-{id}-{theme}"))
        |> List.append [ "icon"; "error" ]
        |> List.map (fun name ->
            name, WindowIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", $"{name}.ico")))
        |> Map.ofList

type TrayItem(runtimeEnv: RuntimeEnvironment) =

    let reInitializeMenu = NativeMenuItem(Header = "Reinitialize GlazeWM Connection")
    let menuEvent = Event<MenuEvent>()
    let statusIcons: Map<string, WindowIcon> = Assets.loadIcons ()
    let tray = new TrayIcon()

    let mutable persistentMenuItems: NativeMenuItem seq = seq { }

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

    /// One-time function to populate the persistent tray menu items
    member private _.InitializePersistentMenuItems() =
        let desktopLifetime =
            Avalonia.Application.Current.ApplicationLifetime :?> IClassicDesktopStyleApplicationLifetime

        let toggleItem = NativeMenuItem(Header = "Show/Hide Main Window")
        toggleItem.Click.Add(fun _ -> menuEvent.Trigger ToggleMainWindow)

        let openLogsMenu = NativeMenuItem(Header = "Open Logs Directory")
        openLogsMenu.Click.Add(fun _ -> openDirectory runtimeEnv.Config.LogsDirectory)

        let toggleDebug =
            NativeMenuItem(Header = "Verbose Logging", ToggleType = NativeMenuItemToggleType.CheckBox)

        toggleDebug.IsChecked <- (runtimeEnv.LevelSwitch.MinimumLevel = Events.LogEventLevel.Debug)

        toggleDebug.Click.Add(fun _ ->
            if toggleDebug.IsChecked then
                runtimeEnv.LevelSwitch.MinimumLevel <- Events.LogEventLevel.Debug
            else
                runtimeEnv.LevelSwitch.MinimumLevel <- Events.LogEventLevel.Information)

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

    member private _.UpdateTrayMenu(wss: WorkspaceName list) =
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

    /// Modify the tray icon to match messages from GlazeWM. Assumes already running on AvaloniaUI thread.
    member this.Handle (state: TrayIconState) (msg: ParsedMessage) : TrayIconState =
        let newState =
            match msg with
            | Workspaces wm -> { state with Workspaces = wm }
            | Paused p -> { state with Paused = p }
            | NewBindingModes nb -> { state with CustomBinding = nb }

        if newState <> state then
            Log.Debug("Refreshing tray icon with: {State}", newState)
            tray.Icon <- matchStateToIcon newState
            tray.ToolTipText <- $"Workspace {newState.Workspaces.Current.DisplayName}"
        if newState.Workspaces.Active <> state.Workspaces.Active then
            Log.Debug("Refreshing tray Menu with: {Active}", newState.Workspaces.Active)
            this.UpdateTrayMenu newState.Workspaces.Active

        newState

    member this.Initialize() =
        tray.ToolTipText <- "Workspace ?"
        tray.Menu <- NativeMenu()
        this.InitializePersistentMenuItems()
        this.UpdateTrayMenu []
        tray.Clicked.Add(fun _ -> menuEvent.Trigger ToggleMainWindow)
        let app_icon = statusIcons |> Map.find "icon"
        tray.Icon <- app_icon


    member _.Tray = tray
    member _.MenuEvent = menuEvent.Publish

    member this.OnConnectionError() =
        this.UpdateTrayMenu []
        reInitializeMenu.IsEnabled <- true
        tray.Icon <- statusIcons |> Map.find "error"

    member _.OnConnectionRestored() = reInitializeMenu.IsEnabled <- false
