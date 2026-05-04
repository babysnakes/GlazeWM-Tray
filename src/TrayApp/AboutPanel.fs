namespace GlazeWM.TrayApp.Views

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open System.Reflection

module AboutPanel =
    let version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        |> Option.ofObj
        |> Option.map (fun a -> a.InformationalVersion.Split('+')[0])
        |> Option.defaultValue "0.0-error"

    let contents =
        DockPanel.create
            [ DockPanel.children
                  [ StackPanel.create
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
                                      TextBlock.text $"Version {version}" ] ] ] ] ]
