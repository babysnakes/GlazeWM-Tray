namespace GuiTests

open Avalonia
open Avalonia.Headless
open Avalonia.Themes.Fluent

type TestApp() =
    inherit Application()
    override this.Initialize() = this.Styles.Add(FluentTheme())
    static member BuildAvaloniaApp() =
        AppBuilder.Configure<TestApp>().UseHeadless(AvaloniaHeadlessPlatformOptions())

[<assembly: AvaloniaTestApplication(typeof<TestApp>)>]
do ()
