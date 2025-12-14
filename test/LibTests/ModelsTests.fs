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
    let ``short name and display name returns provided`` () =
        let w = mkWorkspace "1" (Some "One")
        let result = WorkspaceResponse.extractWorkspaceName w
        result.Name |> should equal "1"
        result.DisplayName |> should equal "One"

    [<TestCaseSource(nameof emptyDisplayNames)>]
    let ``none or with empty display name duplicates name as display name`` (dn: string option) =
        let w = mkWorkspace "2" dn
        let result = WorkspaceResponse.extractWorkspaceName w
        result.Name |> should equal "2"
        result.DisplayName |> should equal "2"

    [<Test>]
    let ``long name and no display name returns first letter lowercase as name and name as display name`` () =
        let w = mkWorkspace "A Long Workspace Name" None
        let result = WorkspaceResponse.extractWorkspaceName w
        result.Name |> should equal "a"
        result.DisplayName |> should equal "A Long Workspace Name"

    [<Test>]
    let ``long name and display name returns first letter lowercase as name and provided display name`` () =
        let w = mkWorkspace "A Long Workspace Name" (Some "Display Name")
        let result = WorkspaceResponse.extractWorkspaceName w
        result.Name |> should equal "a"
        result.DisplayName |> should equal "Display Name"
