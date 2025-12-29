namespace CounterApp

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout

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

type App() =
    inherit Application()

    // Toggle show/hide of the main window
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

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            // Make shut down explicit, Don't shut down when closing the main window
            desktopLifetime.ShutdownMode <- ShutdownMode.OnExplicitShutdown

            let showHideItem = NativeMenuItem(Header = "Show/Hide Window")
            showHideItem.Click.Add(fun _ -> toggleMainWindow desktopLifetime)

            let quitItem = NativeMenuItem(Header = "Quit")

            quitItem.Click.Add(fun _ ->
                match this.ApplicationLifetime with
                | :? IClassicDesktopStyleApplicationLifetime as dl -> dl.Shutdown(0)
                | _ -> ())

            let menu = NativeMenu()
            menu.Items.Add(showHideItem)
            menu.Items.Add(NativeMenuItemSeparator())
            menu.Items.Add(quitItem)

            let tray = new TrayIcon()
            tray.ToolTipText <- "Workspace ?"
            tray.Menu <- menu
            tray.Clicked.Add(fun _ -> toggleMainWindow desktopLifetime)

            let app_icon = WindowIcon(System.IO.Path.Combine("Assets", "icon-qm-w.ico"))
            tray.Icon <- app_icon

            let icons = TrayIcons()
            icons.Add(tray)
            TrayIcon.SetIcons(this, icons)

            tray.IsVisible <- true

        | _ -> ()



module Program =

    [<EntryPoint>]
    let main (args: string[]) =
        AppBuilder.Configure<App>().UsePlatformDetect().UseSkia().StartWithClassicDesktopLifetime(args)
