module LibTests.CommonHelpers

open System
open System.IO
open GlazeWM.Tray.Models

let mkIWsClient (f: string -> unit) (s: IObservable<string>) =
    { new IWsClient with
        member _.ReceivedMessages = s
        member _.SendMessage msg = f msg }

let loadFixture fileName =
    let fixturePath = Path.Combine("Fixtures", fileName)
    File.ReadAllText fixturePath

[<RequireQualifiedAccess>]
module Result =
    let unwrap =
        function
        | Ok x -> x
        | Error e -> failwith $"unwrapped error: {e}"

    let unwrapError =
        function
        | Ok v -> failwith $"expected error but was: Ok {v}"
        | Error e -> e
