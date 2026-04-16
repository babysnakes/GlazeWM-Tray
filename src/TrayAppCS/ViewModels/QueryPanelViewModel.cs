using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.FSharp.Core;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace GlazeWM.TrayAppCS.ViewModels;

// ── Query state ──────────────────────────────────────────────────────────────
// Sealed record hierarchy: the view binds a single State object and routes to
// the appropriate DataTemplate. No combination of IsEmpty/IsError/IsData flags
// can represent a contradictory state.

public abstract record QueryState;
public sealed record QueryStateEmpty : QueryState;
public sealed record QueryStateQuerying : QueryState;
public sealed record QueryStateError(string Message) : QueryState;
public sealed record QueryStateData(JsonTreeNode Root) : QueryState;

// ── JSON tree node ────────────────────────────────────────────────────────────
// Pure immutable tree built from a JsonElement. No ReactiveObject machinery
// needed — it is constructed once and never mutated.

public sealed class JsonTreeNode
{
    public string Header { get; }
    public IReadOnlyList<JsonTreeNode> Children { get; }

    public JsonTreeNode(string label, JsonElement element)
    {
        Header = BuildHeader(label, element);
        Children = BuildChildren(element);
    }

    private static string BuildHeader(string label, JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String  => $"{label}: {element.GetString()}",
            JsonValueKind.Number  => $"{label}: {element.GetRawText()}",
            JsonValueKind.True    => $"{label}: true",
            JsonValueKind.False   => $"{label}: false",
            JsonValueKind.Null    => $"{label}: null",
            _                     => label   // Object or Array — label only
        };

    private static IReadOnlyList<JsonTreeNode> BuildChildren(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .Select(p => new JsonTreeNode(p.Name, p.Value))
                .ToList(),
            JsonValueKind.Array => element.EnumerateArray()
                .Select((v, i) => new JsonTreeNode(i.ToString(), v))
                .ToList(),
            _ => []
        };
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public partial class QueryPanelViewModel : ReactiveObject
{
    private readonly Func<string, FSharpResult<string, string>> _queryFunc;

    // [Reactive] generates the backing field + RaiseAndSetIfChanged accessor.
    [Reactive]
    public partial string QueryInput { get; set; }

    // [ObservableAsProperty] generates the ObservableAsPropertyHelper field + getter.
    // The constructor wires up the observable via _stateHelper = ...ToProperty(this, x => x.State).
    [ObservableAsProperty]
    public partial QueryState State { get; }

    public ReactiveCommand<System.Reactive.Unit, QueryState> ExecuteQueryCommand { get; }

    // CS8618: _state is a generated backing field updated lazily by the OAPH getter —
    // it is always valid after _stateHelper is assigned. False positive from source generator.
#pragma warning disable CS8618
    public QueryPanelViewModel(Func<string, FSharpResult<string, string>> queryFunc)
#pragma warning restore CS8618
    {
        _queryFunc = queryFunc;
        QueryInput = "";

        var canExecute = this.WhenAnyValue(
            x => x.QueryInput,
            input => !string.IsNullOrWhiteSpace(input));

        ExecuteQueryCommand = ReactiveCommand.CreateFromTask(RunQueryAsync, canExecute);

        // Build the State stream by merging three sources:
        //   1. Command starts executing  → Querying
        //   2. Command completes         → Data or Error (returned by RunQueryAsync)
        //   3. Command throws            → Error (unexpected exception)
        _stateHelper = Observable.Merge(
                ExecuteQueryCommand.IsExecuting
                    .Where(executing => executing)
                    .Select(_ => (QueryState)new QueryStateQuerying()),
                ExecuteQueryCommand
                    .Select(result => result),
                ExecuteQueryCommand.ThrownExceptions
                    .Select(ex => (QueryState)new QueryStateError(ex.Message)))
            .StartWith(new QueryStateEmpty())
            .ToProperty(this, x => x.State);
    }

    // Runs on a thread-pool thread (ReactiveCommand default scheduler).
    // WebSocketClient.Query is a synchronous blocking call — that is intentional
    // and acceptable on a background thread.
    private Task<QueryState> RunQueryAsync()
    {
        var result = _queryFunc(QueryInput);

        if (!result.IsOk)
            return Task.FromResult<QueryState>(new QueryStateError(result.ErrorValue));

        try
        {
            using var doc = JsonDocument.Parse(result.ResultValue);
            var root = doc.RootElement;

            if (!root.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
            {
                var errMsg = root.TryGetProperty("error", out var errProp)
                    ? errProp.GetString() ?? "Unknown error"
                    : "Unknown error";
                return Task.FromResult<QueryState>(new QueryStateError(errMsg));
            }

            if (!root.TryGetProperty("data", out var dataProp))
                return Task.FromResult<QueryState>(new QueryStateError("No 'data' field in response"));

            // Clone the element so it outlives the JsonDocument.
            var cloned = dataProp.Clone();
            return Task.FromResult<QueryState>(new QueryStateData(new JsonTreeNode("data", cloned)));
        }
        catch (Exception ex)
        {
            return Task.FromResult<QueryState>(new QueryStateError($"JSON parse error: {ex.Message}"));
        }
    }
}
