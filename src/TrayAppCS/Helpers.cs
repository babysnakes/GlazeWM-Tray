using System;
using System.Diagnostics;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using static GlazeWM.Tray.Literals;

namespace GlazeWM.TrayAppCS.Helpers;

public static class Notifications
{
    private static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, "Failed to open uri: {Uri}", uri); }
    }

    public static void SendBugNotification(string bug)
    {
        Log.Error("Bug: {Bug}", bug);
        var title = "You encountered a bug!";
        var body = $"Please report the bug and specify the reason ({bug}). " +
                   "You can also attach log.txt from the logs directory.\n\n" +
                   "Do you want to open the bug report page?";

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var box = MessageBoxManager.GetMessageBoxStandard(title, body, ButtonEnum.YesNo, Icon.Error);
                var result = await box.ShowAsync();
                if (result == ButtonResult.Yes) OpenUri(BugUrl);
            }
            catch (Exception ex) { Log.Error(ex, "Failed to show bug notification box"); }
        });
    }

    public static void ShowErrorMessage(string title, string message)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Error);
                await box.ShowAsync();
            }
            catch (Exception ex) { Log.Error(ex, "Failed to show error message box"); }
        });
    }
}
