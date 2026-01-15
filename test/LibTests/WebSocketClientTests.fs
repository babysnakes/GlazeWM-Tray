module LibTests.WebSocketClientTests

open System
open System.Net
open System.Net.Sockets
open System.Threading
open Fleck
open FsUnit
open LibTests.CommonHelpers
open NUnit.Framework
open GlazeWM.Tray.WebSocketClient

let getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

[<Test>]
let ``It fails fast if an error occurs`` () =
    let mutable serverSideSocket: IWebSocketConnection option = None
    let connectionSignal = new ManualResetEvent(false)
    let tcs = System.Threading.Tasks.TaskCompletionSource<bool>()
    let parser = mkDemoAgent ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <-
            fun () ->
                serverSideSocket <- Some socket
                connectionSignal.Set() |> ignore)

    let client = new WebSocketClient(Uri(url), parser)
    client.Error.Add(fun _ -> tcs.SetResult(true))

    if connectionSignal.WaitOne(TimeSpan.FromSeconds(2.0)) then
        let socket = serverSideSocket |> Option.get
        socket.Close(500)
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
    let mutable serverSideSocket: IWebSocketConnection option = None
    let connectionSignal = new ManualResetEvent(false)
    let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
    let parser = mkDemoAgent tcs.SetResult
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <-
            fun () ->
                serverSideSocket <- Some socket
                connectionSignal.Set() |> ignore)

    let client = new WebSocketClient(Uri(url), parser)
    client.Error.Add(failwith)

    if connectionSignal.WaitOne(TimeSpan.FromSeconds(2.0)) then
        let socket = serverSideSocket |> Option.get
        socket.Send(msg) |> ignore
        if not (tcs.Task.Wait(1000)) then failwith "Reached timeout"
        let result = tcs.Task.Result
        result |> should equal msg

[<Test>]
let ``ensure no duplicate connections for websocket client`` () =
    let parser = mkDemoAgent ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)
    let tcs = System.Threading.Tasks.TaskCompletionSource<string>()
    let mutable counter = 0

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> TestContext.Progress.WriteLine("Connected")

        socket.OnMessage <-
            fun message ->
                counter <- counter + 1
                if counter > 1 then tcs.SetResult(message)

        socket.OnClose <- fun () -> printfn "Disconnected")

    let client = new WebSocketClient(Uri(url), parser)
    client.Error.Add(fun msg -> Assert.Fail($"Error event: {msg}"))
    client.Agent.Post(SendMessage "hello")
    client.Agent.Post(SendMessage "hello") // it should raise event error if double connection happened
    if not (tcs.Task.Wait(1000)) then failwith "Timeout Happened"
