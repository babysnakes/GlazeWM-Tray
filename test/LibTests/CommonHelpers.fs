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
