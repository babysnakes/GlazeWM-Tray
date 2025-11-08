module GlazeWM.Tray.SubscriptionAgent

open System
open System.Net.WebSockets
open System.Text
open System.Threading

open GlazeWM.Tray.Models

let agent =
    MailboxProcessor.Start(fun inbox ->
        async {
            use client = new ClientWebSocket()
            use cts = new CancellationTokenSource()

            // Wait for the client to connect
            do! Async.AwaitTask(client.ConnectAsync(ServerUri, cts.Token))
            printfn $"Connected to {ServerUri.ToString}"

            // Define a separate async task to listen for incoming messages.
            // This task will run in parallel with the agent's main loop.
            let listenTask =
                async {
                    let mutable buffer = Array.zeroCreate<byte> 1024

                    try
                        while not cts.IsCancellationRequested do
                            // Listen for a message from the server
                            let! result = Async.AwaitTask(client.ReceiveAsync(ArraySegment<byte>(buffer), cts.Token))
                            let jsonString = Encoding.UTF8.GetString(buffer, 0, result.Count)
                            // Post the received message back to the agent for processing.
                            inbox.Post(ReceiveMessage jsonString)

                            // Check if the server closed the connection.
                            if result.MessageType = WebSocketMessageType.Close then
                                printfn "Server closed the connection."
                                // This will cause the listener to exit its loop
                                cts.Cancel()

                    with ex ->
                        printfn $"Error during message reception: {ex.Message}"
                        cts.Cancel()
                }

            // Start the listening task in the background.
            Async.Start(listenTask)

            // The main message processing loop for the agent.
            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | SendMessage content ->
                        // Send a message over the WebSocket.
                        let bytes = Encoding.UTF8.GetBytes(content)

                        let! _ =
                            Async.AwaitTask(
                                client.SendAsync(ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token)
                            )

                        printfn $"Sent message: {content}"
                        return! loop ()
                    | ReceiveMessage jsonString ->
                        // Print the JSON received from the server.
                        printfn $"Received JSON: {jsonString}"
                        return! loop ()
                    | Exit reply ->
                        // Gracefully shut down the connection and signal completion to the caller.
                        printfn "Shutting down client..."

                        do!
                            Async.AwaitTask(
                                client.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Client initiated close",
                                    cts.Token
                                )
                            )

                        reply.Reply() // Signal to the caller that the shutdown is complete
                }

            do! loop ()
        })
