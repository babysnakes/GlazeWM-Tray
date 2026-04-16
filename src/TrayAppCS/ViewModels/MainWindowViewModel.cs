using System;
using System.Reflection;
using Microsoft.FSharp.Core;
using ReactiveUI;

namespace GlazeWM.TrayAppCS.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public QueryPanelViewModel QueryPanel { get; }

    public string VersionDisplay { get; }

    public MainWindowViewModel(Func<string, FSharpResult<string, string>> queryFunc)
    {
        QueryPanel = new QueryPanelViewModel(queryFunc);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0-error";

        VersionDisplay = $"Version {version}";
    }
}
