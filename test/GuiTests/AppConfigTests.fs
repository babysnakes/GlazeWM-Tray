namespace GuiTests

open Farse
open FsUnit
open GlazeWM.TrayApp.Models
open LibTests.CommonHelpers
open NUnit.Framework

module AppConfigTests =

    [<Test>]
    let ``app config serialize and deserialize correctly`` () =
        let appConfig: AppConfig =
            { LogsDirectory = "E:\\logs"
              Port = 12345
              Debug = true
              StartWithWindow = false }
        let jsonString = appConfig |> AppConfig.asJsonString
        let parsed = Parser.parse jsonString AppConfig.appConfigParser |> Result.unwrap
        parsed |> should equal appConfig
