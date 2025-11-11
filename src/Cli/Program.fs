// For more information see https://aka.ms/fsharp-console-apps
open System
open GlazeWM.Tray.WebSocketClient

let uri = Uri("ws://localhost:6123/")
let client = newClient uri

client.Post(
    SendMessage
        "sub -e workspace_updated workspace_activated workspace_deactivated binding_modes_changed pause_changed focus_changed"
)
// IMPORTANT: listen to error events
client.Error.Add(fun exn ->
    printfn $"client error {exn.Message}"
    Environment.Exit(1))

printfn "Type message to send to the server (or 'Exit' to quit): "

let rec ReadAndSendLoop () =
    let input = Console.ReadLine()

    match input.ToLower() with
    | "exit" -> client.PostAndReply(Exit)
    | _ ->
        client.Post(SendMessage input)
        ReadAndSendLoop()

ReadAndSendLoop()
