using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Irihi.Avalonia.Shared.Contracts;
using LunaTV.ViewModels.Base;
using System;

namespace LunaTV.ViewModels;

public partial class LoadingWaitViewModel : ViewModelBase, IDialogContext
{
    [ObservableProperty] private string _loadingGifPath = "avares://LunaTV/Assets/images/shalou.gif";
    [ObservableProperty] private string _loadingTime = "00:00:00";
    private int _loadingTimeSeconds;

    private readonly DispatcherTimer _timer;

    public LoadingWaitViewModel()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) =>
        {
            _loadingTimeSeconds++;
            LoadingTime = TimeSpan.FromSeconds(_loadingTimeSeconds).ToString(@"hh\:mm\:ss");
        };
    }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
        TimerStop();
    }

    public event EventHandler<object?>? RequestClose;

    public void TimerStart()
    {
        _timer.Start();
    }

    public void TimerStop()
    {
        _timer.Stop();
        _loadingTimeSeconds = 0;
    }
}