namespace GlazeWM.Tray.WebSocketClient

open System
open System.Net.WebSockets
open System.Reactive.Linq
open System.Reactive.Subjects
open System.Text
open System.Threading
open GlazeWM.Tray.Models
open Serilog


type SyncMessage = SyncQuery of string * AsyncReplyChannel<Result<string, string>>

type WebSocketClient(uri: Uri) =

    let client = new ClientWebSocket()
    let syncClient = new ClientWebSocket()
    let cts = new CancellationTokenSource()
    let receivedMessages = new Subject<string>()
    let failures = new Subject<exn>()

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
                        Exception("GlazeWM closed the connection.") |> failures.OnNext
                        cts.Cancel()
                    else
                        receivedMessages.OnNext(jsonString)

                with
                | :? OperationCanceledException as ex ->
                    Log.Information "Listening task cancelled"
                    raise ex
                | ex ->
                    Log.Error("Error during message reception: {Ex}", ex)
                    failures.OnNext(ex)
                    cts.Cancel()
        }

    // Create the agent once (per instance) and cache it.
    let agent =
        let a =
            MailboxProcessor.Start(
                (fun (inbox: MailboxProcessor<string>) ->
                    async {
                        do! Async.AwaitTask(client.ConnectAsync(uri, cts.Token))
                        Log.Information("Connected to {Url}", uri)
                        Async.Start(listen (), cancellationToken = cts.Token)

                        while not cts.IsCancellationRequested do
                            let! msg = inbox.Receive()
                            let bytes = Encoding.UTF8.GetBytes(msg)

                            do!
                                Async.AwaitTask(
                                    client.SendAsync(
                                        ArraySegment<byte>(bytes),
                                        WebSocketMessageType.Text,
                                        true,
                                        cts.Token
                                    )
                                )

                            Log.Debug("Sent message: {Content}", msg)
                    }),
                cancellationToken = cts.Token
            )

        a.Error.Subscribe(failures) |> ignore
        a

    let syncAgent =
        let a =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
                        do! Async.AwaitTask(syncClient.ConnectAsync(uri, cts.Token))

                        while not cts.IsCancellationRequested do
                            let! (SyncQuery(query, reply)) = inbox.Receive()
                            let bytes = Encoding.UTF8.GetBytes(query)

                            try
                                do!
                                    syncClient.SendAsync(
                                        ArraySegment<byte>(bytes),
                                        WebSocketMessageType.Text,
                                        true,
                                        cts.Token
                                    )
                                    |> Async.AwaitTask

                                let! _, response = readMessage syncClient
                                Log.Debug("Sync query: query='{Query}', response='{Response}'", query, response)
                                reply.Reply(Ok response)
                            with ex ->
                                Log.Error(ex, "Sync query failed")
                                reply.Reply(Error ex.Message)
                                cts.Cancel()
                                failures.OnNext ex

                    }),
                cancellationToken = cts.Token
            )

        a.Error.Subscribe(failures) |> ignore
        a

    /// disconnect all connections
    let disconnect () =
        let closeAsync (socket: ClientWebSocket) =
            socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client initiated close", CancellationToken.None)
            |> Async.AwaitTask

        [| closeAsync client; closeAsync syncClient |]
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore

        cts.Cancel()

    /// Subscribe to GlazeWM connection errors
    member _.Failures = failures.AsObservable()

    /// Send a query and synchronously wait for a single response. Optionally provide timeout in milliseconds
    /// (default is 5000ms).
    member _.Query(message: string, ?timeoutMs: int) : Result<string, string> =
        let timeout = defaultArg timeoutMs 5000
        try
            syncAgent.PostAndReply((fun reply -> SyncQuery(message, reply)), timeout)
        with :? TimeoutException ->
            let msg = $"Sync query timeout (after {timeout}ms)"
            cts.Cancel()
            Exception(msg) |> failures.OnNext
            Error msg

    interface IWsClient with
        /// Subscribe to GlazeWM JSON messages
        member _.ReceivedMessages = receivedMessages.AsObservable()

        /// Send Async messages to GlazeWM
        member _.SendMessage msg = agent.Post msg

    interface IDisposable with
        member _.Dispose() =
            try
                disconnect ()
            with _ ->
                ()

            client.Dispose()
            syncClient.Dispose()
            cts.Dispose()
            receivedMessages.OnCompleted()
            failures.OnCompleted()
