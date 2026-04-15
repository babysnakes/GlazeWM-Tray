namespace FSharpReactiveUI.Views

open Avalonia.Controls
open Avalonia.Markup.Xaml

type QueryPanelView() as this =
    inherit UserControl()
    do this.InitializeComponent()

    member private _.InitializeComponent() = AvaloniaXamlLoader.Load(this)
