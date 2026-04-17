using System.Reactive.Linq;
using System.Text.Json;
using GlazeWM.Tray.MessageParser;
using Microsoft.FSharp.Core;
using ReactiveUI;

namespace GlazeWM.TrayAppCS.ViewModels;

// Possible states to be used in the query panel
public abstract record QueryState;
public sealed record QueryStateEmpty : QueryState;
public sealed record QueryStateQuerying : QueryState;
public sealed record QueryStateError(string Message) : QueryState;
public sealed record QueryStateData(JsonTreeNode Root) : QueryState;

// JSON construct to use with TreeView
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

public class QueryPanelViewModel : ReactiveObject
{
    private readonly Func<string, FSharpResult<string, string>> _queryFunc;

    public string QueryInput
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    private readonly ObservableAsPropertyHelper<QueryState> _stateHelper;
    public QueryState State => _stateHelper.Value;

    public ReactiveCommand<System.Reactive.Unit, QueryState> ExecuteQueryCommand { get; }

    public QueryPanelViewModel(Func<string, FSharpResult<string, string>> queryFunc)
    {
        _queryFunc = queryFunc;

        var canExecute = this.WhenAnyValue(
            x => x.QueryInput,
            input => !string.IsNullOrWhiteSpace(input));

        ExecuteQueryCommand = ReactiveCommand.CreateFromTask(RunQueryAsync, canExecute);

        // Update the query state.
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

    private async Task<QueryState> RunQueryAsync()
    {
        var result = await Task.Run(() => _queryFunc(QueryInput));

        return result.IsOk
            ? ParseJsonResponse(result.ResultValue)
            : new QueryStateError(result.ErrorValue);
    }

    private static QueryState ParseJsonResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
            {
                var errMsg = root.TryGetProperty("error", out var errProp)
                    ? errProp.GetString() ?? "Unknown error"
                    : "Unknown error";
                return new QueryStateError(errMsg);
            }

            if (!root.TryGetProperty("data", out var dataProp))
                return new QueryStateError("No 'data' field in response");

            // Clone the element so it outlives the JsonDocument.
            return new QueryStateData(new JsonTreeNode("data", dataProp.Clone()));
        }
        catch (Exception ex)
        {
            return new QueryStateError($"JSON parse error: {ex.Message}");
        }
    }
}
