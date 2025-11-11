module GlazeWM.Tray.WebSocketClient

open System
open System.Net.WebSockets
open System.Text
open System.Threading

/// Defines the types of messages our WebSocket client agent can process.
type WebSocketMessage =
    /// Instructs the agent to send a message to the WebSocket server.
    | SendMessage of string
    /// Acknowledges a message received from the server.
    | ReceiveMessage of string
    /// Instructs the agent to gracefully shut down the connection and provides a reply channel to signal completion.
    | Exit of AsyncReplyChannel<unit>

let newClient (url: Uri) =
    MailboxProcessor.Start(fun inbox ->
        async {
            use client = new ClientWebSocket()
            use cts = new CancellationTokenSource()

            do! Async.AwaitTask(client.ConnectAsync(url, cts.Token))
            printfn $"Connected to {url}"

            let listenTask =
                async {
                    let mutable buffer = Array.zeroCreate<byte> 1024

                    while not cts.IsCancellationRequested do
                        try
                            let messageBuilder = StringBuilder()
                            let mutable result = Unchecked.defaultof<WebSocketReceiveResult>
                            let mutable receiving = true

                            while receiving do
                                let! currentResult =
                                    Async.AwaitTask(client.ReceiveAsync(ArraySegment<byte>(buffer), cts.Token))

                                result <- currentResult
                                let chunk = Encoding.UTF8.GetString(buffer, 0, currentResult.Count)
                                messageBuilder.Append(chunk) |> ignore
                                receiving <- not currentResult.EndOfMessage

                            let jsonString = messageBuilder.ToString()
                            inbox.Post(ReceiveMessage jsonString)

                            if result.MessageType = WebSocketMessageType.Close then
                                printfn "Server closed the connection."
                                cts.Cancel()

                        with ex ->
                            printfn $"Error during message reception: {ex.Message}"
                            cts.Cancel()
                }

            Async.Start(listenTask)

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
                        printfn $"Received JSON: {jsonString}"
                        return! loop ()
                    | Exit reply ->
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
