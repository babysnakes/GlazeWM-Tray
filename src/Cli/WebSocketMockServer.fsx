#r "nuget: Fleck, 1.2.0"

open Fleck

let mutable currentSocket: IWebSocketConnection option = None
let server = new WebSocketServer("ws://0.0.0.0:8181")

server.Start(fun socket ->
    socket.OnOpen <-
        fun () ->
            printfn $">> Client Connected: {socket.ConnectionInfo.ClientIpAddress}"
            currentSocket <- Some socket

    socket.OnClose <-
        fun () ->
            printfn ">> Client Disconnected"
            currentSocket <- None

    socket.OnMessage <- fun message -> printfn $">> Received: %s{message}")

printfn "Server started on ws://0.0.0.0:8181"

let send (msg: string) =
    match currentSocket with
    | Some s ->
        s.Send(msg) |> ignore
        printfn $"Sent: %s{msg}"
    | None -> printfn "No client connected!"

let closeClient () =
    match currentSocket with
    | Some s ->
        s.Close()
        printfn "Closed client connection"
    | None -> printfn "No client connected!"

let stopServer () =
    server.Dispose()
    printfn "Server stopped"
