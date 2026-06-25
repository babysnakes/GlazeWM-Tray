namespace GuiTests

open Avalonia.Controls
open Avalonia.Headless
open Avalonia.Headless.NUnit
open Avalonia.Interactivity
open FsUnit
open GlazeWM.TrayApp.Views
open GuiTests.Helpers


module QueryPanelTests =
    let extractQueryInput (window: Window) =
        allType<TextBox> window |> Seq.exactlyOne

    let extractQueryButton window = getButton window "query-button"

    [<AvaloniaTest>]
    let ``on start, indicates that it's waiting for query`` () =
        let window = Window()
        let runQuery = fun _ -> Ok("")
        window.Content <- QueryPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        allType<TextBlock> window
        // |> Seq.tap (fun tb -> TestContext.Out.WriteLine(tb.Text))
        |> Seq.exists (fun tb -> tb.Text.Contains "enter a query")
        |> should be True

    [<AvaloniaTest>]
    let ``when initialized, all buttons are disabled`` () =
        let window = Window()
        let runQuery = fun _ -> Ok("")
        window.Content <- QueryPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        allType<Button> window
        |> Seq.forall (fun b -> b.IsEnabled |> not)
        |> should be True

    [<AvaloniaTest>]
    let ``when input is not empty, the query button should be enabled`` () =
        let window = Window()
        let runQuery = fun _ -> Ok("")
        window.Content <- QueryPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        let queryTextBox = extractQueryInput window
        let queryButton = extractQueryButton window
        queryTextBox.Focus() |> ignore
        window.KeyTextInput("query workspaces")
        queryButton.IsEnabled |> should be True

    [<AvaloniaTest>]
    let ``Once a query is sent, there should be a waiting indicator`` () =
        let window = Window()
        let runQuery =
            fun _ ->
                Async.Sleep(1000) |> Async.RunSynchronously
                Ok("")
        window.Content <- QueryPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        let queryTextBox = extractQueryInput window
        let queryButton = extractQueryButton window
        queryTextBox.Focus() |> ignore
        window.KeyTextInput("query workspaces")
        queryButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

        waitForResult
            (fun () ->
                allType<TextBlock> window
                // |> Seq.tap (fun tb -> TestContext.Out.WriteLine(tb.Text))
                |> Seq.exists (fun tb -> tb.Text.Contains "Waiting"))
            5
        |> expectTrue "Waiting view does not show"

    [<AvaloniaTest>]
    let ``when a query returns an error, the error mesn sage should be displayed`` () =
        let window = Window()
        let errorMessage = "error occured"
        let runQuery = fun _ -> Error errorMessage

        window.Content <- QueryPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        let queryTextBox = extractQueryInput window
        let queryButton = extractQueryButton window
        queryTextBox.Focus() |> ignore
        window.KeyTextInput("query workspaces")
        queryButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

        waitForResult
            (fun () ->
                allType<TextBlock> window
                // |> Seq.tap (fun tb -> TestContext.Out.WriteLine(tb.Text))
                |> Seq.exists (fun tb -> tb.Text.Contains errorMessage))
            5
        |> expectTrue "error message does not show"

    [<AvaloniaTest>]
    let ``on query result, all buttons should be enabled`` () =
        let window = Window()
        let jsonText = loadFixture "focused-response.json"
        let runQuery = fun _ -> Ok(jsonText)
        window.Content <- QueryPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        let queryTextBox = extractQueryInput window
        let queryButton = extractQueryButton window
        queryTextBox.Focus() |> ignore
        window.KeyTextInput("query focused")
        queryButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

        // Just to make sure nothing funny happened, we should check for rendered data
        waitForResult (fun _ -> allType<TreeViewItem> window |> Seq.isEmpty |> not) 20
        |> expectTrue "waiting for tree view"
        waitForResult (fun _ -> allType<Button> window |> Seq.forall (fun b -> b.IsEnabled)) 5
        |> expectTrue "waiting for buttons to be enabled"

    [<AvaloniaTest>]
    let ``on query result, when pressing the collapse button, it should collapse the tree`` () =
        let window = Window()
        let jsonText = loadFixture "focused-response.json"
        let runQuery = fun _ -> Ok(jsonText)
        window.Content <- QueryPanel.view (mkViewHesHelpers runQuery)
        window.Show()

        let queryTextBox = extractQueryInput window
        let queryButton = extractQueryButton window
        let foldButton = getButton window "fold-button"
        queryTextBox.Focus() |> ignore
        window.KeyTextInput("query focused")
        queryButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

        waitForResult (fun _ -> allType<TreeViewItem> window |> Seq.isEmpty |> not) 20
        |> expectTrue "waiting for tree view"

        let tv = allType<TreeView> window |> Seq.exactlyOne
        let data = allType<TreeViewItem> window |> Seq.exactlyOne
        tv.ExpandSubTree(data)

        waitForResult (fun _ -> allType<TreeViewItem> window |> Seq.length > 1) 5
        |> expectTrue "waiting for opened tree item"

        foldButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
        // the following should return false, but we just want to give it enough time to re-render
        waitForResult (fun _ -> allType<TreeViewItem> window |> Seq.length > 1) 10
        |> ignore
        // now the real test
        allType<TreeViewItem> window |> Seq.length |> should equal 1
