module LibTests.MessageParserTests

open FsUnit
open GlazeWM.Tray.Literals
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.WebSocketClient
open LibTests.CommonHelpers
open GlazeWM.Tray.Models
open NUnit.Framework

module ``workspace response parsing tests`` =

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

    [<Test>]
    let ``MessageParser emits error when workspace response does not contain current workspace`` () =
        let queryWorkspacesResponse =
            loadFixture "basic-workspaces-response-with-no-focus.json"

        let tcs = System.Threading.Tasks.TaskCompletionSource<MessageParserEvent>()
        let handler = mkDemoAgent ignore
        let parser = Parser(handler)
        parser.Error.Add tcs.SetResult
        let dispatcher = parser.Dispatcher()
        dispatcher.Post queryWorkspacesResponse

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("timeout waiting for event error")

        let result = tcs.Task.Result
        result |> should be (ofCase <@ NoCurrentWorkspace @>)

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
                | SendMessage QWorkspaces -> tcs.SetResult(true)
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
    let ``when state is empty, it triggers a workspace refresh`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<WebSocketMessage>()
        let eventJson = loadFixture "focus-changed-event-with-no-matching-workspace.json"
        let handler: MailboxProcessor<ParsingOutput> = mkDemoAgent ignore

        let mockWsClient = mkDemoAgent tcs.SetResult

        let parser = Parser(handler)
        parser.SetWsClient mockWsClient
        let dispatcher = parser.Dispatcher()
        dispatcher.Post eventJson

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("Timeout waiting for workspace query message")

        let result = tcs.Task.Result
        result |> should equal (SendMessage QWorkspaces)

    [<Test>]
    let ``when emitted with other container then window, emits workspace query`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<WebSocketMessage>()
        let eventJson = loadFixture "focus-changed-event-with-workspace-container.json"
        let handler: MailboxProcessor<ParsingOutput> = mkDemoAgent ignore
        let mockWsClient = mkDemoAgent tcs.SetResult
        let parser = Parser(handler)
        parser.SetWsClient mockWsClient
        let dispatcher = parser.Dispatcher()
        dispatcher.Post eventJson

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("Timeout: non window container")

        let result = tcs.Task.Result
        result |> should equal (SendMessage "query workspaces")

module ``Unsuccessful Responses`` =
    [<Test>]
    let ``unsuccessful response with error message returns the error`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<unit>()
        let response = loadFixture "error-response-with-error.json"

        let handler =
            mkDemoAgent (fun s ->
                match s with
                | UnSuccessfulResponse _ -> tcs.SetResult()
                | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}"))

        let parser = Parser(handler)
        let dispatcher = parser.Dispatcher()
        dispatcher.Post response

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("timeout waiting for error message")

    [<Test>]
    let ``unsuccessful response without error message returns descriptive error`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
        let response = loadFixture "error-response-without-error.json"

        let handler =
            mkDemoAgent (function
                | UnSuccessfulResponse r -> tcs.SetResult(r)
                | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}"))

        let parser = Parser(handler)
        let dispatcher = parser.Dispatcher()
        dispatcher.Post response

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("timeout waiting for error message")

        let result = tcs.Task.Result
        result |> should equal "Unspecified Error"

module ``Pause status`` =
    type TestInput = { File: string; Expected: bool }

    let input =
        [ { File = "unpaused-query-response.json"
            Expected = false }
          { File = "paused.json"
            Expected = true }
          { File = "unpaused.json"
            Expected = false } ]

    [<TestCaseSource(nameof input)>]
    let ``Correctly parses pause status`` (input: TestInput) =
        let tcs = System.Threading.Tasks.TaskCompletionSource<bool>()
        let response = loadFixture input.File

        let handler =
            mkDemoAgent (function
                | Paused p -> tcs.SetResult(p)
                | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}"))

        let parser = Parser(handler)
        let dispatcher = parser.Dispatcher()

        dispatcher.Post response
        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout: pause status")
        let result = tcs.Task.Result
        result |> should equal input.Expected

module ``Binding Modes`` =
    type TestInput = { File: string; Expected: bool }

    let input =
        [ { File = "binding-modes-default-query-response.json"
            Expected = false }
          { File = "binding-modes-custom-query-response.json"
            Expected = true }
          { File = "binding-modes-new.json"
            Expected = true }
          { File = "binding-modes-default.json"
            Expected = false } ]

    [<TestCaseSource(nameof input)>]
    let ``Correctly parses binding modes`` (input: TestInput) =
        let tcs = System.Threading.Tasks.TaskCompletionSource<bool>()
        let response = loadFixture input.File

        let handler =
            mkDemoAgent (function
                | NewBindingModes b -> tcs.SetResult(b)
                | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}"))

        let parser = Parser(handler)
        let dispatcher = parser.Dispatcher()

        dispatcher.Post response
        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout: bindings")
        let result = tcs.Task.Result
        result |> should equal input.Expected

module ``Workspace activated-deactivated-updated`` =
    let event = [ SWorkspaceUP; SWorkspaceACT; SWorkspaceDeACT ]

    let mkMinimalJson (evt: string) =
        $"""{{"messageType":"event_subscription","data":{{"eventType":"{evt}"}},"error":null,"success":true}}"""

    [<TestCaseSource(nameof event)>]
    let ``Workspace-* events trigger workspaces query`` (evt: string) =
        let tcs = System.Threading.Tasks.TaskCompletionSource<unit>()
        let json = mkMinimalJson evt
        let handler = mkDemoAgent ignore

        let mockWsClient =
            mkDemoAgent (function
                | SendMessage QWorkspaces -> tcs.SetResult()
                | invalid -> TestContext.Progress.WriteLine($"unexpected message: {invalid}"))

        let parser = Parser(handler)
        parser.SetWsClient mockWsClient
        let dispatcher = parser.Dispatcher()
        dispatcher.Post json

        if not (tcs.Task.Wait(1000)) then Assert.Fail($"timeout: {evt}")

module ``Actor Resilience Test`` =
    type TestInput = { File: string; ErrorMessage: string }

    let mkInvalidJsonTypes () =
        [ { File = "non-json.json"
            ErrorMessage = "'n' is an invalid start" }
          { File = "invalid-workspace-response.json"
            ErrorMessage = "Missing field for record type" }
          // This case is unique, it fails before parsing for lack of wsClient
          { File = "invalid-focus-changed-event.json"
            ErrorMessage = "UnsetWsClient" }
          { File = "binding-modes-invalid.json"
            ErrorMessage = "Missing field for record type" }
          { File = "error-paused-query-response.json"
            ErrorMessage = "Expected Bool, but got String" }
          { File = "invalid-paused-event.json"
            ErrorMessage = "Missing field for record type" } ]

    [<TestCaseSource(nameof mkInvalidJsonTypes)>]
    let ``keeps working after non/invalid json input`` (input: TestInput) =
        let invalidJson = loadFixture input.File
        let tcs = System.Threading.Tasks.TaskCompletionSource<unit>()
        let goodWorkspaceQuery = loadFixture "basic-workspaces-response.json"
        let mutable error = ""

        let handler =
            mkDemoAgent (function
                | CurrentWorkspace _ -> tcs.SetResult()
                | msg -> TestContext.Progress.WriteLine($"handler invalid message: {msg}"))

        let errorHandler =
            function
            | ParseError e ->
                TestContext.Progress.WriteLine($"error handler parse error: {e}")
                error <- e
            | UnsetWsClient -> error <- "UnsetWsClient"
            | msg -> TestContext.Progress.WriteLine($"error handler invalid message: {msg}")

        let parser = Parser(handler)
        parser.Error.Add errorHandler
        let dispatcher = parser.Dispatcher()
        dispatcher.Post invalidJson
        dispatcher.Post goodWorkspaceQuery

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail($"timeout processing input: {input}")

        error |> should contain input.ErrorMessage

    [<Test>]
    let ``wsClient option emits specific event when called before being set`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<unit>()

        let json =
            """{"messageType":"event_subscription","data":{"eventType":"workspace_deactivated"},"error":null,"success":true}"""

        let handler =
            mkDemoAgent (fun inp -> TestContext.Progress.WriteLine($"handler: {inp}"))

        let parser = Parser(handler)

        parser.Error.Add (function
            | UnsetWsClient -> tcs.SetResult()
            | invalid -> TestContext.Progress.WriteLine($"unexpected message: {invalid}"))

        let dispatcher = parser.Dispatcher()
        dispatcher.Post json

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail($"timeout unset wsClient")
