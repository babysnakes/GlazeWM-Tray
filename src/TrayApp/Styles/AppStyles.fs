namespace GlazeWM.TrayApp

open Avalonia.Markup.Xaml
open Avalonia.Styling

type AppStyles() as this =
    inherit Styles()
    do AvaloniaXamlLoader.Load(this)