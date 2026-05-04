#r "nuget: Fleck, 1.2.0"

open System.IO
open System.Text.Json
open Fleck

let mutable asyncClient: IWebSocketConnection option = None
let mutable syncClient: IWebSocketConnection option = None
let mutable connections: IWebSocketConnection list = []
let mutable server: WebSocketServer option = None

let readFixture fileName =
    let relativePath =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "test", "LibTests", "Fixtures", fileName)

    let doc = JsonDocument.Parse(File.ReadAllText(relativePath))
    JsonSerializer.Serialize(doc.RootElement)

let sampleWorkspaces = readFixture "basic-workspaces-response.json"
let basicFocusChangedEvent = readFixture "basic-focus-changed-event.json"
let unpausedResponse = readFixture "unpaused-query-response.json"
let defaultBindings = readFixture "binding-modes-default-query-response.json"
let newBindings = readFixture "binding-modes-custom-query-response.json"
let workspacesNoFocus = readFixture "basic-workspaces-response-with-no-focus.json"
let unsuccessfulResponse = readFixture "error-response-without-error.json"

let tryAssignSync (asyncSocket: IWebSocketConnection) =
    if syncClient.IsNone then
        connections
        |> List.tryFind (fun c -> c.ConnectionInfo.Id <> asyncSocket.ConnectionInfo.Id)
        |> Option.iter (fun s ->
            syncClient <- Some s
            printfn $">> Sync client identified: {s.ConnectionInfo.ClientIpAddress}")

let assignSync () =
    match asyncClient with
    | Some s -> tryAssignSync s
    | None -> printfn "Error: async client not yet identified!"

let send (socket: IWebSocketConnection option) (msg: string) =
    match socket with
    | Some s ->
        s.Send(msg) |> ignore
        printfn $"Sent: %s{msg}"
    | None -> printfn "No client connected!"

let run () =
    let localServer = new WebSocketServer("ws://0.0.0.0:8181")

    localServer.Start(fun socket ->
        socket.OnOpen <-
            fun () ->
                connections <- socket :: connections

                match asyncClient with
                | Some _ ->
                    syncClient <- Some socket
                    printfn $">> Sync client connected: {socket.ConnectionInfo.ClientIpAddress}"
                | None -> printfn $">> Client connected (role TBD): {socket.ConnectionInfo.ClientIpAddress}"

        socket.OnClose <-
            fun () ->
                connections <-
                    connections
                    |> List.filter (fun c -> c.ConnectionInfo.Id <> socket.ConnectionInfo.Id)

                if
                    asyncClient
                    |> Option.exists (fun c -> c.ConnectionInfo.Id = socket.ConnectionInfo.Id)
                then
                    asyncClient <- None
                    printfn ">> Async client disconnected"
                elif
                    syncClient
                    |> Option.exists (fun c -> c.ConnectionInfo.Id = socket.ConnectionInfo.Id)
                then
                    syncClient <- None
                    printfn ">> Sync client disconnected"
                else
                    printfn ">> Client disconnected"

        socket.OnMessage <-
            fun message ->
                if message.StartsWith("sub ") then
                    asyncClient <- Some socket
                    printfn $">> Async client identified: {socket.ConnectionInfo.ClientIpAddress}"
                    tryAssignSync socket

                match message with
                | m when m.StartsWith("sub ") -> ()
                | "query workspaces" -> send (Some socket) sampleWorkspaces
                | "query paused" -> send (Some socket) unpausedResponse
                | "query binding-modes" -> send (Some socket) defaultBindings
                | _ -> printfn $">> Received: %s{message}")

    printfn "Server started on ws://0.0.0.0:8181"
    server <- Some localServer

let stop () =
    match asyncClient with
    | Some s ->
        s.Close()
        printfn "Closed async client connection"
    | None -> printfn "Error: async client not connected!"

let stopError () =
    match asyncClient with
    | Some s ->
        s.Close(500)
        printfn "Closed async client connection with error"
    | None -> printfn "Error: async client not connected!"

(* -- Snippets you can select and evaluate in FSI
run ()
assignSync ()
send asyncClient sampleWorkspaces
send asyncClient basicFocusChangedEvent
send asyncClient workspacesNoFocus
send asyncClient unsuccessfulResponse
stop ()
stopError ()
*)
