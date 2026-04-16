# TrayAppCS — Avalonia + ReactiveUI C# Implementation Plan

A living document tracking the plan for creating `src/TrayAppCS`, a C# ReactiveUI Avalonia tray app with the same functionality as the existing `TrayApp` (FuncUI) and `TrayAppRUI` (F# ReactiveUI) projects.

**Guiding principles for the C# project:**
- Minimize mutable state
- Prevent illegal state — prefer sealed record hierarchies (DU equivalents) over classes with interdependent flags
- Idiomatic ReactiveUI: derive UI state from observable streams, not from manual `RaisePropertyChanged` wiring

---

## Step 1: MessageParserCS.fs — IObservable-based parser ✅ DONE

### What was built

Added `src/Lib/MessageParserCS.fs` to the existing F# `Lib` project. It is a near-clone of `MessageParser.fs` but exposes parsed results as `IObservable<T>` streams instead of requiring a `MailboxProcessor<AppNotification>` handler.

**Files changed:**
- `Directory.Packages.props` — added `System.Reactive 6.1.0`
- `src/Lib/Lib.fsproj` — added `System.Reactive` package reference and compile entry
- `src/Lib/MessageParserCS.fs` — new file (created)

### Design decisions

| Aspect | `MessageParser.fs` | `MessageParserCS.fs` |
|---|---|---|
| Constructor arg | `handler: MailboxProcessor<AppNotification>` | none |
| Notify app | `handler.Post(...)` | `notifSubject.OnNext(...)` |
| Error events | `Event<MessageParserEvent>` + `[<CLIEvent>]` | `Subject<MessageParserEvent>` → `IObservable` |
| Public API | `Error: IEvent<MessageParserEvent>` | `Notifications: IObservable<AppNotification>`, `Errors: IObservable<MessageParserEvent>` |
| `Dispatcher()` | returns `MailboxProcessor<string>` for `WebSocketClient` | unchanged |
| `SetWsClient()` | sets feedback WS client | unchanged |
| `IDisposable` | not implemented | disposes both subjects |

### Why IObservable over alternatives

- **vs. `MailboxProcessor` handler**: F# type, awkward from C#; also push-only with no composition
- **vs. `ActionBlock` (TPL Dataflow)**: Less natural with ReactiveUI; consumer still has to bridge to Rx manually
- **vs. .NET `event`**: Needs `Observable.FromEventPattern` conversion in C#; extra indirection

`IObservable<T>` is first-class in ReactiveUI — ViewModels subscribe with `.ObserveOn(RxApp.MainThreadScheduler)`, compose with standard Rx operators, and dispose via `CompositeDisposable`.

### Usage from C#

```csharp
var parser = new ParserCS();
var client = new WebSocketClient(uri, parser.Dispatcher());
parser.SetWsClient(client.Agent);
parser.Notifications
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(HandleAppEvent);
parser.Errors
    .Subscribe(HandleParserError);
```

---

## C# / F# Lib Integration Surface

Before building `src/TrayAppCS`, we reviewed all integration points between the C# project and the F# `Lib`. Conclusion: **no further changes to `Lib` are needed.**

| Integration point | C# friction | Approach |
|---|---|---|
| `ParserCS.Notifications` / `.Errors` (F# DUs over `IObservable`) | Low — C# switch expressions work on F# DU cases | Use as-is |
| `WebSocketClient.SendMessage(string)` | None | Use as-is |
| `WebSocketClient.Query(string)` → `FSharpResult<string, string>` | Low — `.IsOk` / `.ResultValue` / `.ErrorValue` | Unwrap inline in C# |
| `CustomParsers.tryExtractResponseData` | Not used — wraps `FSharp.Data.JsonValue`, which has no C# story | C# project owns its own query response parsing |

### Query panel JSON flow in C#

The C# QueryPanel does **not** call `CustomParsers`. Instead it owns the full pipeline:

1. Call `WebSocketClient.Query(string)` → `FSharpResult<string, string>`
2. Unwrap via `.IsOk` check — surface error string if failed
3. Parse the raw JSON string with `System.Text.Json.JsonDocument`
4. Check `success` field, extract `data` element
5. Bind the `JsonElement` tree to Avalonia TreeView via a C#-native ViewModel

No `FSharp.Data` dependency in the C# project at all.

---

## Step 2: src/TrayAppCS project — TODO

*To be planned.*