using Avalonia.Input.Platform;
using System;
using Ursa.Controls;

namespace LunaTV.Views;

public partial class CrashWindow : UrsaWindow
{
    public CrashWindow(string exception)
    {
        InitializeComponent();

        Info.Text = exception;
        Copy.Click += async (_, _) =>
        {
            IClipboard? clipboard = GetTopLevel(this)?.Clipboard;
            await clipboard!.SetTextAsync(exception);
        };
        Continue.Click += (_, _) =>
        {
            Close();
        };
        Exit.Click += (_, _) =>
        {
            Environment.Exit(0);
        };
        Topmost = true;
        Show();
        Activate();
    }

    public CrashWindow()
    {
    }

    public sealed override void Show()
    {
        base.Show();
    }
}