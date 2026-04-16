using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using GlazeWM.Tray.MessageParser;
using GlazeWM.Tray.Models;
using GlazeWM.TrayAppCS.Helpers;
using GlazeWM.TrayAppCS.ViewModels;
using GlazeWM.TrayAppCS.Views;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using ReactiveUI;
using Serilog;
using Serilog.Core;
using static GlazeWM.Tray.Literals;

namespace GlazeWM.TrayAppCS;

// Immutable snapshot of all tray icon state — a pure record.
// The entire rendering decision (icon + menu) is a pure function of this value.
public sealed record TrayState(
    WorkspacesNotification Workspaces,
    bool Paused,
    bool CustomBinding)
{
    public static readonly TrayState Empty = new(
        new WorkspacesNotification(
            new WorkspaceName("?", "Unknown Workspace"),
            FSharpList<WorkspaceName>.Empty),
        Paused: false,
        CustomBinding: false);
}

public partial class App : Application
{
    private readonly Uri _glazeUri = new("ws://localhost:6123/");
    private readonly LoggingLevelSwitch _levelSwitch;
    private readonly string _logDir;
    private readonly TrayIcon _tray = new();
    private readonly NativeMenuItem _reInitMenuItem = new() { Header = "Reinitialize GlazeWM Connection" };

    // Connection-scoped disposables — cleared and rebuilt on every InitGlazeConnection call.
    private CompositeDisposable _connectionDisposables = new();

    private Dictionary<string, WindowIcon> _statusIcons = [];
    private NativeMenuItem[] _persistentMenuItems = [];
    private bool _disconnected;
    private ParserCS? _parser;
    private GlazeWM.Tray.WebSocketClient.WebSocketClient? _wsClient;
    private MainWindow? _mainWindow;

    public App(LoggingLevelSwitch levelSwitch, string logDir)
    {
        _levelSwitch = levelSwitch;
        _logDir = logDir;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _statusIcons = LoadIcons();

        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _tray.ToolTipText = "Workspace ?";
        _tray.Menu = new NativeMenu();

        BuildPersistentMenuItems(lifetime);
        UpdateTrayMenu([]);

        var vm = new MainWindowViewModel(RunSyncQuery);
        _mainWindow = new MainWindow { DataContext = vm };

        _tray.Clicked += (_, _) => ToggleMainWindow();
        _tray.Icon = _statusIcons["icon"];

        var icons = new TrayIcons();
        icons.Add(_tray);
        TrayIcon.SetIcons(this, icons);

        InitGlazeConnection();

        ActualThemeVariantChanged += (_, _) =>
        {
            Log.Debug("Theme variant changed, new variant is {Variant}", ActualThemeVariant);
            RefreshState();
        };

        lifetime.Exit += (_, _) => Cleanup();
        _tray.IsVisible = true;

        Log.Information("Application started");
        base.OnFrameworkInitializationCompleted();
    }

    // ── Connection management ───────────────────────────────────────────────

    private void InitGlazeConnection()
    {
        // Dispose all subscriptions from any previous connection before creating new ones.
        _connectionDisposables.Dispose();
        _connectionDisposables = new CompositeDisposable();

        var parser = new ParserCS();
        var client = new GlazeWM.Tray.WebSocketClient.WebSocketClient(_glazeUri, parser.Dispatcher());
        parser.SetWsClient(client.Agent);

        _parser = parser;
        _wsClient = client;
        _disconnected = false;

        // Side-channel: unsuccessful GlazeWM responses shown as in-window notifications.
        var sub1 = parser.Notifications
            .OfType<AppNotification.UnSuccessfulResponse>()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(resp =>
                _mainWindow?.Notify(
                    "Unsuccessful Response from GlazeWM",
                    resp.Item,
                    NotificationType.Error));
        _connectionDisposables.Add(sub1);

        // Main state stream: accumulate workspace/pause/binding notifications into TrayState,
        // then render the tray icon and menu whenever the state changes.
        var sub2 = parser.Notifications
            .Where(n => n is not AppNotification.UnSuccessfulResponse)
            .Scan(TrayState.Empty, ApplyNotification)
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);
        _connectionDisposables.Add(sub2);

        // Parser errors (parse failures, agent crashes, etc.)
        var sub3 = parser.Errors
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(HandleParserError);
        _connectionDisposables.Add(sub3);

        // WebSocket-level errors (disconnect, send failure, etc.)
        // [<CLIEvent>] F# events are standard .NET events in C# — use +=
        client.Error += (_, msg) => HandleCommunicationError(msg);

        // Subscribe to GlazeWM events and fetch initial state.
        client.SendMessage(
            $"sub -e {SWorkspaceUP} {SWorkspaceACT} {SWorkspaceDeACT} {SBindingModesCH} {SPauseCH} {SFocusCH}");
        RefreshState();
    }

    // Pure function: fold a single AppNotification into TrayState.
    private static TrayState ApplyNotification(TrayState state, AppNotification notification) =>
        notification switch
        {
            AppNotification.Workspaces ws       => state with { Workspaces = ws.Item },
            AppNotification.Paused p            => state with { Paused = p.Item },
            AppNotification.NewBindingModes nb  => state with { CustomBinding = nb.Item },
            _                                   => state
        };

    private void RefreshState()
    {
        if (_wsClient is null) return;
        _wsClient.SendMessage(QWorkspaces);
        _wsClient.SendMessage(QBinding);
        _wsClient.SendMessage(QPaused);
    }

    // ── Tray rendering ──────────────────────────────────────────────────────

    private void OnStateChanged(TrayState state)
    {
        _tray.Icon = MatchStateToIcon(state);
        _tray.ToolTipText = $"Workspace {state.Workspaces.Current.DisplayName}";
        UpdateTrayMenu(state.Workspaces.Active);
    }

    private WindowIcon MatchStateToIcon(TrayState state)
    {
        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        var bw = isDark ? "w" : "b";
        var theme = (state.Paused || state.CustomBinding) ? "g" : bw;
        var name = state.CustomBinding ? "qm" : state.Workspaces.Current.Name;
        var key = $"icon-{name}-{theme}";
        return _statusIcons.TryGetValue(key, out var icon) ? icon : _statusIcons["icon-qm-g"];
    }

    private void UpdateTrayMenu(IEnumerable<WorkspaceName> workspaces)
    {
        _tray.Menu!.Items.Clear();

        foreach (var ws in workspaces)
        {
            var dn = ws.Name == ws.DisplayName ? $"Workspace {ws.Name}" : ws.DisplayName;
            var item = new NativeMenuItem { Header = $"{ws.Name} - {dn}" };
            var captured = ws;
            item.Click += (_, _) => _wsClient?.SendMessage($"{CFocusWorkspacePrefix} {captured.Name}");
            _tray.Menu.Items.Add(item);
        }

        _reInitMenuItem.IsEnabled = _disconnected;
        foreach (var item in _persistentMenuItems)
            _tray.Menu.Items.Add(item);
    }

    // ── Error handling ──────────────────────────────────────────────────────

    private void HandleCommunicationError(string msg)
    {
        var text = $"A fatal error occurred regarding communication with GlazeWM\n\n{msg}.\n\n" +
                   "Check the logs for more details. To renew communication with GlazeWM after " +
                   "you make sure it runs correctly, please select 'Reinitialize GlazeWM Connection' " +
                   "from the tray menu.";

        Notifications.ShowErrorMessage("GlazeWM Communication Error", text);
        Log.Error("A websocket client exception had occurred: {Err}", msg);
        _disconnected = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateTrayMenu([]);
            _tray.Icon = _statusIcons["error"];
        });

        _connectionDisposables.Dispose();
        _connectionDisposables = new CompositeDisposable();
        _parser = null;

        // WebSocketClient uses explicit F# IDisposable implementation — cast to dispose.
        if (_wsClient is IDisposable disposableClient)
            disposableClient.Dispose();
        _wsClient = null;
    }

    private void HandleParserError(MessageParserEvent evt)
    {
        // Cases with data use type patterns; cases without data use the generated IsXxx properties.
        if (evt is MessageParserEvent.ParseError parseError)
            Log.Error("A parser exception had occurred: {Err}", parseError.Item);
        else if (evt.IsNoCurrentWorkspace)
            Notifications.SendBugNotification("NoCurrentWorkspace");
        else if (evt.IsUnsetWsClient)
            Notifications.SendBugNotification("UnsetWsClient");
        else if (evt is MessageParserEvent.AgentError agentError)
        {
            Log.Error(agentError.Item, "MessageParser agent error:");
            HandleCommunicationError($"MessageParser: {agentError.Item.Message}");
        }
    }

    // ── Window management ───────────────────────────────────────────────────

    private void ToggleMainWindow()
    {
        if (_mainWindow is null)
        {
            Log.Error("MainWindow is None");
            Notifications.SendBugNotification("Null MainWindow");
            Notifications.ShowErrorMessage("App Error", "MainWindow is null, please restart the application");
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
        else
        {
            _mainWindow.Hide();
        }
    }

    // ── Persistent menu setup ───────────────────────────────────────────────

    private void BuildPersistentMenuItems(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var toggleItem = new NativeMenuItem { Header = "Show/Hide Main Window" };
        toggleItem.Click += (_, _) => ToggleMainWindow();

        var openLogsItem = new NativeMenuItem { Header = "Open Logs Directory" };
        openLogsItem.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(_logDir) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error(ex, "Failed to open logs directory"); }
        };

        var verboseItem = new NativeMenuItem
        {
            Header = "Verbose Logging",
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = false
        };
        verboseItem.Click += (_, _) =>
            _levelSwitch.MinimumLevel = verboseItem.IsChecked
                ? Serilog.Events.LogEventLevel.Debug
                : Serilog.Events.LogEventLevel.Information;

        var quitItem = new NativeMenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => lifetime.Shutdown(0);

        _reInitMenuItem.Click += (_, _) =>
        {
            Log.Information("Reinitializing GlazeWM Connection");
            _mainWindow?.Notify("Reinitializing GlazeWM Connection", "Attempting to reconnect...");
            InitGlazeConnection();
        };

        var refreshItem = new NativeMenuItem { Header = "Refresh" };
        refreshItem.Click += (_, _) => RefreshState();

        _persistentMenuItems =
        [
            new NativeMenuItemSeparator(),
            openLogsItem,
            verboseItem,
            new NativeMenuItemSeparator(),
            _reInitMenuItem,
            refreshItem,
            toggleItem,
            quitItem,
        ];
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private FSharpResult<string, string> RunSyncQuery(string query) =>
        _wsClient is not null
            ? _wsClient.Query(query, FSharpOption<int>.None)
            : FSharpResult<string, string>.NewError("BUG: No websocket client set");

    private static Dictionary<string, WindowIcon> LoadIcons()
    {
        var ids = Enumerable.Range(0, 10).Select(i => i.ToString()).Append("qm");
        var themes = new[] { "w", "b", "g" };

        return ids
            .SelectMany(id => themes.Select(theme => $"icon-{id}-{theme}"))
            .Append("icon")
            .Append("error")
            .ToDictionary(
                name => name,
                name => new WindowIcon(Path.Combine(AppContext.BaseDirectory, "Assets", $"{name}.ico")));
    }

    private void Cleanup()
    {
        Log.Information("Shutting down...");
        _connectionDisposables.Dispose();
        if (_wsClient is IDisposable disposable)
            disposable.Dispose();
        _tray.IsVisible = false;
    }
}
