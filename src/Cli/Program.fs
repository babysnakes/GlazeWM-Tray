// For more information see https://aka.ms/fsharp-console-apps
open System
open GlazeWM.Tray.Models
open GlazeWM.Tray.SubscriptionAgent

agent.Post(
    SendMessage
        "sub -e workspace_updated workspace_activated workspace_deactivated binding_modes_changed pause_changed focus_changed"
)

printfn "Type message to send to the server (or 'Exit' to quit): "

let rec ReadAndSendLoop () =
    let input = Console.ReadLine()

    match input.ToLower() with
    | "exit" -> agent.PostAndReply(Exit)
    | _ ->
        agent.Post(SendMessage input)
        ReadAndSendLoop()

ReadAndSendLoop()
