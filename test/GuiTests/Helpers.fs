module GuiTests.Helpers

open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.VisualTree
open NUnit.Framework

[<RequireQualifiedAccess>]
module Seq =
    let tap f (ss: 'S seq) =
        ss
        |> Seq.map (fun s ->
            f s
            s)

/// Get all visual descendants of type 'T
let allType<'T when 'T :> Visual> (window: Window) =
    window.GetVisualDescendants()
    |> Seq.choose (function
        | :? 'T as b -> Some b
        | _ -> None)

/// Identifies a button by its tooltip. The provided tooltip is compared with `Contains`.
/// Fails if the result is not unique.
let buttonByTooltip (tooltip: string) (window: Window) =
    allType<Button> window
    |> Seq.filter (fun b ->
        match ToolTip.GetTip(b) with
        | :? string as tip -> tip.Contains tooltip
        | _ -> false)
    |> Seq.exactlyOne


/// nested background tasks caused by `useEffect` timings are tricky to test. This forces execution, once per
/// nested level. The sleep gives thread-pool work (e.g., JSON parsing after runQuery returns) time to complete
/// before the next RunJobs() drains the dispatcher queue.
let waitForResult (p: _ -> bool) retries =
    let rec loop n =
        Avalonia.Threading.Dispatcher.UIThread.RunJobs()
        let found = p ()
        if found then
            found
        elif n <= 0 then
            false
        else
            System.Threading.Thread.Sleep(10)
            loop (n - 1)

    loop retries

let loadFixture fileName =
    let fixturePath = Path.Combine("Fixtures", fileName)
    File.ReadAllText fixturePath

let printElements (window: Window) =
    window.GetVisualDescendants()
    |> Seq.map (fun v -> v.GetType().Name)
    |> Seq.iter TestContext.Progress.WriteLine

let expectTrue (message: string) (tested: bool) = Assert.That(tested, message)
