# CLAUDE.md
n
This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

GlazeWM-Tray is a Windows system tray application for GlazeWM (a tiling window manager). It shows the active workspace as a tray icon, indicates paused/binding mode states, and provides a context menu for workspace navigation. Built in F# on .NET 10 with Avalonia UI.

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
- `just package [rid]` — publish and zip a single RID (default: `win-x64`)
- `just dist` — package all supported architectures (`win-x64`, `win-arm64`)

Direct .NET: `dotnet build`, `dotnet test`, `dotnet tool restore`

Run a single test: `dotnet test --filter "FullyQualifiedName~TestName"`

## Architecture

**Lib** (`src/Lib/`) — core library shared between TrayApp and Cli:
- `Models.fs` — data types (Workspace, Window, WorkspaceName) and JSON parsers using Farse
- `WebSocketClient.fs` — MailboxProcessor-based async WebSocket agent
- `MessageParser.fs` — routes incoming WS messages, manages app state
- `Literals.fs` — GlazeWM command/event string constants

**TrayApp** (`src/TrayApp/`) — main tray application:
- `App.fs` — tray icon state machine (MailboxProcessor agent), menu construction, WS connection init, theme-aware icon selection
- `Helpers.fs` — Windows toast notifications (UWP), Win32 MessageBoxW interop
- `Program.fs` — entry point, Serilog setup (console in debug, file in release at `%APPDATA%\GlazeWM-Tray\logs\`)

**Cli** (`src/Cli/`) — console tool for testing/debugging GlazeWM connection

**Tests** (`test/LibTests/`) — NUnit + FsUnit tests for Lib, with JSON fixtures in `Fixtures/`

**Data flow:** TrayApp connects via WebSocket to GlazeWM (`ws://localhost:6123/`), subscribes to workspace/focus/pause/binding events, MessageParser updates state, App agent renders tray icon and menu.

**Icon system:** Per-workspace icons (0-9, a-z first char) in three variants: black (b), white (w), grey (g). Grey = paused or custom binding mode. Question mark for unknown states. Source: `resources/Icons.af` (Affinity Designer); regenerate with `just icons`.

## Collaboration Style

- **Prefer suggestions over edits** — unless explicitly asked, don't edit code directly. Instead, offer idiomatic F# solutions with samples that resemble the actual code. Where feasible, provide a self-contained `.fsx` script the user can run independently to explore the approach outside the project.
- **Avalonia class/function balance** — Avalonia is a C# library and sometimes requires classes (e.g. the `App` class in `App.fs`). Prefer `let` functions for logic, but some behaviour belongs in class methods by design — don't force everything into `let` bindings when the class method is the natural fit.

## Cross-Platform Notes

- The project is being extended for macOS.
- Only development lock files are committed; publish-time lock file changes are
  intentionally ignored for now.
- With multi-targeting, RID-specific native packages can introduce
  platform-divergent lock file entries. The goal is that `dotnet restore` on
  both Windows and macOS produces the same development lock file; if that breaks,
  regenerate with `dotnet restore --force-evaluate` and commit the result.

## Key Patterns

- **MailboxProcessor agents** for concurrency (WebSocket client, tray icon state)
- **Railway-oriented programming** with Result types via FsToolkit.ErrorHandling
- F# file ordering matters — files compile top-to-bottom as listed in .fsproj
- Central Package Management via `Directory.Packages.props`
- TrayApp targets `net10.0-windows10.0.19041.0`; Lib and Cli target `net10.0`
- **Published trimmed** — avoid libraries that rely on reflection (e.g. System.Text.Json source generators are fine, but reflection-based serializers will break at runtime after trimming)
