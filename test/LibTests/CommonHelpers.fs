module LibTests.CommonHelpers

open System.IO

let mkDemoAgent fn =
    MailboxProcessor.Start(fun (inbox: MailboxProcessor<'T>) ->
        let rec loop () =
            async {
                let! msg = inbox.Receive()
                fn msg
                return! loop ()
            }

        loop ())

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
