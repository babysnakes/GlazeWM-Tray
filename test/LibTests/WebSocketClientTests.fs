module LibTests.WebSocketClientTests

open System.Net
open System.Net.Sockets
open System.Threading
open Fleck
open Xunit
open FsUnit.Xunit
open GlazeWM.Tray.WebSocketClient
open System

let mkDemoParser fn =
    MailboxProcessor.Start(fun (inbox: MailboxProcessor<string>) ->
        let rec loop () =
            async {
                let! msg = inbox.Receive()
                fn msg
                return! loop ()
            }

        loop ())

let getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

[<Fact>]
let ``It fails fast if an error occurs`` () =
    let mutable serverSideSocket: IWebSocketConnection option = None
    let connectionSignal = new ManualResetEvent(false)
    let mutable errorOccured = false
    let parser = mkDemoParser ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <-
            fun () ->
                serverSideSocket <- Some socket
                connectionSignal.Set() |> ignore)

    let agent = newClient (Uri(url)) parser
    agent.Error.Add(fun _ -> errorOccured <- true)

    async {
        if connectionSignal.WaitOne(TimeSpan.FromSeconds(2.0)) then
            let socket = serverSideSocket |> Option.get
            socket.Close(500)
            do! Async.Sleep 40
    }
    |> Async.RunSynchronously

    mockServer.Dispose()
    errorOccured |> should be True

[<Fact>]
let ``It can send very messages`` () =
    let msg = String.replicate 1024 "ae-d"
    let mutable serverReceived = ""
    let parser = mkDemoParser ignore
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <- fun () -> printfn "Connected"
        socket.OnMessage <- fun msg -> serverReceived <- msg
        socket.OnClose <- fun () -> printfn "Disconnected")

    let agent = newClient (Uri(url)) parser
    agent.Error.Add(raise)
    agent.Post(SendMessage msg)
    Async.Sleep 40 |> Async.RunSynchronously
    serverReceived |> should equal msg

[<Fact>]
let ``It handles large messages from server`` () =
    let msg = String.replicate 1024 "a-bd"
    let mutable serverSideSocket: IWebSocketConnection option = None
    let connectionSignal = new ManualResetEvent(false)
    let mutable received = ""
    let parser = mkDemoParser (fun s -> received <- s)
    let url = $"ws://127.0.0.1:{(getFreePort ())}"
    use mockServer = new WebSocketServer(url)

    mockServer.Start(fun socket ->
        socket.OnOpen <-
            fun () ->
                serverSideSocket <- Some socket
                connectionSignal.Set() |> ignore)

    let agent = newClient (Uri(url)) parser
    agent.Error.Add(raise)

    async {
        if connectionSignal.WaitOne(TimeSpan.FromSeconds(2.0)) then
            let socket = serverSideSocket |> Option.get
            socket.Send(msg) |> ignore
            do! Async.Sleep 40
    }
    |> Async.RunSynchronously

    received |> should equal msg
