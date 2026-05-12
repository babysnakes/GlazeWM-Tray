namespace GlazeWM.TrayApp.Application

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls.Notifications
open Avalonia.Themes.Fluent
open GlazeWM.Tray.Literals
open GlazeWM.TrayApp.Models
open Huskui.Avalonia
open Serilog
open GlazeWM.Tray.Models
open GlazeWM.TrayApp
open GlazeWM.TrayApp.Helpers
open GlazeWM.TrayApp.Helpers.Notifications
open GlazeWM.TrayApp.Views

type App(config: AppConfig) as this =
    inherit Application()

    let trayItem = TrayItem(config)

    let mutable connection: GlazeWMConnection option = None
    let mutable mainWindow: MainWindow option = None

    /// Toggle show/hide of the main window
    let toggleMainWindow () =
        match mainWindow with
        | None ->
            Log.Error("MainWindow is None")
            sendBugNotification "Null MainWindow"
            showErrorMessage "App Error" "MainWindow is null, please restart the application"
        | Some w when w.IsVisible |> not ->
            w.Show()

            if w.WindowState = WindowState.Minimized then
                w.WindowState <- WindowState.Normal

            w.Activate()
        | Some w -> w.Hide()

    let cleanup (tray: TrayIcon) =
        Log.Information("Shutting down...")
        connection |> Option.iter (fun c -> (c :> IDisposable).Dispose())
        tray.IsVisible <- false

    /// Resets both the `parser` and `wsClient` references and prints an error message to the user.
    let handleCommunicationError (msg: string) =
        let text =
            $"A fata error occured regarding communication with GlazeWM \n\n{msg}. \n\nCheck the logs for more \
              details. To renew communication with GlazeWM after you make sure it runs correctly, please select \
              'Reinitialize GlazeWM Connection' from the tray menu."

        showErrorMessage "GlazeWM Communication Error" text
        trayItem.OnCommunicationError()

    let handleConnectionNotification notification =
        match notification with
        | ConnectionError msg -> handleCommunicationError msg
        | Reconnected -> trayItem.Handle(SetReInitializeMenuEnabled false)
        | BugNoCurrentWorkspace -> sendBugNotification "No current workspace"
        | BugUnsetWsClient -> sendBugNotification "Unset wsClient"

    let handleAgentError (ex: Exception) =
        let text =
            $"An fatal error occured in the application's agent:\n\n{ex.Message}.\n\nPlease restart the application!"

        Log.Error(ex, "TrayIcon agent error:")
        showErrorMessage "TrayIcon Agent Error" text

    let runSyncQuery query =
        match connection with
        | Some c -> c.RunSyncQuery query
        | None -> Error "BUG: Connection is None"

    let handleTrayMenuEvent (e: MenuEvent) : Unit =
        match e with
        | ReconnectRequested -> connection |> Option.tryDo _.Reconnect()
        | RefreshRequested -> connection |> Option.tryDo _.RefreshState()
        | ToggleMainWindow -> toggleMainWindow ()
        | Notify(title, body) -> mainWindow |> Option.tryDo _.Notify(title, body)
        | SwitchWorkspace workspaceName ->
            connection
            |> Option.tryDo (fun c -> c.Send $"{CFocusWorkspacePrefix} {workspaceName.Name}")

    let agent =
        MailboxProcessor<ParsedMessages>.Start(fun inbox ->
            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Workspaces wn -> trayItem.Handle(WorkspacesChanged wn)
                    | Paused p -> trayItem.Handle(PausedChanged p)
                    | NewBindingModes cb -> trayItem.Handle(BindingModesChanged cb)
                    | UnSuccessfulResponse msg ->
                        Log.Error("Unsuccessful Response: {Message}", msg)
                        mainWindow
                        |> Option.tryDo (fun w ->
                            w.Notify("Unsuccessful Response from GlazeWM", $"{msg}", NotificationType.Error))

                    return! loop ()
                }

            loop ())

    member private _.InitGlazeConnection() =
        let connection' = new GlazeWMConnection(config, agent)
        connection <- Some connection'
        connection'.Notifications.Add(handleConnectionNotification)

        connection'.RefreshState()

    override _.Initialize() =
        this.Styles.Add(FluentTheme())
        this.Styles.Add(HuskuiTheme())
        this.Styles.Add(AppStyles())

#if DEBUG
        this.AttachDeveloperTools() |> ignore
#endif

    override _.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            // Make shut down explicit, Don't shut down when closing the main window
            desktopLifetime.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            let w = MainWindow runSyncQuery
            mainWindow <- Some w
            trayItem.Initialize()
            let tray = trayItem.Tray
            let icons = TrayIcons()
            icons.Add(tray)
            TrayIcon.SetIcons(this, icons)
            agent.Error.Add(handleAgentError)
            this.InitGlazeConnection()
            trayItem.ErrorEvent.Add handleAgentError
            trayItem.MenuEvent.Add handleTrayMenuEvent

            this.ActualThemeVariantChanged.Add(fun _ ->
                Log.Debug("Theme variant changed, new variant is {Variant}", this.ActualThemeVariant)
                // trigger recalculation of the icon
                connection |> Option.tryDo _.RefreshState())

            desktopLifetime.Exit.Add(fun _ -> cleanup tray)
            tray.IsVisible <- true

            Log.Information("Application started")

        | _ -> ()
