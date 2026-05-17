# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

GlazeWM-Tray is a cross-platform (Windows and macOS) system tray application for GlazeWM (a tiling window manager). It shows the active workspace as a tray icon, indicates paused/binding mode states, and provides a context menu for workspace navigation. A GUI window lets users query GlazeWM and inspect the JSON response in an interactive tree view. Built in F# on .NET 10 with Avalonia UI.

## Commands

Uses **Just** as task runner:

- `just cli` — run console app against running GlazeWM/ex?/
- `just cli-mock` — run console app against mock websocket (port 8181)
- `just format` — format code with Fantomas
- `just check-format` — check formatting without changes
- `just lint` — lint with FSharpLint
- `just check` — run both format check and lint
- `just vtest` — run tests with verbose output (`NUnit.ConsoleOut=1`)
- `just ci` — full CI pipeline (restore, check-format, build, test)
- `just restore` — restore dependencies honoring lock file
- `just package [rid]` — publish and package a single RID; default is `win-x64` on Windows, `osx-arm64` on macOS
- `just dist` — package all supported architectures for the current platform (`win-x64`, `win-arm64` on Windows; `osx-arm64`, `osx-x64` on macOS)

Direct .NET: `dotnet build`, `dotnet test`, `dotnet tool restore`

Run a single test: `dotnet test --filter "FullyQualifiedName~TestName"`

## Architecture

**Lib** (`src/Lib/`) — core library shared between TrayApp and Cli:
- `Extensions.fs` — small utilities (e.g. `Result.tryCatch`)
- `Models.fs` — data types (Workspace, Window, WorkspaceName) and JSON parsers using Farse
- `Literals.fs` — GlazeWM command/event string constants
- `WebSocketClient.fs` — WebSocket client; uses MailboxProcessor internally for the send loop and sync query, but exposes received messages and failures as `IObservable<T>` via `Subject<T>`
- `MessageParser.fs` — `Parser` class: subscribes to raw WS messages and emits parsed `ParsedMessage` and `ParserWarnings` observables using Rx operators (`Observable.choose`, `Observable.scanInit`)
- `GlazeWMClient.fs` — thin façade composing `WebSocketClient` + `Parser`; the primary entry point for consumers

**TrayApp** (`src/TrayApp/`) — main tray application:
- `Icons.fs` — SVG path icon data and `pathIcon` helper for Avalonia FuncUI
- `Helpers.fs` — Windows toast notifications (UWP), Win32 MessageBoxW interop
- `Models.fs` — `AppConfig` type and its loader
- `AboutPanel.fs` — about panel view: app name and version
- `QueryPanel.fs` — query panel view: text input to send a GlazeWM query, displays the JSON response in an interactive collapsible tree view, and allows copying the formatted JSON to clipboard
- `MainView.fs` — `MainWindow` (tabbed GUI window): hosts the Query and About tabs; supports Ctrl+W to close and hides on close instead of exiting
- `Styles/AppStyles.fs` — `AppStyles` class loading XAML styles
- `TrayItem.fs` — `TrayItem` class: manages the tray icon and native menu; `Handle` is a pure state-transition function (`TrayIconState -> ParsedMessage -> TrayIconState`); impure operations (menu mutation, icon update) are class members
- `App.fs` — Avalonia `App` class; wires the reactive pipeline in `OnFrameworkInitializationCompleted` (`GlazeWMClient` observables → `ObserveOn(uiScheduler)` → `Scan` → `TrayItem.Handle`); handles tray menu events and connection lifecycle
- `Program.fs` — entry point, Serilog setup (console in debug, file in release at `%APPDATA%\GlazeWM-Tray\logs\`)

**Cli** (`src/Cli/`) — console tool for testing/debugging GlazeWM connection

**Tests**:
- `test/LibTests/` — NUnit + FsUnit unit tests for Lib, with JSON fixtures in `Fixtures/`
- `test/GuiTests/` — headless Avalonia GUI tests (using `Avalonia.Headless.NUnit`) for TrayApp views; currently covers `QueryPanel`

**Data flow:** `GlazeWMClient` connects via WebSocket to GlazeWM (`ws://localhost:6123/`), subscribes to workspace/focus/pause/binding events. `Parser` emits typed `ParsedMessage` values as an observable. `App` pipes them through `Scan` (with `TrayItem.Handle` as the pure fold function) on the Avalonia UI scheduler, updating the tray icon and menu reactively.

**Icon system:** Per-workspace icons (0-9, a-z first char) in three variants: black (b), white (w), grey (g). Grey = paused or custom binding mode. Question mark for unknown states. Source: `resources/Icons.af` (Affinity Designer); regenerate with `just icons`.

## Collaboration Style

- **Prefer suggestions over edits** — unless explicitly asked, don't edit code directly. Instead, offer idiomatic F# solutions with samples that resemble the actual code. Where feasible, provide a self-contained `.fsx` script the user can run independently to explore the approach outside the project.
- **Avalonia class/function balance** — Avalonia is a C# library and sometimes requires classes (e.g. the `App` class in `App.fs`). Prefer `let` functions for logic, but some behaviour belongs in class methods by design — don't force everything into `let` bindings when the class method is the natural fit.
- **Purity discipline** — `let` bindings inside classes are kept pure (no side effects, no mutable state). Impure operations (UI mutation, logging, sending messages) belong in class members. This makes pure logic easy to test in isolation.

## Cross-Platform Notes

- The project supports both Windows and macOS.
- Only development lock files are committed; publish-time lock file changes are
  intentionally ignored for now.
- With multi-targeting, RID-specific native packages can introduce
  platform-divergent lock file entries. The goal is that `dotnet restore` on
  both Windows and macOS produces the same development lock file; if that breaks,
  regenerate with `dotnet restore --force-evaluate` and commit the result.

## Key Patterns

- **Reactive architecture** (Rx.NET / `FSharp.Control.Reactive`) for the main data flow: observables carry parsed messages from `GlazeWMClient` through the pipeline to the UI
- **MailboxProcessor** is still used inside `WebSocketClient` for the outbound send loop and synchronous query, but is an implementation detail not exposed to consumers
- **Railway-oriented programming** with Result types via FsToolkit.ErrorHandling
- F# file ordering matters — files compile top-to-bottom as listed in .fsproj
- Central Package Management via `Directory.Packages.props`
- **Published trimmed** — avoid libraries that rely on reflection (e.g. System.Text.Json source generators are fine, but reflection-based serializers will break at runtime after trimming)
