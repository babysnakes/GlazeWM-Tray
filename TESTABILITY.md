# Testability Comparison: ReactiveUI ViewModel vs FuncUI View

Comparing `src/TrayAppRUI/ViewModels/QueryPanelViewModel.fs` (ReactiveUI) against
`src/TrayApp/QueryPanel.fs` (FuncUI) for plain F# unit testability — no Avalonia headless
testing involved.

## The Core Difference

**`QueryPanelViewModel`** separates logic from UI. The query function is injected, state is
plain properties on a POCO — you can instantiate it and poke it directly.

**`QueryPanel.view`** embeds all logic *inside* a `Component(fun ctx -> ...)` closure. The
state (`ctx.useState`) lives inside Avalonia's component infrastructure, which has no
existence outside a running Avalonia application.

---

## ReactiveUI ViewModel — testable, with one caveat

The ViewModel is almost entirely unit-testable. There's one leak: `RunQuery` uses
`Avalonia.Threading.Dispatcher.UIThread.Post` to marshal back to the UI thread. That's an
Avalonia dependency that will throw in a plain NUnit process.

### What you can test with zero Avalonia

```fsharp
module ViewModelTests.QueryPanelTests

open NUnit.Framework
open FsUnit
open FSharpReactiveUI.ViewModels

[<TestFixture>]
type ``QueryPanelViewModel - structural tests``() =

    [<Test>]
    member _.``initial state is Empty``() =
        let vm = QueryPanelViewModel(fun _ -> Ok "")
        vm.IsEmpty    |> should equal true
        vm.IsQuerying |> should equal false
        vm.IsData     |> should equal false
        vm.IsError    |> should equal false

    [<Test>]
    member _.``command is disabled when input is blank``() =
        let vm = QueryPanelViewModel(fun _ -> Ok "")
        let mutable canExecute = true
        vm.ExecuteQueryCommand.CanExecute.Subscribe(fun v -> canExecute <- v) |> ignore
        canExecute |> should equal false

    [<Test>]
    member _.``command becomes enabled when input has text``() =
        let vm = QueryPanelViewModel(fun _ -> Ok "")
        let mutable canExecute = false
        vm.ExecuteQueryCommand.CanExecute.Subscribe(fun v -> canExecute <- v) |> ignore
        vm.QueryInput <- "get workspaces"
        canExecute |> should equal true

    [<Test>]
    member _.``command becomes disabled again when input is cleared``() =
        let vm = QueryPanelViewModel(fun _ -> Ok "")
        let mutable canExecute = false
        vm.ExecuteQueryCommand.CanExecute.Subscribe(fun v -> canExecute <- v) |> ignore
        vm.QueryInput <- "get workspaces"
        vm.QueryInput <- ""
        canExecute |> should equal false
```

### The Dispatcher problem — and the fix

The `RunQuery` body routes state back via `Avalonia.Threading.Dispatcher.UIThread.Post`.
This is the one line that couples the ViewModel to Avalonia. Replacing it with an injected
scheduler removes the dependency:

```fsharp
// In QueryPanelViewModel, inject a scheduler instead of hard-coding the Dispatcher:
type QueryPanelViewModel(queryFunc: string -> Result<string, string>,
                         ?scheduler: IScheduler) as this =
    let scheduler = defaultArg scheduler RxApp.MainThreadScheduler
    // ...
    // then in RunQuery:
    scheduler.Schedule(fun () -> this.State <- ...) |> ignore
```

With that change, the full end-to-end test becomes:

```fsharp
    [<Test>]
    member _.``RunQuery sets ResponseData state on success``() =
        let responseJson = """{"success":true,"data":{"workspaces":[]}}"""
        // CurrentThreadScheduler runs actions synchronously on the calling thread
        let vm = QueryPanelViewModel((fun _ -> Ok responseJson), Scheduler.CurrentThread)

        vm.QueryInput <- "get workspaces"
        vm.ExecuteQueryCommand.Execute().Wait()   // blocks until async completes

        vm.IsData  |> should equal true
        vm.IsError |> should equal false

    [<Test>]
    member _.``RunQuery sets ErrorMsg state on query function failure``() =
        let vm = QueryPanelViewModel((fun _ -> Error "connection refused"), Scheduler.CurrentThread)

        vm.QueryInput <- "get workspaces"
        vm.ExecuteQueryCommand.Execute().Wait()

        vm.IsError      |> should equal true
        vm.ErrorMessage |> should equal "connection refused"
```

---

## FuncUI View — not unit testable

There is no object to instantiate. The entire state machine lives inside Avalonia's
`IComponentContext`, which only exists during a rendering pass:

```fsharp
// This is what you'd need to "test":
let view (f: string -> Result<string, string>) =
    Component(fun ctx ->          // <-- ctx only exists when Avalonia creates this component
        let state = ctx.useState State.Empty   // IWritable<State> — Avalonia-managed
        let query = ctx.useState ""

        ctx.useEffect(...)        // side effects tied to component lifecycle
        // ...
    )

// You cannot call `view` and then inspect `state` — it's inaccessible after the call.
// view returns an IView (an Avalonia rendering descriptor), not a testable value.
```

The only way to observe state changes in the FuncUI version is to render the component in an
Avalonia headless host, interact with the `TextBox`/`Button` controls, and assert on the
rendered `TextBlock` text — which is exactly the headless testing you want to avoid.

### The extraction workaround

The FuncUI view *could* be made partially testable by extracting the query logic into a pure
function:

```fsharp
// Extract the pure logic:
let runQuery (f: string -> Result<string, string>) (queryText: string) : State =
    match f queryText >>= CustomParsers.tryExtractResponseData with
    | Ok(GlazeWMRawResponse.Data r)        -> ResponseData r
    | Ok(GlazeWMRawResponse.ErrorMsg e)    -> ErrorMsg e
    | Error e                              -> ErrorMsg e

// Then test that function directly — but this is just recreating the ViewModel pattern.
```

This is effectively reimplementing MVVM manually. The component itself would still require
headless testing for any UI interaction.

---

## Summary

| | ReactiveUI ViewModel | FuncUI View |
|---|---|---|
| Instantiate without Avalonia | Yes | No |
| Test initial state | Yes | No |
| Test property change logic | Yes | No |
| Test command enable/disable | Yes | No |
| Test query result → state | Yes (after scheduler injection) | No |
| Current Avalonia leak | `Dispatcher.UIThread.Post` | Everything |

> [!question]-
> Great, one part of the answer that I don't fully understand is the issue of the `IScheduler`. Can you write the entire `RunQuery` updated function when I have a `scheduler` variable?

The current code uses Avalonia.Threading.Dispatcher.UIThread.Post to jump back onto the UI thread after the async work finishes. The scheduler replaces that marshalling concern — in production it's RxApp.MainThreadScheduler (which uses the Avalonia dispatcher under the hood), and in tests it's Scheduler.CurrentThread (which runs synchronously, inline). Here's the updated function:

```fsharp
member private this.RunQuery() =
    let queryText = this.QueryInput.Trim()

    if not (String.IsNullOrWhiteSpace queryText) then
        Log.Information("Query: {Query}", queryText)
        this.State <- Querying

        async {
            let result =
                queryFunc queryText >>= CustomParsers.tryExtractResponseData

            scheduler.Schedule(fun () ->
                this.State <-
                    match result with
                    | Ok(Data r)                        -> ResponseData r
                    | Ok(GlazeWMRawResponse.ErrorMsg e) -> ErrorMsg e
                    | Error e                           -> ErrorMsg e)
            |> ignore
        }
        |> Async.Start
```

The only change is the last part — Avalonia.Threading.Dispatcher.UIThread.Post(fun () -> ...) becomes scheduler.Schedule(fun () -> ...) |> ignore. The |> ignore is needed because Schedule returns an IDisposable (a cancellation handle).

scheduler.Schedule is an extension method from System.Reactive.Concurrency, so you'll need open System.Reactive.Concurrency at the top of the file if it's not already there.

> [!question]-
> and this will surely work? in the past, when I had GUI changes outside of Avalonia's UIThread, it failed. Does the RxApp.MainThreadScheduler equal to Avalonia's UIthread?

Good instinct to question this. The answer is: yes, but only because of how Avalonia+ReactiveUI wire themselves up together.

When you call `UseReactiveUI()` in the Avalonia app builder, it registers `AvaloniaScheduler.Instance` as `RxApp.MainThreadScheduler`. AvaloniaScheduler internally calls `Dispatcher.UIThread.Post` — so it's the same mechanism, just one level of indirection.

You can verify this in the ReactiveUI source: AvaloniaScheduler is literally a thin wrapper around Dispatcher.UIThread.

So the chain is:

```fsharp
scheduler.Schedule(...)          // your injected scheduler
  → AvaloniaScheduler            // registered by UseReactiveUI() at startup
    → Dispatcher.UIThread.Post   // the actual Avalonia UI thread dispatch
```

If `UseReactiveUI()` is missing from your app builder, `RxApp.MainThreadScheduler` falls back to a thread-pool scheduler and your UI updates will crash exactly as before. So the safety net is not the scheduler pattern itself — it's that the Avalonia+ReactiveUI integration sets it up correctly.

The testability gain is separate from that: in tests you bypass the whole chain by injecting Scheduler.CurrentThread, which runs the action inline on the calling thread, no Avalonia involved at all.