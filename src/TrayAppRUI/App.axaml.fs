namespace FSharpReactiveUI

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml
open FSharpReactiveUI.ViewModels
open FSharpReactiveUI.Views

type App() =
    inherit Application()

    override this.Initialize() =
            AvaloniaXamlLoader.Load(this)

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
             desktop.MainWindow <- MainWindow(DataContext = MainWindowViewModel())
        | _ -> ()

        base.OnFrameworkInitializationCompleted()
