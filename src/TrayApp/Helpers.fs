namespace GlazeWM.TrayApp.Helpers

open Microsoft.Toolkit.Uwp.Notifications
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
        ToastContentBuilder().AddText(title).AddText(body).Show()
