module LibTests.MessageParserTests

open System.Reactive.Subjects
open FsUnit
open LibTests.CommonHelpers
open NUnit.Framework
open GlazeWM.Tray.Literals
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models

module ``workspace response parsing tests`` =

    [<Test>]
    let ``correctly parses workspace response`` () =
        let queryWorkspacesResponse = loadFixture "basic-workspaces-response.json"
        let tcs = System.Threading.Tasks.TaskCompletionSource<WorkspacesNotification>()
        use subject = new Subject<string>()
        let wsc = mkIWsClient ignore subject

        let handler msg =
            match msg with
            | Workspaces r -> tcs.SetResult(r)
            | _ -> ()

        use parser = new Parser(wsc)
        parser.GlazewmMessages.Subscribe handler |> ignore
        subject.OnNext queryWorkspacesResponse
        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
        let result = tcs.Task.Result
        result.Current.Name |> should equal "2"
        result.Current.DisplayName |> should equal "2"
        result.Active |> should haveLength 2
        result.Active |> should contain result.Current

    [<Test>]
    let ``MessageParser emits error when workspace response does not contain current workspace`` () =
        let queryWorkspacesResponse =
            loadFixture "basic-workspaces-response-with-no-focus.json"
        let tcs = System.Threading.Tasks.TaskCompletionSource<ParserWarnings>()
        use subject = new Subject<string>()
        let wsc = mkIWsClient ignore subject
        use parser = new Parser(wsc)
        parser.Warnings.Subscribe tcs.SetResult |> ignore
        subject.OnNext queryWorkspacesResponse

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("timeout waiting for event error")

        let result = tcs.Task.Result
        result |> should be (ofCase <@ NoCurrentWorkspace @>)

module ``focus-changed-event workflow tests`` =

    [<Test>]
    let ``happy workflow triggers active workspace response`` () =
        let queryWorkspacesResponse = loadFixture "basic-workspaces-response.json"
        let eventJson = loadFixture "basic-focus-changed-event.json"
        let tcs = System.Threading.Tasks.TaskCompletionSource<WorkspacesNotification>()
        let mutable counter = 0
        use subject = new Subject<string>()
        let wsc = mkIWsClient ignore subject

        let handler msg =
            match msg with
            | Workspaces w ->
                counter <- counter + 1
                if counter = 2 then tcs.SetResult(w)
            | message -> TestContext.Progress.WriteLine($"handler received message: {message}")

        use parser = new Parser(wsc)
        parser.GlazewmMessages.Subscribe handler |> ignore
        subject.OnNext queryWorkspacesResponse // Make sure the parser has a state
        subject.OnNext eventJson
        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
        let result = tcs.Task.Result
        // The expected current workspace is calculated by joining the two JSON files loaded.
        result.Current.Name |> should equal "2"
        result.Active |> should contain result.Current

    [<Test>]
    let ``when focused window parent id is not found, it triggers a workspace refresh`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<unit>()
        let queryWorkspacesResponse = loadFixture "basic-workspaces-response.json"
        let eventJson = loadFixture "focus-changed-event-with-no-matching-workspace.json"
        use subject = new Subject<string>()
        let mockSend _msg = tcs.SetResult(())
        let wsc = mkIWsClient mockSend subject
        use parser = new Parser(wsc)
        subject.OnNext queryWorkspacesResponse // Make sure the parser has a state
        subject.OnNext eventJson

        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")

    [<Test>]
    let ``when state is empty, it triggers a workspace refresh`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
        let eventJson = loadFixture "focus-changed-event-with-no-matching-workspace.json"
        use subject = new Subject<string>()
        let wcs = mkIWsClient tcs.SetResult subject
        use parser = new Parser(wcs)
        subject.OnNext eventJson
        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("Timeout waiting for workspace query message")

        let result = tcs.Task.Result
        result |> should equal QWorkspaces

    [<Test>]
    let ``when emitted with other container then window, emits workspace query`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
        let eventJson = loadFixture "focus-changed-event-with-workspace-container.json"
        use subject = new Subject<string>()
        let wsc = mkIWsClient tcs.SetResult subject
        use parser = new Parser(wsc)
        subject.OnNext eventJson
        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("Timeout: non window container")

        let result = tcs.Task.Result
        result |> should equal QWorkspaces

module ``Unsuccessful Responses`` =
    [<Test>]
    let ``unsuccessful response with error message returns the error`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
        let response = loadFixture "error-response-with-error.json"
        use subject = new Subject<string>()
        let handler (w: ParserWarnings) =
            match w with
            | UnsuccessfulResponse e -> tcs.SetResult e
            | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}")
        let wsc = mkIWsClient ignore subject

        use parser = new Parser(wsc)
        parser.Warnings.Subscribe handler |> ignore
        subject.OnNext response

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail("timeout waiting for error message")
        let result = tcs.Task.Result
        result |> should contain "unrecognized subcommand"

    [<Test>]
    let ``unsuccessful response without error message returns descriptive error`` () =
        let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
        let response = loadFixture "error-response-without-error.json"
        use subject = new Subject<string>()
        let handler (w: ParserWarnings) =
            match w with
            | UnsuccessfulResponse r -> tcs.SetResult(r)
            | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}")
        let wsc = mkIWsClient ignore subject

        use parser = new Parser(wsc)
        parser.Warnings.Subscribe handler |> ignore
        subject.OnNext response

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
        use subject = new Subject<string>()
        let wsc = mkIWsClient ignore subject
        use parser = new Parser(wsc)

        let handler (m: ParsedMessage) =
            match m with
            | Paused p -> tcs.SetResult(p)
            | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}")
        parser.GlazewmMessages.Subscribe handler |> ignore

        subject.OnNext response
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
        use subject = new Subject<string>()
        let wsc = mkIWsClient ignore subject
        use parser = new Parser(wsc)

        let handler =
            function
            | NewBindingModes b -> tcs.SetResult(b)
            | invalid -> TestContext.Error.WriteLine($"unexpected message: {invalid}")
        parser.GlazewmMessages.Subscribe handler |> ignore

        subject.OnNext response
        if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout: bindings")
        let result = tcs.Task.Result
        result |> should equal input.Expected

module ``Workspace activated-deactivated-updated`` =
    let event = [ SWorkspaceUP; SWorkspaceACT; SWorkspaceDeACT ]

    let mkMinimalJson (evt: string) =
        $"""{{"messageType":"event_subscription","data":{{"eventType":"{evt}"}},"error":null,"success":true}}"""

    [<TestCaseSource(nameof event)>]
    let ``Workspace-* events trigger workspaces query`` (evt: string) =
        let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
        let json = mkMinimalJson evt
        use subject = new Subject<string>()
        let mockSendMsg s = tcs.SetResult s
        let wsc = mkIWsClient mockSendMsg subject
        use parser = new Parser(wsc)

        subject.OnNext json

        if not (tcs.Task.Wait(1000)) then Assert.Fail($"timeout: {evt}")
        let result = tcs.Task.Result
        result |> should equal QWorkspaces

module ``Actor Resilience Test`` =
    type TestInput = { File: string; ErrorMessage: string }

    let mkInvalidJsonTypes () =
        [ { File = "non-json.json"
            ErrorMessage = "'n' is an invalid start" }
          { File = "invalid-workspace-response.json"
            ErrorMessage = "$.data.workspaces[0].name" }
          { File = "invalid-focus-changed-event.json"
            ErrorMessage = "Expected String, but got Undefined" }
          { File = "binding-modes-invalid.json"
            ErrorMessage = "$.data.newBindingModes[0].name" }
          { File = "error-paused-query-response.json"
            ErrorMessage = "Expected Bool, but got String" }
          { File = "invalid-paused-event.json"
            ErrorMessage = "$.data.isPaused" } ]

    [<TestCaseSource(nameof mkInvalidJsonTypes)>]
    let ``keeps working after non/invalid json input`` (input: TestInput) =
        let invalidJson = loadFixture input.File
        let tcs = System.Threading.Tasks.TaskCompletionSource<unit>()
        let goodWorkspaceQuery = loadFixture "basic-workspaces-response.json"
        let mutable error = ""
        use subject = new Subject<string>()
        let wsc = mkIWsClient ignore subject

        let handleParsed m =
            match m with
            | Workspaces _ -> tcs.SetResult()
            | msg -> TestContext.Progress.WriteLine($"handler invalid message: {msg}")

        let handleWarnings (w: ParserWarnings) =
            match w with
            | ParserError e ->
                TestContext.Progress.WriteLine($"error handler parse error: {e}")
                error <- e
            | msg -> TestContext.Progress.WriteLine($"error handler invalid message: {msg}")

        use parser = new Parser(wsc)
        parser.Warnings.Subscribe handleWarnings |> ignore
        parser.GlazewmMessages.Subscribe handleParsed |> ignore
        subject.OnNext invalidJson
        subject.OnNext goodWorkspaceQuery

        if not (tcs.Task.Wait(1000)) then
            Assert.Fail($"timeout processing input: {input}")

        error |> should contain input.ErrorMessage
