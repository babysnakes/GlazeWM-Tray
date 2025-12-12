module LibTests.WebSocketClientTests

open System.Net
open System.Net.Sockets
open System.Threading
open Fleck
open LibTests.CommonHelpers
open GlazeWM.Tray.WebSocketClient
open System
open NUnit.Framework
open FsUnit

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

    let agent = newClient (Uri(url)) parser
    agent.Error.Add(fun _ -> tcs.SetResult(true))

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

    let agent = newClient (Uri(url)) parser
    agent.Error.Add(raise)
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

    let agent = newClient (Uri(url)) parser
    agent.Error.Add(raise)

    if connectionSignal.WaitOne(TimeSpan.FromSeconds(2.0)) then
        let socket = serverSideSocket |> Option.get
        socket.Send(msg) |> ignore
        if not (tcs.Task.Wait(1000)) then failwith "Reached timeout"
        let result = tcs.Task.Result
        result |> should equal msg
