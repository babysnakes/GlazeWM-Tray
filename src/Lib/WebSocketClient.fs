module GlazeWM.Tray.WebSocketClient

open System
open System.Net.WebSockets
open System.Text
open System.Threading
open Serilog


type WebSocketMessage =
    | SendMessage of string
    | Exit of AsyncReplyChannel<unit>
    | Fail of exn

let newClient (url: Uri) (parser: MailboxProcessor<string>) =
    MailboxProcessor.Start(fun inbox ->
        async {
            use client = new ClientWebSocket()
            use cts = new CancellationTokenSource()

            do! Async.AwaitTask(client.ConnectAsync(url, cts.Token))
            Log.Information("Connected to {Url}", url)

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
                            parser.Post(jsonString)

                            if result.MessageType = WebSocketMessageType.Close then
                                Log.Information "Server closed the connection."
                                cts.Cancel()

                        with
                        | :? OperationCanceledException as ex ->
                            Log.Information "Listening task cancelled gracefully"
                            raise ex
                        | ex ->
                            Log.Error("Error during message reception: {Ex}", ex)
                            cts.Cancel()
                            inbox.Post(Fail ex)
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

                        Log.Debug("Sent message: {Content}", content)
                        return! loop ()
                    | Fail ex ->
                        Log.Error("WebSocket client received Fail with {Ex}", ex)
                        raise ex
                    | Exit reply ->
                        Log.Information "Shutting down client..."

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
