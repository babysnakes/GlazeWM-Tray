namespace GlazeWM.TrayApp.Helpers

open Serilog

[<RequireQualifiedAccess>]
module Option =
    let tryDo (f: 'T -> unit) =
        function
        | Some x -> f x
        | None -> Log.Warning("tryDo on None")
