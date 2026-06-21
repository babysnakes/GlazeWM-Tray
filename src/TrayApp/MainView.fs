namespace GlazeWM.TrayApp.Views

open System
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open GlazeWM.TrayApp.Icons
open GlazeWM.TrayApp.Models
open GlazeWM.TrayApp.Views

type MainWindow(syncQueryF: string -> Result<string, string>) as this =
    inherit HostWindow()

    let helpText =
        """
Global Shortcuts:
-----------------
- Ctrl+W: Close the window

Windows Tab:
------------
- Right click on a window panel for contextual menu
"""

    let helpers =
        { new IViewsHelpers with
            member _.RunSyncQuery query = syncQueryF query }

    let notificationManager = WindowNotificationManager(this, MaxItems = 3)
    let pending = System.Collections.Generic.Queue<Notification>()
    let mutable initialized = false

    let tabs: IView list =
        [ TabItem.create [ TabItem.header "Query"; TabItem.content (QueryPanel.view helpers) ]
          TabItem.create [ TabItem.header "Windows"; TabItem.content (WindowsPanel.view helpers) ]
          TabItem.create [ TabItem.header "About"; TabItem.content AboutPanel.contents ] ]

    let view () =
        Component(fun _ ->
            DockPanel.create
                [ DockPanel.children
                      [ StackPanel.create
                            [ StackPanel.orientation Orientation.Horizontal
                              StackPanel.dock Dock.Bottom
                              StackPanel.horizontalAlignment HorizontalAlignment.Center
                              ToolTip.tip helpText
                              ToolTip.placement PlacementMode.Top
                              StackPanel.children
                                  [ PathIcon.create
                                        [ PathIcon.data (Geometry.Parse helpIcon)
                                          PathIcon.width 15.0
                                          PathIcon.height 15.0
                                          PathIcon.margin (20, 0, 10, 20)
                                          PathIcon.foreground Brushes.Gray ]
                                    TextBlock.create
                                        [ DockPanel.dock Dock.Bottom
                                          TextBlock.margin (0.0, 0.0, 0.0, 20.0)
                                          TextBlock.fontSize 12.0
                                          TextBlock.horizontalAlignment HorizontalAlignment.Center
                                          TextBlock.opacity 0.5
                                          TextBlock.text "Hover to show help" ] ] ]
                        TabControl.create [ TabControl.margin (5.0, 5.0, 5.0, 5.0); TabControl.viewItems tabs ] ]

                  ])

    do
        base.Title <- "GlazeWM Tray"
        base.Width <- 600
        base.Height <- 400
        base.MinWidth <- 550
        base.MinHeight <- 250
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
