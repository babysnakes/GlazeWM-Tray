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
        wr.Data.Workspaces |> List.indexed |> List.tryFind (fun (_, w) -> w.HasFocus)

    /// Logic to extract the workspace name. The `Name` should be very short or calculated from `idx`. The `DisplayName`
    /// should be calculated by index or name if not provided.
    let extractWorkspaceName (idx: int) (workspace: Workspace) =
        if workspace.Name.Length <= 2 then
            let dn =
                workspace.DisplayName
                |> Option.bind (fun n -> if n.Trim().Length > 0 then Some n else None)

            let name =
                if workspace.Name.Trim() = "" then
                    $"{idx + 1}"
                else
                    workspace.Name

            { Name = name
              DisplayName = dn |> Option.defaultValue $"Workspace {idx + 1}" }
        else
            { Name = $"{idx + 1}"
              DisplayName = workspace.DisplayName |> Option.defaultValue workspace.Name }

    /// Try to find a workspace by its ID.
    let tryGetWorkspace (id: Guid) (wr: WorkspacesResponse) =
        wr.Data.Workspaces |> List.indexed |> List.tryFind (fun (_, w) -> w.Id = id)
