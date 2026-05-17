namespace GlazeWM.TrayApp.Application

open System
open System.Reactive.Concurrency
open System.Reactive.Linq
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls.Notifications
open Avalonia.Themes.Fluent
open FSharp.Control.Reactive.Disposables
open FSharp.Control.Reactive.Observable
open GlazeWM.Tray.Literals
open GlazeWM.Tray.MessageParser
open GlazeWM.TrayApp.Models
open Huskui.Avalonia
open Lib.Tray
open Serilog
open GlazeWM.Tray.Models
open GlazeWM.TrayApp
open GlazeWM.TrayApp.Helpers
open GlazeWM.TrayApp.Helpers.Notifications
open GlazeWM.TrayApp.Views

type App(config: AppConfig) as this =
    inherit Application()

    let trayItem = TrayItem(config)
    let uri = Uri($"ws://localhost:{config.Port}/")

    let mutable uiScheduler: SynchronizationContextScheduler = null // this is assigned once Avalonia initializes.
    let mutable observers: IDisposable option = None
    let mutable mainWindow: MainWindow option = None
    let mutable client: GlazeWMClient option = None

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

    let triggerRefreshState () =
        client
        |> Option.tryDo (fun c -> [ QWorkspaces; QBinding; QPaused ] |> List.iter c.SendMessage)

    let runSyncQuery query =
        match client with
        | Some c -> c.Query query
        | None -> Error "BUG: GlazeWMClient is None"

    member _.Cleanup(tray: TrayIcon) =
        Log.Information("Shutting down...")
        this.ClearGlazeClient()
        tray.IsVisible <- false

    member private _.OnTrayMenuEvent(e: MenuEvent) : Unit =
        match e with
        | ReconnectRequested ->
            this.InitGlazeClient()
            trayItem.OnConnectionRestored()
        | RefreshRequested -> triggerRefreshState ()
        | ToggleMainWindow -> toggleMainWindow ()
        | Notify(title, body) -> mainWindow |> Option.tryDo _.Notify(title, body)
        | SwitchWorkspace workspaceName ->
            client
            |> Option.tryDo (fun c -> c.SendMessage $"{CFocusWorkspacePrefix} {workspaceName.Name}")

    /// Resets client connection and prints an error message to the user.
    member private _.OnCommunicationError(err: exn) =
        let text =
            $"A fatal error occured regarding communication with GlazeWM \n\n{err.Message}. \n\nCheck the logs for \
              more details. To renew communication with GlazeWM after you make sure it runs correctly, please select \
              'Reinitialize GlazeWM Connection' from the tray menu."

        showErrorMessage "GlazeWM Communication Error" text
        this.ClearGlazeClient()
        trayItem.OnConnectionError()

    member private _.ClearGlazeClient() =
        client |> Option.iter (fun c -> (c :> IDisposable).Dispose())
        observers |> Option.iter (fun o -> o.Dispose())
        client <- None
        observers <- None

    member private _.InitGlazeClient() =
        let client' = new GlazeWMClient(uri)
        client <- Some client'

        let a =
            client'.GlazeWmMessages
                .DistinctUntilChanged()
                .ObserveOn(uiScheduler)
                .Scan(TrayIconState.empty, trayItem.Handle)
                .Subscribe(ignore)
        let b =
            client'.Failures.Take(1).ObserveOn(uiScheduler).Subscribe(this.OnCommunicationError)
        let c = client'.Warnings.ObserveOn(uiScheduler).Subscribe(this.OnParserWarnings)
        observers <- compose [ a; b; c ] |> Some

        $"sub -e {SWorkspaceUP} {SWorkspaceACT} {SWorkspaceDeACT} {SBindingModesCH} {SPauseCH} {SFocusCH}"
        |> client'.SendMessage
        triggerRefreshState ()

    member private _.OnParserWarnings(w: ParserWarnings) =
        match w with
        | UnsuccessfulResponse m ->
            mainWindow
            |> Option.tryDo _.Notify("Unsuccessful Response", m, NotificationType.Warning)
        | ParserError _ -> () // TODO: we already log it. Consider notification with toggle
        | UnexpectedError err -> sendBugNotification $"Un expected error: ${err}"
        | NoCurrentWorkspace -> sendBugNotification NoCurrentWorkspace

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
            uiScheduler <- SynchronizationContextScheduler(SynchronizationContext.Current)
            let w = MainWindow runSyncQuery
            mainWindow <- Some w
            trayItem.Initialize()
            let tray = trayItem.Tray
            let icons = TrayIcons()
            icons.Add(tray)
            TrayIcon.SetIcons(this, icons)
            this.InitGlazeClient()
            trayItem.MenuEvent.Subscribe(this.OnTrayMenuEvent) |> ignore

            this.ActualThemeVariantChanged.Add(fun _ ->
                Log.Debug("Theme variant changed, new variant is {Variant}", this.ActualThemeVariant)
                // trigger recalculation of the icon
                triggerRefreshState ())

            desktopLifetime.Exit.Add(fun _ -> this.Cleanup tray)
            tray.IsVisible <- true

            Log.Information("Application started")

        | _ -> ()
