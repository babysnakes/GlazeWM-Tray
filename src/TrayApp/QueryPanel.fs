namespace GlazeWM.TrayApp.Views

open Avalonia.Controls
open Avalonia.FuncUI.DSL

module QueryPanel =

    let contents =
        DockPanel.create [ DockPanel.children [ TextBlock.create [ TextBlock.text "to be continued..." ] ] ]
