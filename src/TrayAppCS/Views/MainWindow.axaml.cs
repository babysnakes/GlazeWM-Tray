using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;

namespace GlazeWM.TrayAppCS.Views;

public partial class MainWindow : Window
{
    private readonly WindowNotificationManager _notificationManager;
    private readonly System.Collections.Generic.Queue<Notification> _pending = new();
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        _notificationManager = new WindowNotificationManager(this) { MaxItems = 3 };
    }

    public void Notify(string title, string body, NotificationType type = NotificationType.Information)
    {
        var notification = new Notification(title, body, type, TimeSpan.Zero);

        if (_initialized)
            _notificationManager.Show(notification);
        else
            _pending.Enqueue(notification);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Hide();
        e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (!_initialized)
        {
            _initialized = true;
            while (_pending.TryDequeue(out var n))
                _notificationManager.Show(n);
        }
    }
}
