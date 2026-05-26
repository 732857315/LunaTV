using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LunaTV.Views;

public partial class DoubanVerifyWindow : Window
{
    private static readonly List<DoubanVerifyWindow> Windows = [];
    private readonly SemaphoreSlim _apiFetchLock = new(1, 1);
    private TaskCompletionSource<string>? _apiFetchCompletion;
    private bool _forceClose;

    public DoubanVerifyWindow()
    {
        InitializeComponent();
        Windows.Add(this);
        Closing += (_, e) =>
        {
            if (_forceClose) return;

            e.Cancel = true;
            Hide();
        };
        Closed += (_, _) => Windows.Remove(this);
        DoubanWebView.NewWindowRequested += DoubanWebView_OnNewWindowRequested;
        DoubanWebView.NavigationCompleted += async (_, _) => await CompleteApiFetchAsync();
    }

    public DoubanVerifyWindow(Uri source) : this()
    {
        DoubanWebView.Source = source;
    }

    public static void CloseAll()
    {
        foreach (var window in Windows.ToArray())
        {
            window._forceClose = true;
            window.Close();
        }
    }

    public async Task<string> FetchApiAsync(string url)
    {
        await _apiFetchLock.WaitAsync();
        var completion = new TaskCompletionSource<string>();
        try
        {
            _apiFetchCompletion = completion;
            DoubanWebView.Source = new Uri(url);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (ReferenceEquals(_apiFetchCompletion, completion))
            {
                completion.TrySetCanceled();
                _apiFetchCompletion = null;
            }

            _apiFetchLock.Release();
        }
    }

    private async Task CompleteApiFetchAsync()
    {
        var completion = _apiFetchCompletion;
        if (completion is null) return;

        try
        {
            var text = await DoubanWebView.InvokeScript("document.body.innerText");
            completion.TrySetResult(text);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            if (ReferenceEquals(_apiFetchCompletion, completion)) _apiFetchCompletion = null;
        }
    }

    private void DoubanWebView_OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        new DoubanVerifyWindow(e.Request).Show();
    }

    private void ReloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _apiFetchCompletion?.TrySetCanceled();
        _apiFetchCompletion = null;
        DoubanWebView.Source = new Uri("about:blank");
        DoubanWebView.Source = new Uri("https://movie.douban.com/");
    }

    private void HideButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Hide();
    }
}
