namespace GlazeWM.TrayApp.Helpers

open System.Diagnostics
open System.Threading.Tasks
open MsBox.Avalonia
open MsBox.Avalonia.Enums
open Serilog
open GlazeWM.Tray.Literals


[<RequireQualifiedAccess>]
module Option =
    let tryDo t (f: 'T -> unit) =
        function
        | Some x -> f x
        | None -> Log.Warning($"tryDo on None (type: {t})")

module Notifications =
    let private openUri uri =
        try
            Process.Start(ProcessStartInfo(uri, UseShellExecute = true)) |> ignore
        with ex ->
            Log.Error(ex, $"Failed to open uri: {uri}")

    /// Send Windows toast basic notification
    let sendNotification title body =
        // ToastContentBuilder().AddText(title).AddText(body).Show()
        Log.Warning $"Notification title: {title}, body: {body}"

    let sendBugNotification bug =
        let title = "You encountered a bug!"

        let body =
            $"Please report the bug and specify the reason ({bug}). \
              You can also attach log.txt from the logs directory. \
              \n\n\
              Do you want to open the bug report page?"

        async {
            try
                let! result =
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<ButtonResult>(fun () ->
                        let box =
                            MessageBoxManager.GetMessageBoxStandard(title, body, ButtonEnum.YesNo, Icon.Error)

                        box.ShowAsync())
                    |> Async.AwaitTask

                if result = ButtonResult.Yes then openUri BugUrl
            with ex ->
                Log.Error(ex, "Failed to show error message box")

        }
        |> Async.Start

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
