module LibTests.WebSocketClientTests

open System
open System.Net
open System.Net.Sockets
open Fleck
open FsUnit
open LibTests.CommonHelpers
open NUnit.Framework
open GlazeWM.Tray.WebSocketClient

type ClientUnderTest =
    | Client
    | SyncClient

let getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let clients = [ ClientUnderTest.Client; ClientUnderTest.SyncClient ]

[<TestCaseSource(nameof clients)>]
let ``It fails fast if an error occurs`` (cut: ClientUnderTest) =
    let tcs = System.Threading.Tasks.TaskCompletionSource<bool>()
    let parser = mkDemoAgent ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> TestContext.Progress.WriteLine("Connected")
        socket.OnMessage <- fun _ -> socket.Close(500))

    let client = new WebSocketClient(Uri(url), parser)
    client.Error.Add(fun _ -> tcs.SetResult(true))
    match cut with
    | Client -> client.Agent.Post(SendMessage "ping")
    | SyncClient -> client.Query("ping") |> ignore

    if not (tcs.Task.Wait(1000)) then failwith "timeout"
    let result = tcs.Task.Result
    mockServer.Dispose()
    result |> should be True


[<Test>]
let ``It can send very long messages`` () =
    let msg = String.replicate 1024 "ae-d"
    let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
    let parser = mkDemoAgent ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> printfn "Connected"
        socket.OnMessage <- fun message -> tcs.SetResult(message)
        socket.OnClose <- fun () -> printfn "Disconnected")

    let client = new WebSocketClient(Uri(url), parser)
    let agent = client.Agent
    client.Error.Add(failwith)
    agent.Post(SendMessage msg)
    if not (tcs.Task.Wait(1000)) then failwith "reached timeout"
    let result = tcs.Task.Result
    result |> should equal msg

[<Test>]
let ``It handles large messages from server`` () =
    let msg = String.replicate 1024 "a-bd"
    let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
    let parser = mkDemoAgent tcs.SetResult
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> TestContext.Progress.WriteLine("Connected")
        socket.OnMessage <- fun _ -> socket.Send(msg) |> ignore)

    let client = new WebSocketClient(Uri(url), parser)
    client.Error.Add(failwith)
    client.Agent.Post(SendMessage "ping")
    if not (tcs.Task.Wait(1000)) then failwith "Reached timeout"
    let result = tcs.Task.Result
    result |> should equal msg

[<Test>]
let ``send and receive sync messages`` () =
    let tcs = System.Threading.Tasks.TaskCompletionSource<bool>()
    let parser = mkDemoAgent ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> TestContext.Progress.WriteLine("Connected")
        socket.OnMessage <-
            fun _ ->
                tcs.SetResult(true)
                socket.Send("pong") |> ignore
        socket.OnClose <- fun () -> printfn "Disconnected")

    let client = new WebSocketClient(Uri(url), parser)
    let result = client.Query("ping") |> Result.unwrap
    if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
    result |> should equal "pong"

[<Test>]
let ``send and receive: error response`` () =
    let tcs = System.Threading.Tasks.TaskCompletionSource<bool>()
    let parser = mkDemoAgent ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> TestContext.Progress.WriteLine("Connected")
        socket.OnMessage <-
            fun _ ->
                tcs.SetResult(true)
                socket.Close(500))

    let client = new WebSocketClient(Uri(url), parser)
    let result = client.Query("ping") |> Result.unwrapError
    if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
    result |> should contain "One or more errors"

[<Test>]
let ``sync client respects timeout parameter`` () =
    let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
    let parser = mkDemoAgent ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> TestContext.Progress.WriteLine("Connected")
        socket.OnMessage <- fun m -> TestContext.Progress.WriteLine($"Message {m}"))

    let client = new WebSocketClient(Uri(url), parser)
    client.Error.Add tcs.SetResult
    let result = client.Query("ping", 500) |> Result.unwrapError
    if not (tcs.Task.Wait(1000)) then Assert.Fail("timeout")
    result |> should contain "500ms"
