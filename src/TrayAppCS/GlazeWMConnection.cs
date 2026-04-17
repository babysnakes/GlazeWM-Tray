using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using GlazeWM.Tray.MessageParser;
using GlazeWM.Tray.Models;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Serilog;
using static GlazeWM.Tray.Literals;

namespace GlazeWM.TrayAppCS;

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

// ReSharper disable once InconsistentNaming
public sealed class GlazeWMConnection : IDisposable
{
    private readonly Uri _uri;
    private readonly Subject<TrayState> _stateSubject = new();
    private readonly Subject<string> _unsuccessfulSubject = new();
    private readonly Subject<string> _communicationErrorSubject = new();
    private readonly Subject<string> _bugNotificationSubject = new();
    private readonly BehaviorSubject<bool> _disconnectedSubject = new(false);

    private CompositeDisposable _disposables = new();
    private Tray.WebSocketClient.WebSocketClient? _wsClient;

    // Stable observables — valid for the lifetime of this object, survive reconnections.
    public IObservable<TrayState> State => _stateSubject.AsObservable();
    public IObservable<string> UnsuccessfulResponses => _unsuccessfulSubject.AsObservable();
    public IObservable<string> CommunicationErrors => _communicationErrorSubject.AsObservable();
    // NoCurrentWorkspace and UnsetWsClient — forwarded to App for bug notifications.
    public IObservable<string> BugNotifications => _bugNotificationSubject.AsObservable();
    public IObservable<bool> IsDisconnected => _disconnectedSubject.AsObservable();

    public GlazeWMConnection(Uri uri)
    {
        _uri = uri;
        Connect();
    }

    public void Reconnect()
    {
        _disconnectedSubject.OnNext(false);
        Connect();
    }

    public void Refresh()
    {
        if (_wsClient is null) return;
        _wsClient.SendMessage(QWorkspaces);
        _wsClient.SendMessage(QBinding);
        _wsClient.SendMessage(QPaused);
    }

    public void FocusWorkspace(string name) =>
        _wsClient?.SendMessage($"{CFocusWorkspacePrefix} {name}");

    public FSharpResult<string, string> Query(string query) =>
        _wsClient?.Query(query, FSharpOption<int>.None)
        ?? FSharpResult<string, string>.NewError("Not connected");

    private void Connect()
    {
        _disposables.Dispose();
        _disposables = new CompositeDisposable();

        var parser = new ParserCS();
        var client = new Tray.WebSocketClient.WebSocketClient(_uri, parser.Dispatcher());
        parser.SetWsClient(client.Agent);
        _wsClient = client;

        _disposables.Add(parser.Notifications
            .OfType<AppNotification.UnSuccessfulResponse>()
            .Subscribe(resp => _unsuccessfulSubject.OnNext(resp.Item)));

        _disposables.Add(parser.Notifications
            .Scan(TrayState.Empty, ApplyNotification)
            .DistinctUntilChanged()
            .Subscribe(state => _stateSubject.OnNext(state)));

        _disposables.Add(parser.Errors
            .Subscribe(HandleParserError));

        // [<CLIEvent>] F# events are standard .NET events in C# — use +=
        client.Error += (_, msg) => HandleCommunicationError(msg);

        client.SendMessage(
            $"sub -e {SWorkspaceUP} {SWorkspaceACT} {SWorkspaceDeACT} {SBindingModesCH} {SPauseCH} {SFocusCH}");
        Refresh();
    }

    private void HandleCommunicationError(string msg)
    {
        Log.Error("A websocket client exception had occurred: {Err}", msg);

        _disposables.Dispose();
        _disposables = new CompositeDisposable();

        // WebSocketClient uses explicit F# IDisposable implementation — cast to dispose.
        if (_wsClient is IDisposable d) d.Dispose();
        _wsClient = null;

        _disconnectedSubject.OnNext(true);
        _communicationErrorSubject.OnNext(msg);
    }

    private void HandleParserError(MessageParserEvent evt)
    {
        if (evt is MessageParserEvent.ParseError parseError)
            Log.Error("A parser exception had occurred: {Err}", parseError.Item);
        else if (evt is MessageParserEvent.AgentError agentError)
        {
            Log.Error(agentError.Item, "MessageParser agent error:");
            HandleCommunicationError($"MessageParser: {agentError.Item.Message}");
        }
        else if (evt.IsNoCurrentWorkspace || evt.IsUnsetWsClient)
            _bugNotificationSubject.OnNext(nameof(evt));
        else
            Log.Error("Unknown parser event: {Evt}", evt);
    }

    private static TrayState ApplyNotification(TrayState state, AppNotification notification) =>
        notification switch
        {
            AppNotification.Workspaces ws       => state with { Workspaces = ws.Item },
            AppNotification.Paused p            => state with { Paused = p.Item },
            AppNotification.NewBindingModes nb  => state with { CustomBinding = nb.Item },
            _                                   => state
        };

    public void Dispose()
    {
        _disposables.Dispose();
        if (_wsClient is IDisposable d) d.Dispose();
        _stateSubject.Dispose();
        _unsuccessfulSubject.Dispose();
        _communicationErrorSubject.Dispose();
        _bugNotificationSubject.Dispose();
        _disconnectedSubject.Dispose();
    }
}