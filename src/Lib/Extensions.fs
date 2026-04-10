namespace GlazeWM.Tray.Models

[<RequireQualifiedAccess>]
module Result =
    let tryCatch f =
        try Ok (f ()) with ex -> Error ex.Message

