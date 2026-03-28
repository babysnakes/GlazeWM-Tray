module GlazeWM.TrayApp.Views

open System
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open System.Reflection

module Main =
    let version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        |> Option.ofObj
        |> Option.map (fun a -> a.InformationalVersion.Split('+')[0])
        |> Option.defaultValue "0.0-error"

    let view () =
        Component(fun _ ->
            DockPanel.create
                [ DockPanel.children
                      [ // Footer text docked to the bottom
                        TextBlock.create
                            [ DockPanel.dock Dock.Bottom
                              TextBlock.margin (0.0, 0.0, 0.0, 20.0)
                              TextBlock.fontSize 12.0
                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                              TextBlock.opacity 0.5
                              TextBlock.text "To close: ESC or ENTER or CTRL+W" ]

                        StackPanel.create
                            [ StackPanel.verticalAlignment VerticalAlignment.Center
                              StackPanel.children
                                  [ TextBlock.create
                                        [ TextBlock.fontSize 48.0
                                          TextBlock.horizontalAlignment HorizontalAlignment.Center
                                          TextBlock.text "GlazeWM Tray" ]
                                    TextBlock.create
                                        [ TextBlock.fontSize 18.0
                                          TextBlock.horizontalAlignment HorizontalAlignment.Center
                                          TextBlock.opacity 0.6
                                          TextBlock.text $"Version {version}" ] ] ] ] ])


type MainWindow() as this =
    inherit HostWindow()

    let notificationManager = WindowNotificationManager(this, MaxItems = 3)
    let pending = System.Collections.Generic.Queue<Notification>()
    let mutable initialized = false

    do
        base.Title <- "GlazeWM Tray"
        base.Width <- 600
        base.Height <- 400
        base.Content <- Main.view ()

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

        if e.Key = Avalonia.Input.Key.Escape || e.Key = Avalonia.Input.Key.Enter || isCtrlW then
            this.Close()

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
