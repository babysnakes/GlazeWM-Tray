namespace FSharpReactiveUI.Views

open System
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.Markup.Xaml

type MainWindow() as this =
    inherit Window()

    let notificationManager = WindowNotificationManager(this, MaxItems = 3)
    let pending = System.Collections.Generic.Queue<Notification>()
    let mutable initialized = false

    do this.InitializeComponent()

    member private _.InitializeComponent() = AvaloniaXamlLoader.Load(this)

    member _.Notify(title, body, ?tpe) =
        let t = defaultArg tpe NotificationType.Information
        let n = Notification(title, body, t, TimeSpan.Zero)

        if initialized then
            notificationManager.Show(n)
        else
            pending.Enqueue(n)

    override _.OnKeyDown(e: Avalonia.Input.KeyEventArgs) =
        base.OnKeyDown(e)

        let isCtrlW =
            e.Key = Avalonia.Input.Key.W
            && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)

        if isCtrlW then this.Close()

    override _.OnClosing(e: WindowClosingEventArgs) =
        this.Hide()
        e.Cancel <- true
        base.Hide()

    override _.OnOpened(e) =
        base.OnOpened(e)

        if not initialized then
            initialized <- true

            while pending.Count > 0 do
                notificationManager.Show(pending.Dequeue())
