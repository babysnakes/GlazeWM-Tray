namespace GlazeWM.Tray.WebSocketClient

open System
open System.Net.WebSockets
open System.Text
open System.Threading
open Serilog


type WebSocketMessage =
    | SendMessage of string
    | Exit of AsyncReplyChannel<unit>

type SyncMessage = SyncQuery of string * AsyncReplyChannel<Result<string, string>>

type WebSocketClient(uri: Uri, parser: MailboxProcessor<string>) =

    let client = new ClientWebSocket()
    let syncClient = new ClientWebSocket()
    let cts = new CancellationTokenSource()
    let errorEvent = Event<string>()

    let readMessage (socket: ClientWebSocket) =
        async {
            let buffer = Array.zeroCreate<byte> 1024
            let sb = StringBuilder()
            let mutable lastResult = Unchecked.defaultof<WebSocketReceiveResult>
            let mutable receiving = true

            while receiving do
                let! result = Async.AwaitTask(socket.ReceiveAsync(ArraySegment<byte>(buffer), cts.Token))

                lastResult <- result
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count)) |> ignore
                receiving <- not result.EndOfMessage

            return lastResult, sb.ToString()
        }

    let listen () =
        async {
            while not cts.IsCancellationRequested do
                try
                    let! result, jsonString = readMessage client

                    if result.MessageType = WebSocketMessageType.Close then
                        Log.Warning "GlazeWM closed the connection."
                        errorEvent.Trigger("GlazeWM closed the connection.")
                        cts.Cancel()
                    else
                        parser.Post(jsonString)

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

    let syncAgent =
        let a =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
                        do! Async.AwaitTask(syncClient.ConnectAsync(uri, cts.Token))

                        let rec loop () =
                            async {
                                let! (SyncQuery(query, reply)) = inbox.Receive()

                                try
                                    let bytes = Encoding.UTF8.GetBytes(query)

                                    do!
                                        Async.AwaitTask(
                                            syncClient.SendAsync(
                                                ArraySegment<byte>(bytes),
                                                WebSocketMessageType.Text,
                                                true,
                                                cts.Token
                                            )
                                        )

                                    let! _, response = readMessage syncClient
                                    Log.Debug("Sync query: query='{Query}', response='{Response}'", query, response)
                                    reply.Reply(Ok response)
                                    return! loop ()
                                with
                                | :? OperationCanceledException as ex ->
                                    reply.Reply(Error "Sync client Cancelled")
                                    raise ex
                                | ex ->
                                    Log.Error("Sync query error: {Ex}", ex)
                                    errorEvent.Trigger(ex.Message)
                                    reply.Reply(Error ex.Message)
                                    cts.Cancel()
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

    /// Send a query and synchronously wait for a single response. Optionally provide timeout in milliseconds
    /// (default is 5000ms).
    member _.Query(message: string, ?timeoutMs: int) : Result<string, string> =
        let timeout = defaultArg timeoutMs 5000
        try
            syncAgent.PostAndReply((fun reply -> SyncQuery(message, reply)), timeout)
        with :? TimeoutException ->
            let msg = $"Sync query timeout (after {timeout}ms)"
            cts.Cancel()
            errorEvent.Trigger(msg)
            Error msg

    [<CLIEvent>]
    member this.Error = errorEvent.Publish

    interface IDisposable with
        member _.Dispose() =
            try
                cts.Cancel()
            with _ ->
                ()

            client.Dispose()
            syncClient.Dispose()
            cts.Dispose()
