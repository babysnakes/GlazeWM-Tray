namespace GlazeWM.TrayApp.Helpers

open System
open System.Runtime.InteropServices
open GlazeWM.Tray.Literals
open Microsoft.Toolkit.Uwp.Notifications
open Serilog

[<RequireQualifiedAccess>]
module Option =
    let tryDo (f: 'T -> unit) =
        function
        | Some x -> f x
        | None -> Log.Warning("tryDo on None")

module Notifications =
    /// Send Windows toast basic notification
    let sendNotification title body =
        ToastContentBuilder().AddText(title).AddText(body).Show()

    let sendBugNotification bug =
        ToastContentBuilder()
            .AddText("You encountered a bug!")
            .AddText(
                $"Please report the bug and specify the reason ({bug}). You can also attach log.txt from the logs directory."
            )
            .AddButton(ToastButton().SetContent("Report the bug").SetProtocolActivation(Uri(BugUrl)))
            .Show()

    /// Define the Win32 MessageBox function
    [<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
    extern int MessageBoxW(nativeint hWnd, string text, string caption, uint32 type')

    /// Send Windows native error message box
    let showErrorMessage (title: string) (message: string) =
        // 0x00040010u is MB_ICONERROR + MB_TOPMOST
        MessageBoxW(nativeint 0, message, title, 0x00040010u) |> ignore
