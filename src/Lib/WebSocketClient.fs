module GlazeWM.Tray.WebSocketClient

open System
open System.Net.WebSockets
open System.Text
open System.Threading
open Serilog
open GlazeWM.Tray.Literals


type WebSocketMessage =
    | SendMessage of string
    | Exit of AsyncReplyChannel<unit>

type WebSocketClient(uri: Uri, parser: MailboxProcessor<string>) =

    let client = new ClientWebSocket()
    let cts = new CancellationTokenSource()
    let errorEvent = Event<string>()

    let listen () =
        async {
            let mutable buffer = Array.zeroCreate<byte> 1024

            while not cts.IsCancellationRequested do
                try
                    let messageBuilder = StringBuilder()
                    let mutable result = Unchecked.defaultof<WebSocketReceiveResult>
                    let mutable receiving = true

                    while receiving do
                        let! currentResult = Async.AwaitTask(client.ReceiveAsync(ArraySegment<byte>(buffer), cts.Token))

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
                    Log.Information "Listening task cancelled"
                    raise ex
                | ex ->
                    Log.Error("Error during message reception: {Ex}", ex)
                    errorEvent.Trigger(ex.Message)
                    cts.Cancel()
        }

    // Create the agent once (per instance) and cache it.
    let agent =
        let a =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
                        do! Async.AwaitTask(client.ConnectAsync(uri, cts.Token))
                        Log.Information("Connected to {Url}", uri)
                        Async.Start(listen (), cancellationToken = cts.Token)

                        let rec loop () =
                            async {
                                let! msg = inbox.Receive()

                                match msg with
                                | SendMessage content ->
                                    let bytes = Encoding.UTF8.GetBytes(content)

                                    do!
                                        Async.AwaitTask(
                                            client.SendAsync(
                                                ArraySegment<byte>(bytes),
                                                WebSocketMessageType.Text,
                                                true,
                                                cts.Token
                                            )
                                        )

                                    Log.Debug("Sent message: {Content}", content)
                                    return! loop ()
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

                                    reply.Reply()
                            }

                        do! loop ()
                    }),
                cancellationToken = cts.Token
            )

        a.Error.Add(fun ex -> errorEvent.Trigger(ex.Message))
        a

    member _.Agent = agent

    /// Subscribe to GlazeWM events
    member _.SendMessage(msg: string) = SendMessage msg |> agent.Post

    [<CLIEvent>]
    member this.Error = errorEvent.Publish

    interface IDisposable with
        member _.Dispose() =
            try
                cts.Cancel()
            with _ ->
                ()

            client.Dispose()
            cts.Dispose()
