module LibTests.MessageParserTests

open FsUnit
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.WebSocketClient
open LibTests.CommonHelpers
open GlazeWM.Tray.Models
open NUnit.Framework

[<Test>]
let ``correctly parses workspace response`` () =
    let queryWorkspacesResponse = loadFixture "basic-workspaces-response.json"
    let tcs = System.Threading.Tasks.TaskCompletionSource<WorkspaceName>()

    let handler: MailboxProcessor<ParsingOutput> =
        mkDemoAgent (fun s ->
            match s with
            | CurrentWorkspace r -> tcs.SetResult(r)
            | _ -> ())

    let parser = Parser(handler)
    let dispatcher = parser.Dispatcher()
    dispatcher.Post queryWorkspacesResponse
    if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
    let result = tcs.Task.Result
    result.Name |> should equal "2"
    result.DisplayName |> should equal "2"

module ``focus-changed-event workflow tests`` =

    [<Test>]
    let ``happy workflow triggers active workspace response`` () =
        let queryWorkspacesResponse = loadFixture "basic-workspaces-response.json"
        let eventJson = loadFixture "basic-focus-changed-event.json"
        let tcs = System.Threading.Tasks.TaskCompletionSource<WorkspaceName>()
        let mutable counter = 0

        let handler: MailboxProcessor<ParsingOutput> =
            mkDemoAgent (function
                | CurrentWorkspace w ->
                    counter <- counter + 1

                    if counter = 2 then tcs.SetResult(w)
                | message -> TestContext.Progress.WriteLine($"handler received message: {message}"))

        let parser = Parser(handler)
        let dispatcher = parser.Dispatcher()
        dispatcher.Post queryWorkspacesResponse // Make sure the parser has a state
        dispatcher.Post eventJson
        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
        let result = tcs.Task.Result
        // The expected current workspace is calculated by joining the two JSON files loaded.
        result.Name |> should equal "2"

    [<Test>]
    let ``when focused window parent id is not found, it triggers a workspace refresh`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<bool>()
        let queryWorkspacesResponse = loadFixture "basic-workspaces-response.json"
        let eventJson = loadFixture "focus-changed-event-with-no-matching-workspace.json"
        let handler: MailboxProcessor<ParsingOutput> = mkDemoAgent ignore

        let mockWsClient =
            mkDemoAgent (function
                | SendMessage "query workspaces" -> tcs.SetResult(true)
                | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}"))

        let parser = Parser(handler)
        parser.SetWsClient mockWsClient
        let dispatcher = parser.Dispatcher()
        dispatcher.Post queryWorkspacesResponse // Make sure the parser has a state
        dispatcher.Post eventJson

        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
        let result = tcs.Task.Result
        result |> should be True

    [<Test>]
    [<Ignore("not implemented yet")>]
    let ``something bad happens when no state exist in the agent`` () = Assert.Fail("not implemented yet")
