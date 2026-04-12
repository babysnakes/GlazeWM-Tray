namespace GlazeWM.TrayApp.Views

open System
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout

type MainWindow(syncQueryF: string -> Result<string, string>) as this =
    inherit HostWindow()

    let notificationManager = WindowNotificationManager(this, MaxItems = 3)
    let pending = System.Collections.Generic.Queue<Notification>()
    let mutable initialized = false

    let tabs: IView list =
        [ TabItem.create [ TabItem.header "Query"; TabItem.content (QueryPanel.view syncQueryF) ]
          TabItem.create [ TabItem.header "About"; TabItem.content AboutPanel.contents ] ]

    let view () =
        Component(fun _ ->
            DockPanel.create
                [ DockPanel.children
                      [ TextBlock.create
                            [ DockPanel.dock Dock.Bottom
                              TextBlock.margin (0.0, 0.0, 0.0, 20.0)
                              TextBlock.fontSize 12.0
                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                              TextBlock.opacity 0.5
                              TextBlock.text "To close: CTRL+W" ]
                        TabControl.create [ TabControl.viewItems tabs ] ]

                  ])

    do
        base.Title <- "GlazeWM Tray"
        base.Width <- 600
        base.Height <- 400
        base.Content <- view ()

    member _.Notify(title, body, ?tpe) =
        let t = defaultArg tpe NotificationType.Information
        let n = Notification(title, body, t, TimeSpan.Zero)

        if initialized then
            notificationManager.Show(n)
        else
            pending.Enqueue(n)

    override this.OnKeyDown(e: Avalonia.Input.KeyEventArgs) =
        base.OnKeyDown(e)

        let isCtrlW =
            e.Key = Avalonia.Input.Key.W
            && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)
        if isCtrlW then this.Close()

    override this.OnClosing(e: WindowClosingEventArgs) =
        this.Hide()
        e.Cancel <- true
        base.Hide()

    override this.OnOpened(e) =
        base.OnOpened(e)

        if not initialized then
            initialized <- true
            while pending.Count > 0 do
                notificationManager.Show(pending.Dequeue())
