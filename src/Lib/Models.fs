namespace GlazeWM.Tray.Models

open System
open Farse
open Farse.Operators
open FSharp.Data
open FsToolkit.ErrorHandling

/// `WebSocketClient` must implement this interface
type IWsClient =
    abstract member ReceivedMessages: IObservable<string>
    abstract member SendMessage: msg: string -> unit

type WorkspaceName = { Name: string; DisplayName: string }

type Workspace =
    { Id: Guid
      Name: string
      DisplayName: string option
      ParentId: Guid
      HasFocus: bool }

type WorkspaceResponseData = { Workspaces: Workspace list }

type WorkspacesResponse = { Data: WorkspaceResponseData }

type WorkspacesNotification =
    { Current: WorkspaceName
      Active: WorkspaceName list }

type PauseChangedEventData = { IsPaused: bool }

type PauseChangedEvent = { Data: PauseChangedEventData }

type BindingMode = { Name: string }

type BindingModeQueryData = { BindingModes: BindingMode list }

type BindingModesQueryResponse = { Data: BindingModeQueryData }

type BindingModesChangedEventData = { NewBindingModes: BindingMode list }

type BindingModesChangedEvent = { Data: BindingModesChangedEventData }

type ParsedMessage =
    | Workspaces of WorkspacesNotification
    | Paused of bool
    | NewBindingModes of bool

type GlazeWMRawResponse =
    | Data of JsonValue
    | ErrorMsg of string

module WorkspaceResponse =

    let empty: WorkspacesResponse =
        let data = { Workspaces = [] }
        { Data = data }

    let workspaceParser =
        parser {
            let! id = "id" &= Parse.guid
            and! name = "name" &= Parse.string
            and! displayName = "displayName" ?= Parse.string
            and! parentId = "parentId" &= Parse.guid
            and! hasFocus = "hasFocus" &= Parse.bool

            return
                { Id = id
                  Name = name
                  DisplayName = displayName
                  ParentId = parentId
                  HasFocus = hasFocus }
        }

    let dataParser =
        parser {
            let! workspaces = "workspaces" &= Parse.list workspaceParser

            return { Workspaces = workspaces }
        }

    let wrParser: Parser<WorkspacesResponse> =
        parser {
            let! data = "data" &= dataParser
            return { Data = data }
        }

    /// Extracts the current workspace's name (if found). It might return the index as the name if the name is too long.
    let extractCurrentWorkspace (wr: WorkspacesResponse) =
        wr.Data.Workspaces |> List.tryFind _.HasFocus

    /// Logic to extract the workspace name. If the name is longer than a single character, it uses the first character.
    /// The display name is either defined or duplicates the full name.
    let extractWorkspaceName (workspace: Workspace) =
        let mutable name = workspace.Name.Trim()
        if name.Length <> 1 then name <- name[0] |> string

        let dn =
            workspace.DisplayName
            |> Option.bind (fun d -> if d.Length > 0 then Some d else None)
            |> Option.defaultValue workspace.Name

        { Name = name.ToLower()
          DisplayName = dn }

    /// Try to Extract current workspace by GUID + active workspaces.
    let tryGetWorkspaceNotificationByGuid (id: Guid) (wr: WorkspacesResponse) =
        let active = wr.Data.Workspaces |> List.map extractWorkspaceName

        wr.Data.Workspaces
        |> List.tryFind (fun w -> w.Id = id)
        |> Option.map extractWorkspaceName
        |> Option.map (fun wn -> { Active = active; Current = wn })

    /// Extract both current workspace and active workspaces from the response. Returns none if no current workspace is
    /// found.
    let tryGetWorkspacesNotification (wr: WorkspacesResponse) =
        let active = wr.Data.Workspaces |> List.map extractWorkspaceName

        wr
        |> extractCurrentWorkspace
        |> Option.map (fun current ->
            let wn = current |> extractWorkspaceName
            { Active = active; Current = wn })

module FocusChangedEventData =

    // Tries to parse the focused window. Assumes a successful response.
    let windowParser =
        parser {
            let! containerType = "data.focusedContainer.type" &= Parse.string

            if containerType = "window" then
                let! parentId = "data.focusedContainer.parentId" &= Parse.guid
                return Some parentId
            else
                return None
        }

module BindingModeQueryResponse =
    open Farse.Parse

    let bmParser =
        parser {
            let! name = "name" &= string
            return { Name = name }
        }

    let dataParser =
        parser {
            let! bindingModes = "bindingModes" &= list bmParser
            return { BindingModes = bindingModes }
        }

    let parser: Parser<BindingModesQueryResponse> =
        parser {
            let! data = "data" &= dataParser
            return { Data = data }
        }

module BindingModesChangedEvent =
    open Farse.Parse

    let bmParser =
        parser {
            let! name = "name" &= string
            return { Name = name }
        }

    let dataParser =
        parser {
            let! bindingModes = "newBindingModes" &= list bmParser
            return { NewBindingModes = bindingModes }
        }

    let parser: Parser<BindingModesChangedEvent> =
        parser {
            let! data = "data" &= dataParser
            return { Data = data }
        }

module PauseChangedEvent =
    open Farse.Parse

    let dataParser =
        parser {
            let! isPaused = "isPaused" &= bool
            return { IsPaused = isPaused }
        }

    let parser: Parser<PauseChangedEvent> =
        parser {
            let! data = "data" &= dataParser
            return { Data = data }
        }
