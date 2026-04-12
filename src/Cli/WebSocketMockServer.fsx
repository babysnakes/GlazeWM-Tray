#r "nuget: Fleck, 1.2.0"

open System.IO
open Fleck

let mutable currentSocket: IWebSocketConnection option = None
let mutable server: WebSocketServer option = None

let readFixture fileName =
    let relativePath =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "test", "LibTests", "Fixtures", fileName)

    File.ReadAllText(relativePath)

let sampleWorkspaces = readFixture "basic-workspaces-response.json"
let basicFocusChangedEvent = readFixture "basic-focus-changed-event.json"
let unpausedResponse = readFixture "unpaused-query-response.json"
let defaultBindings = readFixture "binding-modes-default-query-response.json"
let newBindings = readFixture "binding-modes-custom-query-response.json"
let workspacesNoFocus = readFixture "basic-workspaces-response-with-no-focus.json"
let unsuccessfulResponse = readFixture "error-response-without-error.json"

let run () =
    let localServer = new WebSocketServer("ws://0.0.0.0:6123")

    localServer.Start(fun socket ->
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
    server <- Some localServer

let send (msg: string) =
    match currentSocket with
    | Some s ->
        s.Send(msg) |> ignore
        printfn $"Sent: %s{msg}"
    | None -> printfn "No client connected!"

let runAuto () =
    let localServer = new WebSocketServer("ws://0.0.0.0:8181")

    localServer.Start(fun socket ->
        socket.OnOpen <-
            fun () ->
                printfn $">> Client Connected: {socket.ConnectionInfo.ClientIpAddress}"
                currentSocket <- Some socket

        socket.OnClose <-
            fun () ->
                printfn ">> Client Disconnected"
                currentSocket <- None

        socket.OnMessage <-
            fun message ->
                match message with
                | "query workspaces" -> send sampleWorkspaces
                | "query paused" -> send unpausedResponse
                | "query binding-modes" -> send defaultBindings
                | _ -> printfn $">> Received: %s{message}")

    printfn "Server started on ws://0.0.0.0:8181"
    server <- Some localServer

let stop () =
    match currentSocket with
    | Some s ->
        s.Close()
        currentSocket <- None
        printfn "Closed client connection"
        server |> Option.iter (fun s -> s.Dispose())
        server <- None
    | None -> printfn "No client connected!"

let stopError () =
    match currentSocket with
    | Some s ->
        s.Close(500)
        currentSocket <- None
        printfn "Closed client connection with error"
        server |> Option.iter (fun s -> s.Dispose())
        server <- None
    | None -> printfn "No client connected!"

(* -- Snippets you can select and evaluate in FSI
send workspacesNoFocus
send unsuccessfulResponse
*)
