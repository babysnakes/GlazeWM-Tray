namespace GuiTests

open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.Headless
open Avalonia.Headless.NUnit
open FsUnit
open GlazeWM.TrayApp.Views
open GuiTests.Helpers
open NUnit.Framework

module WindowsPanelTests =
    [<AvaloniaTest>]
    let ``on start, trigger workspaces query`` () =
        let window = Window()
        let response = loadFixture "basic-workspaces-response.json"
        let tcs = System.Threading.Tasks.TaskCompletionSource()
        let runQuery =
            fun q ->
                q |> should equal "query workspaces"
                tcs.SetResult()
                // TestContext.Progress.WriteLine($"query: {q}")
                Ok(response)
        window.Content <- WindowsPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        waitForResult (fun _ -> tcs.Task.IsCompleted) 3
        |> expectTrue "did not seems to run query"

    [<AvaloniaTest>]
    let ``when waiting for result, display waiting view`` () =
        let window = Window()
        let response = loadFixture "basic-workspaces-response.json"
        let runQuery =
            fun q ->
                // TestContext.Progress.WriteLine($"query: {q}")
                Async.Sleep(100) |> Async.RunSynchronously
                Ok(response)
        window.Content <- WindowsPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        waitForResult
            (fun () ->
                allType<TextBlock> window
                // |> Seq.tap (fun tb -> TestContext.Out.WriteLine(tb.Text))
                |> Seq.exists (fun tb -> tb.Text.Contains "Waiting"))
            3
        |> expectTrue "does not show Waiting view"

    [<AvaloniaTest>]
    let ``Displays all workspaces and windows`` () =
        let window = Window()
        let response = loadFixture "basic-workspaces-response.json"
        let runQuery = fun _ -> Ok response
        window.Content <- WindowsPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        waitForResult (fun () -> allType<Expander> window |> Seq.length = 2) 6
        |> expectTrue "should show the correct number of workspaces"

        // make sure all expanders are expanded
        allType<Expander> window |> Seq.iter (fun e -> e.IsExpanded <- true)

        // This is a brittle(ish) test as it checks for element we might later change (Grid with name), and the number
        // of windows is from the test JSON :(
        waitForResult
            (fun () ->
                allType<Grid> window
                |> Seq.filter (fun g -> not (isNull g.Name) && g.Name.StartsWith "window-panel-id-")
                // |> Seq.tap (fun g -> TestContext.Progress.WriteLine($"grid: {g.Name}"))
                |> Seq.length = 7)
            5
        |> expectTrue "should show the correct number of windows"
    [<AvaloniaTest>]
    let ``Displays filtered workspaces and windows`` () =
        let window = Window()
        let response = loadFixture "basic-workspaces-response.json"
        let runQuery = fun _ -> Ok response
        window.Content <- WindowsPanel.view (mkViewHesHelpers runQuery)
        window.Show()
        let filterInput =
            allType<TextBox> window |> Seq.find (fun tb -> tb.Name = "windows-filter")

        filterInput.Focus() |> ignore
        window.KeyTextInput("Microsoft To Do")

        waitForResult
            (fun () ->
                allType<Expander> window
                |> Seq.filter _.IsEnabled
                // |> Seq.tap (fun e -> TestContext.Progress.WriteLine($"expander: {e.Header}, enabled: {e.IsEnabled}"))
                |> Seq.length = 1)
            30 // the high retries count is due to debounce in the filter observable
        |> expectTrue "There should be only one enabled workspace"

        // make sure all expanders are expanded
        allType<Expander> window |> Seq.iter (fun e -> e.IsExpanded <- true)

        // there should be only one window that matches this title (in the JSON)
        waitForResult
            (fun () ->
                allType<Grid> window
                |> Seq.filter (fun g -> not (isNull g.Name) && g.Name.StartsWith "window-panel-id-")
                // |> Seq.tap (fun g -> TestContext.Progress.WriteLine($"grid: {g.Name}"))
                |> Seq.length = 1)
            5
        |> expectTrue "Only a single filtered window should be shown"

    [<AvaloniaTest>]
    let ``on error, displays the error message`` () =
        let window = Window()
        let errorMessage = "An error occured"
        let runQuery = fun q -> Error errorMessage
        window.Content <- WindowsPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        waitForResult (fun _ -> allType<TextBlock> window |> Seq.exists (fun tb -> tb.Text = errorMessage)) 5
        |> expectTrue "Should display the error message"
