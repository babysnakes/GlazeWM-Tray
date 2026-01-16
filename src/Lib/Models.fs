namespace GlazeWM.Tray.Models

open System
open FsToolkit.ErrorHandling

type WorkspaceName = { Name: string; DisplayName: string }

type Workspace =
    { Id: Guid
      Name: string
      DisplayName: string option
      ParentId: Guid
      HasFocus: bool }

type Window =
    { Id: Guid
      ParentId: Guid
      Title: string
      HasFocus: bool }

type WorkspaceResponseData = { Workspaces: Workspace list }

type WorkspacesResponse = { Data: WorkspaceResponseData }

type FocusChangedEventData = { FocusedContainer: Window }

type WorkspacesNotification =
    { Current: WorkspaceName
      Active: WorkspaceName list }

type FocusChangedEvent = { Data: FocusChangedEventData }

type PauseChangedEventData = { IsPaused: bool }

type PauseChangedEvent = { Data: PauseChangedEventData }

type BindingMode = { Name: string }

type BindingModeQueryData = { BindingModes: BindingMode list }

type BindingModesQueryResponse = { Data: BindingModeQueryData }

type BindingModesChangedEventData = { NewBindingModes: BindingMode list }

type BindingModesChangedEvent = { Data: BindingModesChangedEventData }

type AppNotification =
    | RefreshState
    | Workspaces of WorkspacesNotification
    | Paused of bool
    | NewBindingModes of bool
    | UnSuccessfulResponse of string

module WorkspaceResponse =

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
