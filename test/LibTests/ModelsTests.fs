namespace LibTests.ModelsTests

open System
open FsUnit
open GlazeWM.Tray.Models
open NUnit.Framework

module ``extractWorkspaceName tests`` =

    let mkWorkspace name displayName =
        { Id = Guid.NewGuid()
          Name = name
          DisplayName = displayName
          ParentId = Guid.NewGuid()
          HasFocus = false }

    let emptyDisplayNames () = [ None; Some "" ]

    [<Test>]
    let ``workspace with short name and display name`` () =
        let w = mkWorkspace "1" (Some "One")
        let result = WorkspaceResponse.extractWorkspaceName 0 w
        result.Name |> should equal "1"
        result.DisplayName |> should equal "One"

    [<TestCaseSource(nameof emptyDisplayNames)>]
    let ``workspace without or with empty display name show descriptive index+1 in display name`` (dn: string option) =
        let w = mkWorkspace "2" dn
        let result = WorkspaceResponse.extractWorkspaceName 1 w
        result.DisplayName |> should equal "Workspace 2"

    [<Test>]
    let ``workspace with empty display name show descriptive index+1 in display name`` () =
        let w = mkWorkspace "2" (Some "")
        let result = WorkspaceResponse.extractWorkspaceName 1 w
        result.DisplayName |> should equal "Workspace 2"

    [<Test>]
    let ``workspace with long name shows idx+1 as name and long name as description`` () =
        let w = mkWorkspace "A Long Workspace Name" None
        let result = WorkspaceResponse.extractWorkspaceName 0 w
        result.Name |> should equal "1"
        result.DisplayName |> should equal "A Long Workspace Name"

    [<Test>]
    let ``workspace with empty name shows idx+1 as name`` () =
        let w = mkWorkspace "" None
        let result = WorkspaceResponse.extractWorkspaceName 2 w
        result.Name |> should equal "3"
