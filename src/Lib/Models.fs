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

type WorkspacesResponse =
    { Data: WorkspaceResponseData
      Success: bool
      Error: string option }

type FocusChangedEventData = { FocusedContainer: Window }

type FocusChangedEvent =
    { Data: FocusChangedEventData
      Success: bool
      Error: string option }

type ParsingOutput =
    | CurrentWorkspace of WorkspaceName
    | Unknown of string // TODO

module WorkspaceResponse =

    /// Extracts the current workspace's name (if found). It might return the index as the name if the name is too long.
    let extractCurrentWorkspace (wr: WorkspacesResponse) =
        wr.Data.Workspaces |> List.tryFind _.HasFocus

    /// Logic to extract the workspace name. If the name is longer then a single character it uses the first character.
    /// The display name is either defined or duplicates the full name.
    let extractWorkspaceName (workspace: Workspace) =
        let mutable name = workspace.Name.Trim()
        if name.Length <> 1 then name <- name[0] |> string

        let dn =
            workspace.DisplayName
            |> Option.bind (fun d -> if d.Length > 1 then Some d else None)
            |> Option.defaultValue workspace.Name

        { Name = name.ToLower()
          DisplayName = dn }

    /// Try to find a workspace by its ID.
    let tryGetWorkspace (id: Guid) (wr: WorkspacesResponse) =
        wr.Data.Workspaces |> List.tryFind (fun w -> w.Id = id)
