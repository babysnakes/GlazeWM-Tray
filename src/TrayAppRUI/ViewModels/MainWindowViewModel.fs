namespace FSharpReactiveUI.ViewModels

open System.Reflection

type MainWindowViewModel(queryFunc: string -> Result<string, string>) =
    inherit ViewModelBase()

    let version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        |> Option.ofObj
        |> Option.map (fun a -> a.InformationalVersion.Split('+')[0])
        |> Option.defaultValue "0.0-error"

    member val QueryPanel = QueryPanelViewModel(queryFunc)
    member _.VersionDisplay = $"Version {version}"
