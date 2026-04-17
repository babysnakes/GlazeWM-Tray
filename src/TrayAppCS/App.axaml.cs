using System.Diagnostics;
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
using ReactiveUI;
using Serilog;
using Serilog.Core;

namespace GlazeWM.TrayAppCS;

public class App : Application
{
    private readonly Uri _glazeUri = new("ws://localhost:6123/");
    private readonly LoggingLevelSwitch _levelSwitch;
    private readonly string _logDir;
    private readonly TrayIcon _tray = new();
    private readonly NativeMenuItem _reInitMenuItem = new() { Header = "Reinitialize GlazeWM Connection" };
    private readonly Dictionary<string, WindowIcon> _statusIcons = LoadIcons();

    private NativeMenuItem[] _persistentMenuItems = [];
    private GlazeWMConnection? _connection;
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

        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _tray.ToolTipText = "Workspace ?";
        _tray.Menu = new NativeMenu();

        BuildPersistentMenuItems(lifetime);
        UpdateTrayMenu([]);

        var vm = new MainWindowViewModel(query => _connection?.Query(query)
            ?? Microsoft.FSharp.Core.FSharpResult<string, string>.NewError("Not connected"));
        _mainWindow = new MainWindow { DataContext = vm };

        _tray.Clicked += (_, _) => ToggleMainWindow();
        _tray.Icon = _statusIcons["icon"];

        var icons = new TrayIcons();
        icons.Add(_tray);
        TrayIcon.SetIcons(this, icons);

        _connection = new GlazeWMConnection(_glazeUri);

        _connection.State
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);

        _connection.UnsuccessfulResponses
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(msg => _mainWindow?.Notify(
                "Unsuccessful Response from GlazeWM", msg, NotificationType.Error));

        _connection.CommunicationErrors
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(HandleCommunicationError);

        _connection.ParserErrors
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(HandleParserError);

        _connection.IsDisconnected
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(disconnected => _reInitMenuItem.IsEnabled = disconnected);

        ActualThemeVariantChanged += (_, _) =>
        {
            Log.Debug("Theme variant changed, new variant is {Variant}", ActualThemeVariant);
            _connection.Refresh();
        };

        lifetime.Exit += (_, _) => Cleanup();
        _tray.IsVisible = true;

        Log.Information("Application started");
        base.OnFrameworkInitializationCompleted();
    }

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
            item.Click += (_, _) => _connection?.FocusWorkspace(captured.Name);
            _tray.Menu.Items.Add(item);
        }

        foreach (var item in _persistentMenuItems)
            _tray.Menu.Items.Add(item);
    }

    private void HandleCommunicationError(string msg)
    {
        var text = $"A fatal error occurred regarding communication with GlazeWM\n\n{msg}.\n\n" +
                   "Check the logs for more details. To renew communication with GlazeWM after " +
                   "you make sure it runs correctly, please select 'Reinitialize GlazeWM Connection' " +
                   "from the tray menu.";

        Notifications.ShowErrorMessage("GlazeWM Communication Error", text);
        UpdateTrayMenu([]);
        _tray.Icon = _statusIcons["error"];
    }

    private void HandleParserError(MessageParserEvent evt)
    {
        if (evt.IsNoCurrentWorkspace)
            Notifications.SendBugNotification("NoCurrentWorkspace");
        else if (evt.IsUnsetWsClient)
            Notifications.SendBugNotification("UnsetWsClient");
    }

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
            _connection?.Reconnect();
        };

        var refreshItem = new NativeMenuItem { Header = "Refresh" };
        refreshItem.Click += (_, _) => _connection?.Refresh();

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
        _connection?.Dispose();
        _tray.IsVisible = false;
    }
}