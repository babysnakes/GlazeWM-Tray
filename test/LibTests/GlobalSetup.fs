namespace LibTests

open NUnit.Framework
open Serilog

[<SetUpFixture>]
type GlobalSetup() =
    [<OneTimeSetUp>]
    member _.Setup() =
        Log.Logger <- LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger()

    [<OneTimeTearDown>]
    member _.TearDown() = Log.CloseAndFlush()
