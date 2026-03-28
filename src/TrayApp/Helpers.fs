namespace GlazeWM.TrayApp.Helpers

open System.Threading.Tasks
open MsBox.Avalonia
open MsBox.Avalonia.Enums
open Serilog


[<RequireQualifiedAccess>]
module Option =
    let tryDo (f: 'T -> unit) =
        function
        | Some x -> f x
        | None -> Log.Warning("tryDo on None")

module Notifications =
    /// Send Windows toast basic notification
    let sendNotification title body =
        // ToastContentBuilder().AddText(title).AddText(body).Show()
        Log.Warning $"Notification title: {title}, body: {body}"

    let sendBugNotification bug =
        (*
        ToastContentBuilder()
            .AddText("You encountered a bug!")
            .AddText(
                $"Please report the bug and specify the reason ({bug}). You can also attach log.txt from the logs directory."
            )
            .AddButton(ToastButton().SetContent("Report the bug").SetProtocolActivation(Uri(BugUrl)))
            .Show()
        *)
        Log.Warning $"Bug notification title: {bug}"

    /// Send error message box
    let showErrorMessage (title: string) (message: string) =
        async {
            try
                do!
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(fun () ->
                        let box =
                            MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Error)

                        box.ShowAsync() :> Task)
                    |> Async.AwaitTask
            with ex ->
                Log.Error(ex, "Failed to show error message box")
        }
        |> Async.Start
