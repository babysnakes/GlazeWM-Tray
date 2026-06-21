namespace Lib.Tray

open System
open GlazeWM.Tray.MessageParser
open GlazeWM.Tray.Models
open GlazeWM.Tray.WebSocketClient

type GlazeWMClient(uri: Uri) =
    let wsClient = new WebSocketClient(uri)
    let parser = new Parser(wsClient)

    /// GlazeWM parsed responses
    member _.GlazeWmMessages = parser.GlazewmMessages

    /// Connection failures (should abort on first)
    member _.Failures = wsClient.Failures

    /// Parser warnings
    member _.Warnings = parser.Warnings

    /// Send message to GlazeWM (Async)
    member _.SendMessage = (wsClient :> IWsClient).SendMessage

    /// Send a query to GlazeWM and wait for a single response
    member _.Query = wsClient.Query

    interface IDisposable with
        member _.Dispose() =
            (parser :> IDisposable).Dispose()
            (wsClient :> IDisposable).Dispose()
