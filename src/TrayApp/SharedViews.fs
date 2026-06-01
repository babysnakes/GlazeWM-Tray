module GlazeWM.TrayApp.Views.SharedViews

open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media

// A very plain text block with text
let simplePanel msg : IView =
    TextBlock.create [ TextBlock.margin 5.0; TextBlock.text msg ]

/// An error text block
let error msg : IView =
    TextBlock.create [ TextBlock.foreground "red"; TextBlock.text msg ]

/// A Yes/No dialog
let confirmDialog (owner: Window) (message: string) : Task<bool> =
    let dialog =
        Window(
            Title = "Confirm",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        )
    let panel = StackPanel(Margin = Thickness(16.0), Spacing = 12.0)
    panel.Children.Add(
        TextBlock(
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 600.0,
            TextAlignment = TextAlignment.Center
        )
    )
    let btnRow =
        StackPanel(Orientation = Orientation.Horizontal, Spacing = 8.0, HorizontalAlignment = HorizontalAlignment.Right)
    let yes = Button(Content = "Yes", Width = 60.0)
    let no = Button(Content = "No", Width = 60.0)
    yes.Click.Add(fun _ -> dialog.Close(true))
    no.Click.Add(fun _ -> dialog.Close(false))
    btnRow.Children.Add(yes)
    btnRow.Children.Add(no)
    panel.Children.Add(btnRow)
    dialog.Content <- panel
    dialog.ShowDialog<bool>(owner)
