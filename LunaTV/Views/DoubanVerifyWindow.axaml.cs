using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace LunaTV.Views;

public partial class DoubanVerifyWindow : Window
{
    private readonly SemaphoreSlim _apiFetchLock = new(1, 1);
    private TaskCompletionSource<string>? _apiFetchCompletion;

    public DoubanVerifyWindow()
    {
        InitializeComponent();
        DoubanWebView.NewWindowRequested += DoubanWebView_OnNewWindowRequested;
        DoubanWebView.NavigationCompleted += async (_, _) => await CompleteApiFetchAsync();
    }

    public DoubanVerifyWindow(Uri source) : this()
    {
        DoubanWebView.Source = source;
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

    private void ReloadButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _apiFetchCompletion?.TrySetCanceled();
        _apiFetchCompletion = null;
        DoubanWebView.Source = new Uri("about:blank");
        DoubanWebView.Source = new Uri("https://movie.douban.com/");
    }
}
