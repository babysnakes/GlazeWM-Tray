module GlazeWM.Tray.Models

open System

/// Defines the types of messages our WebSocket client agent can process.
type WebSocketMessage =
    /// Instructs the agent to send a message to the WebSocket server.
    | SendMessage of string
    /// Acknowledges a message received from the server.
    | ReceiveMessage of string
    /// Instructs the agent to gracefully shut down the connection and provides a reply channel to signal completion.
    | Exit of AsyncReplyChannel<unit>

let ServerUri = Uri("ws://localhost:6123/")
